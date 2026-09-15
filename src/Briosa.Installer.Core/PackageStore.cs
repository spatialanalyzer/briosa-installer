using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Briosa.Installer.Core;

public sealed record PackageReceipt(int SchemaVersion, CatalogPackage Package, string CatalogSha256,
    PublisherProof Publisher, DateTimeOffset InstalledAt, Dictionary<string, string> Files);
public sealed record InstalledPackage(PackageReceipt Receipt, string Directory)
{
    public string Id => Receipt.Package.Id;
    public string Version => Receipt.Package.Version;
    public string Target => Receipt.Package.TargetDisplay;
    public string Component => Receipt.Package.ComponentName;
    public bool HasControlCenter => Receipt.Package.Component == CatalogComponent.Server &&
        Receipt.Files.ContainsKey("Briosa.ControlCenter.exe");
}
public sealed record PackageProgress(string Phase, long Bytes = 0, long Total = 0);
public sealed record RecoveryJournal(string Id, string Operation);

public sealed class PackageStore
{
    public static string UserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Briosa", "Packages");
    public static string MachineRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Briosa", "Packages");
    public string Root { get; }
    private readonly ICredentialStore? credentials;
    private readonly HttpMessageHandler? handler;
    public PackageStore(string? root = null, ICredentialStore? credentials = null, HttpMessageHandler? handler = null)
    { Root = Path.GetFullPath(root ?? UserRoot); this.credentials = credentials; this.handler = handler; }
    private string Products => SafeFiles.Child(Root, "products");
    private string Transactions => SafeFiles.Child(Root, "transactions");
    private bool IsMachineStore => Root.TrimEnd('\\', '/').Equals(MachineRoot, StringComparison.OrdinalIgnoreCase);
    private string Product(string id)
    {
        if (!ReleaseCatalogCodec.IsSafeReference(id) || id.Contains('/') || id.Length > 200) throw new ManagementException(ManagementError.InvalidInput);
        return SafeFiles.Child(Products, id);
    }
    private IDisposable Lock()
    {
        SafeFiles.NoLinks(Root);
        if (IsMachineStore && OperatingSystem.IsWindows())
        {
            WindowsStoreProtection.EnsureDirectory(Path.GetDirectoryName(Root)!);
            WindowsStoreProtection.EnsureDirectory(Root);
            WindowsStoreProtection.EnsureDirectory(Products);
            WindowsStoreProtection.EnsureDirectory(Transactions);
        }
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Products);
        Directory.CreateDirectory(Transactions);
        SafeFiles.NoLinks(Products); SafeFiles.NoLinks(Transactions);
        try { return new FileStream(Path.Combine(Root, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ManagementException(ManagementError.StoreBusy); }
    }
    public IReadOnlyList<InstalledPackage> List()
    {
        SafeFiles.NoLinks(Root);
        if (!Directory.Exists(Products)) return [];
        if (IsMachineStore && OperatingSystem.IsWindows())
        {
            foreach (var folder in new[] { Path.GetDirectoryName(Root)!, Root, Products })
                WindowsStoreProtection.Validate(new DirectoryInfo(folder).GetAccessControl());
        }
        var result = new List<InstalledPackage>();
        foreach (var directory in Directory.EnumerateDirectories(Products))
        {
            SafeFiles.NoLinks(directory);
            if (IsMachineStore && OperatingSystem.IsWindows()) WindowsStoreProtection.ValidateTree(directory);
            var receipt = InstallerJson.Read<PackageReceipt>(Path.Combine(directory, "receipt.json"), 8 * 1024 * 1024);
            if (receipt.SchemaVersion != 1 || receipt.Package is null || receipt.Package.Id != Path.GetFileName(directory) ||
                receipt.Package.Artifact is null || receipt.Files is null || receipt.Files.Count == 0 ||
                receipt.Publisher is null || receipt.Publisher.Fingerprint is not { Length: 64 })
                throw new ManagementException(ManagementError.InvalidManifest);
            result.Add(new(receipt, directory));
        }
        return result.OrderBy(p => p.Target, StringComparer.Ordinal).ThenBy(p => p.Version, StringComparer.Ordinal).ToList();
    }
    public async Task VerifyAsync(string id, CancellationToken token = default)
    {
        using var held = ReadLock();
        await VerifyUnlockedAsync(id, token).ConfigureAwait(false);
    }

    public async Task<string> ResolveControlCenterAsync(string id, CancellationToken token = default)
    {
        using var held = ReadLock();
        RequireRecovered();
        var product = List().SingleOrDefault(p => p.Id == id) ?? throw new ManagementException(ManagementError.PackageNotFound);
        if (!product.HasControlCenter) throw new ManagementException(ManagementError.InvalidInput);
        EnterprisePolicy.Load()?.ValidateInstalled(product.Receipt.Publisher.Fingerprint, Root);
        await VerifyUnlockedAsync(id, token).ConfigureAwait(false);
        return SafeFiles.Child(product.Directory, "payload/Briosa.ControlCenter.exe");
    }
    private IDisposable ReadLock()
    {
        SafeFiles.NoLinks(Root);
        try { return new FileStream(Path.Combine(Root, "store.lock"), FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { throw new ManagementException(ManagementError.PackageNotFound); }
        catch (DirectoryNotFoundException) { throw new ManagementException(ManagementError.PackageNotFound); }
        catch (IOException) { throw new ManagementException(ManagementError.StoreBusy); }
    }
    private async Task VerifyUnlockedAsync(string id, CancellationToken token)
    {
        var product = List().SingleOrDefault(p => p.Id == id) ?? throw new ManagementException(ManagementError.PackageNotFound);
        var payload = Path.Combine(product.Directory, "payload");
        var actual = SafeFiles.Files(payload).Select(p => Path.GetRelativePath(payload, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(product.Receipt.Files.Keys)) throw new ManagementException(ManagementError.IntegrityFailure);
        foreach (var (relative, expected) in product.Receipt.Files)
        {
            if (!ReleaseCatalogCodec.IsSafeReference(relative)) throw new ManagementException(ManagementError.InvalidManifest);
            await using var input = File.OpenRead(SafeFiles.Child(payload, relative));
            if (Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).ToLowerInvariant() != expected)
                throw new ManagementException(ManagementError.IntegrityFailure);
        }
    }
    public async Task<InstalledPackage> InstallAsync(InstallerSettings settings, CatalogComponent component, string id,
        string expectedCatalogHash, bool repair = false, IProgress<PackageProgress>? progress = null, CancellationToken token = default,
        Func<bool>? configurationStillMatches = null)
    {
        var source = settings.Source(component);
        EnterprisePolicy.Load()?.Validate(source, Root);
        using var held = Lock();
        RequireRecovered();
        var destination = Product(id);
        var existing = List().SingleOrDefault(p => p.Id == id);
        if (!repair && existing is not null) throw new ManagementException(ManagementError.AlreadyInstalled);
        if (repair && existing is null) throw new ManagementException(ManagementError.PackageNotFound);
        progress?.Report(new("Verifying catalog"));
        using var reader = new ReleaseCatalogClient(credentials: credentials);
        // The injectable handler is shared by the source transport for deterministic tests.
        using var testReader = handler is null ? null : new ReleaseCatalogClient(new BorrowedHandler(handler), credentials: credentials);
        var read = await (testReader ?? reader).ReadAsync(settings, component, token).ConfigureAwait(false);
        if (read is CatalogResult<CatalogSnapshot>.Failure failure) throw new ManagementException(failure.Error.Code switch
        {
            CatalogError.Expired => ManagementError.ExpiredCatalog,
            CatalogError.VerificationFailed => ManagementError.InvalidSignature,
            CatalogError.AuthenticationRequired => ManagementError.AuthenticationRequired,
            CatalogError.PolicyDenied => ManagementError.PolicyDenied,
            CatalogError.Cancelled => ManagementError.Cancelled,
            CatalogError.TimedOut => ManagementError.TimedOut,
            _ => ManagementError.SourceUnavailable,
        });
        var catalog = ((CatalogResult<CatalogSnapshot>.Success)read).Value;
        if (catalog.Publisher is null) throw new ManagementException(ManagementError.UntrustedPublisher);
        if (catalog.ContentSha256 != expectedCatalogHash) throw new ManagementException(ManagementError.InvalidInput);
        var package = catalog.Packages.SingleOrDefault(p => p.Id == id) ?? throw new ManagementException(ManagementError.PackageNotFound);
        if (existing is not null && (existing.Receipt.Package != package || existing.Receipt.Publisher.Fingerprint != catalog.Publisher.Fingerprint))
            throw new ManagementException(ManagementError.IntegrityFailure);
        var historyPath = Path.Combine(Root, "catalog-history.json");
        var history = File.Exists(historyPath) ? InstallerJson.Read<Dictionary<string, long>>(historyPath) : [];
        var historyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Catalog + "\n" + catalog.Publisher.Fingerprint)));
        if (history.TryGetValue(historyKey, out var issued) && issued > catalog.Publisher.IssuedAt) throw new ManagementException(ManagementError.CatalogRollback);
        history[historyKey] = catalog.Publisher.IssuedAt;
        InstallerJson.Write(historyPath, history);
        if (IsMachineStore && OperatingSystem.IsWindows()) WindowsStoreProtection.SealFile(historyPath);
        var transaction = SafeFiles.Child(Transactions, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        InstallerJson.Write(Path.Combine(transaction, "journal.json"), new RecoveryJournal(id, repair ? "repair" : "install"));
        var staged = Path.Combine(transaction, "new");
        Directory.CreateDirectory(staged);
        var committed = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(30));
            using var transport = new SourceTransport(source, credentials, handler);
            var archive = Path.Combine(transaction, "package.zip");
            progress?.Report(new("Downloading and verifying package", 0, package.Artifact.Size));
            await transport.DownloadAsync(package.Artifact, archive,
                progress is null ? null : new InlineProgress<long>(value => progress.Report(new("Downloading package", value, package.Artifact.Size))), deadline.Token).ConfigureAwait(false);
            string? provenance = null;
            if (package.Provenance is not null)
            {
                if (package.Provenance.Size > 1024 * 1024) throw new ManagementException(ManagementError.LimitExceeded);
                provenance = Path.Combine(transaction, "provenance.json");
                await transport.DownloadAsync(package.Provenance, provenance, null, deadline.Token).ConfigureAwait(false);
            }
            if (IsMachineStore && OperatingSystem.IsWindows())
            {
                WindowsStoreProtection.SealTree(transaction);
                await using var verifiedInput = File.OpenRead(archive);
                if (Convert.ToHexString(await SHA256.HashDataAsync(verifiedInput, deadline.Token).ConfigureAwait(false)).ToLowerInvariant() != package.Artifact.Sha256)
                    throw new ManagementException(ManagementError.IntegrityFailure);
            }
            progress?.Report(new("Validating package contents"));
            var files = await PackageArchive.ExtractAsync(archive, Path.Combine(staged, "payload"), package, provenance, deadline.Token).ConfigureAwait(false);
            var receipt = new PackageReceipt(1, package, catalog.ContentSha256, catalog.Publisher, DateTimeOffset.UtcNow, files);
            InstallerJson.Write(Path.Combine(staged, "receipt.json"), receipt);
            if (IsMachineStore && OperatingSystem.IsWindows()) WindowsStoreProtection.SealTree(staged);
            deadline.Token.ThrowIfCancellationRequested();
            if (configurationStillMatches is not null && !configurationStillMatches()) throw new ManagementException(ManagementError.InvalidInput);
            EnterprisePolicy.Load()?.Validate(source, Root);
            SafeFiles.NoLinks(destination);
            progress?.Report(new("Committing package"));
            // Cancellation ends at the commit boundary; the following renames complete or recover.
            if (existing is not null)
            {
                // Windows cannot rename a parent while these probe handles are open.
                // Close the probes immediately before the rename; rename itself may reject new users.
                using (SafeFiles.LockFiles(destination)) { }
                try { Directory.Move(destination, Path.Combine(transaction, "old")); }
                catch (IOException) { throw new ManagementException(ManagementError.InUse); }
            }
            try { Directory.Move(staged, destination); committed = true; }
            catch
            {
                if (existing is not null && !Directory.Exists(destination)) Directory.Move(Path.Combine(transaction, "old"), destination);
                throw;
            }
            progress?.Report(new("Package installed"));
            return new(receipt, destination);
        }
        catch (OperationCanceledException) { throw new ManagementException(token.IsCancellationRequested ? ManagementError.Cancelled : ManagementError.TimedOut); }
        finally
        {
            if (committed || !Directory.Exists(Path.Combine(transaction, "old")))
                try { SafeFiles.DeleteTree(Transactions, transaction); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* A complete package remains usable; Recover cleans retained transaction files. */ }
        }
    }
    public void Remove(string id)
    {
        EnterprisePolicy.Load()?.ValidateScope(Root);
        using var held = Lock();
        RequireRecovered();
        var destination = Product(id);
        if (!Directory.Exists(destination)) throw new ManagementException(ManagementError.PackageNotFound);
        var activePath = Path.Combine(Root, "active-installer.json");
        if (File.Exists(activePath) && InstallerJson.Read<ActiveInstaller>(activePath).Id == id) throw new ManagementException(ManagementError.InUse);
        var transaction = SafeFiles.Child(Transactions, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        InstallerJson.Write(Path.Combine(transaction, "journal.json"), new RecoveryJournal(id, "remove"));
        try
        {
            using (SafeFiles.LockFiles(destination)) { }
            try { Directory.Move(destination, Path.Combine(transaction, "old")); }
            catch (IOException) { throw new ManagementException(ManagementError.InUse); }
            SafeFiles.DeleteTree(Transactions, transaction);
        }
        catch
        {
            if (!Directory.Exists(Path.Combine(transaction, "old"))) SafeFiles.DeleteTree(Transactions, transaction);
            throw;
        }
    }
    private void RequireRecovered()
    { if (Directory.EnumerateDirectories(Transactions).Any()) throw new ManagementException(ManagementError.RecoveryRequired); }
    public void Recover()
    {
        EnterprisePolicy.Load()?.ValidateScope(Root);
        using var held = Lock();
        foreach (var transaction in Directory.EnumerateDirectories(Transactions))
        {
            if (!Guid.TryParseExact(Path.GetFileName(transaction), "N", out _)) throw new ManagementException(ManagementError.RecoveryRequired);
            SafeFiles.NoLinks(transaction);
            var journalPath = Path.Combine(transaction, "journal.json");
            if (!File.Exists(journalPath)) { SafeFiles.DeleteTree(Transactions, transaction); continue; }
            var journal = InstallerJson.Read<RecoveryJournal>(journalPath);
            if (journal.Operation is not ("install" or "repair" or "remove")) throw new ManagementException(ManagementError.RecoveryRequired);
            var destination = Product(journal.Id);
            var old = Path.Combine(transaction, "old");
            if (journal.Operation == "repair" && !Directory.Exists(destination) && Directory.Exists(old))
            {
                SafeFiles.NoLinks(destination); _ = SafeFiles.Files(old);
                Directory.Move(old, destination);
            }
            SafeFiles.DeleteTree(Transactions, transaction);
        }
    }
    public async Task ActivateInstallerAsync(string id, CancellationToken token = default)
    {
        using var held = Lock();
        RequireRecovered();
        var package = List().SingleOrDefault(p => p.Id == id) ?? throw new ManagementException(ManagementError.PackageNotFound);
        if (package.Receipt.Package.Component != CatalogComponent.Installer) throw new ManagementException(ManagementError.InvalidInput);
        EnterprisePolicy.Load()?.ValidateInstalled(package.Receipt.Publisher.Fingerprint, Root);
        await VerifyUnlockedAsync(id, token).ConfigureAwait(false);
        InstallerJson.Write(Path.Combine(Root, "active-installer.json"), new ActiveInstaller(1, id));
        if (IsMachineStore && OperatingSystem.IsWindows()) WindowsStoreProtection.SealFile(Path.Combine(Root, "active-installer.json"));
    }
    // A completed conventional setup may supersede an older selection. Newer
    // selections and all immutable products stay intact; later explicit rollback
    // remains possible through ActivateInstallerAsync.
    public bool PreferBundledInstaller(string version)
    {
        if (!ReleaseVersion.IsValid(version)) throw new ManagementException(ManagementError.InvalidInput);
        SafeFiles.NoLinks(Root);
        if (!Directory.Exists(Root)) return false;
        using var held = Lock();
        RequireRecovered();
        var pointer = SafeFiles.Child(Root, "active-installer.json");
        SafeFiles.NoLinks(pointer);
        if (!File.Exists(pointer)) return false;
        var selected = InstallerJson.Read<ActiveInstaller>(pointer);
        if (selected.SchemaVersion != 1) throw new ManagementException(ManagementError.InvalidManifest);
        var package = List().SingleOrDefault(p => p.Id == selected.Id && p.Receipt.Package.Component == CatalogComponent.Installer)
            ?? throw new ManagementException(ManagementError.PackageNotFound);
        EnterprisePolicy.Load()?.ValidateInstalled(package.Receipt.Publisher.Fingerprint, Root);
        if (ReleaseVersion.Compare(package.Version, version) >= 0) return false;
        File.Delete(pointer);
        return true;
    }
    // Selection metadata is useful for UI status; launching still requires ResolveActiveInstallerAsync verification.
    public InstalledPackage? ReadInstallerSelection()
    {
        SafeFiles.NoLinks(Root);
        var pointer = Path.Combine(Root, "active-installer.json");
        if (!File.Exists(pointer)) return null;
        SafeFiles.NoLinks(pointer);
        using var held = ReadLock();
        var active = InstallerJson.Read<ActiveInstaller>(pointer);
        if (active.SchemaVersion != 1) throw new ManagementException(ManagementError.InvalidManifest);
        return List().SingleOrDefault(p => p.Id == active.Id && p.Receipt.Package.Component == CatalogComponent.Installer)
            ?? throw new ManagementException(ManagementError.PackageNotFound);
    }
    public async Task<string?> ResolveActiveInstallerAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(Root)) return null;
        var pointer = Path.Combine(Root, "active-installer.json");
        if (!File.Exists(pointer)) return null;
        using var held = ReadLock();
        var active = InstallerJson.Read<ActiveInstaller>(pointer);
        if (active.SchemaVersion != 1) throw new ManagementException(ManagementError.InvalidManifest);
        var product = List().SingleOrDefault(p => p.Id == active.Id && p.Receipt.Package.Component == CatalogComponent.Installer)
            ?? throw new ManagementException(ManagementError.PackageNotFound);
        EnterprisePolicy.Load()?.ValidateInstalled(product.Receipt.Publisher.Fingerprint, Root);
        await VerifyUnlockedAsync(active.Id, token).ConfigureAwait(false);
        return SafeFiles.Child(product.Directory, "payload/Briosa.Installer.exe");
    }
    public sealed record ActiveInstaller(int SchemaVersion, string Id);
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class BorrowedHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker = new(inner, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => invoker.SendAsync(request, token);
        protected override void Dispose(bool disposing) { if (disposing) invoker.Dispose(); base.Dispose(disposing); }
    }
}
