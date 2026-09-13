using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class PackageManagementTests
{
    [Fact]
    public async Task DoesNotRemoveAPackageContainingARunningOwnedTestProcess()
    {
        using var fixture = new SignedFeed();
        var package = fixture.AddServer("0.1.0", probeDirectory: Path.Combine(AppContext.BaseDirectory, "InUseProbe")); fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        var installed = await store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash);
        var executable = Path.Combine(installed.Directory, "payload", "Briosa.InUseProbe.exe");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(ManagementError.InUse, Assert.Throws<ManagementException>(() => store.Remove(package.Id)).Code);
            Assert.False(process.HasExited);
            Assert.Single(store.List());
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await store.VerifyAsync(package.Id);
        store.Remove(package.Id);
        Assert.Empty(store.List());
    }
    [Fact]
    public async Task InstallerSelectionVerifiesFilesAndPreservesServerInventory()
    {
        using var fixture = new SignedFeed();
        var server = fixture.AddServer("0.1.0");
        var installer = fixture.AddInstaller("0.2.0");
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, server.Id, fixture.Hash);
        var installed = await store.InstallAsync(fixture.Settings, CatalogComponent.Installer, installer.Id, fixture.Hash);
        await store.ActivateInstallerAsync(installer.Id);
        Assert.Equal(Path.Combine(installed.Directory, "payload", "Briosa.Installer.exe"), await store.ResolveActiveInstallerAsync());
        Assert.Equal(ManagementError.InUse, Assert.Throws<ManagementException>(() => store.Remove(installer.Id)).Code);
        Assert.Equal(2, store.List().Count);
        await store.VerifyAsync(server.Id);
        File.WriteAllText(Path.Combine(installed.Directory, "payload", "Briosa.Installer.exe"), "damaged");
        Assert.Equal(ManagementError.IntegrityFailure, (await Assert.ThrowsAsync<ManagementException>(() => store.ResolveActiveInstallerAsync())).Code);
    }
    [Fact]
    public async Task ChangedConfigurationPreventsCommitAfterPayloadVerification()
    {
        using var fixture = new SignedFeed();
        var package = fixture.AddServer("0.1.0"); fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        Assert.Equal(ManagementError.InvalidInput, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash, configurationStillMatches: () => false))).Code);
        Assert.Empty(store.List());
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.StorePath, "transactions")));
    }
    [Fact]
    public void WindowsCredentialVaultKeepsExactSourcesSeparateAndDeletesTheFixture()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = "https://credential-test.invalid/" + Guid.NewGuid().ToString("N") + "/catalog.json";
        var vault = new WindowsCredentialStore();
        try
        {
            vault.Save(source, new("test-user", "inert-test-token"));
            Assert.Equal(new SourceCredential("test-user", "inert-test-token"), vault.Read(source));
            Assert.Null(vault.Read(source + ".other"));
        }
        finally { vault.Delete(source); }
        Assert.Null(vault.Read(source));
    }
    [Fact]
    public async Task InstallsSideBySideVerifiesRepairsAndRemovesOneExactProduct()
    {
        using var fixture = new SignedFeed();
        var first = fixture.AddServer("0.1.0");
        var second = fixture.AddServer("0.2.0");
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, first.Id, fixture.Hash);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, second.Id, fixture.Hash);
        Assert.Equal(2, store.List().Count);
        await store.VerifyAsync(first.Id);
        var file = Path.Combine(store.List().Single(p => p.Id == first.Id).Directory, "payload", "Briosa.Server.exe");
        File.WriteAllText(file, "damaged");
        Assert.Equal(ManagementError.IntegrityFailure, (await Assert.ThrowsAsync<ManagementException>(() => store.VerifyAsync(first.Id))).Code);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, first.Id, fixture.Hash, repair: true);
        await store.VerifyAsync(first.Id);
        store.Remove(first.Id);
        Assert.Equal(second.Id, Assert.Single(store.List()).Id);
        await store.VerifyAsync(second.Id);
    }
    [Fact]
    public async Task RejectsUntrustedTamperedAndChangedPlansWithoutReplacingExistingProducts()
    {
        using var fixture = new SignedFeed();
        var first = fixture.AddServer("0.1.0");
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        Assert.Equal(ManagementError.UntrustedPublisher, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings with { ServerPublisherKey = null }, CatalogComponent.Server, first.Id, fixture.Hash))).Code);
        Assert.Empty(store.List());
        Assert.Equal(ManagementError.InvalidInput, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings, CatalogComponent.Server, first.Id, new string('0', 64)))).Code);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, first.Id, fixture.Hash);
        var second = fixture.AddServer("0.2.0");
        fixture.Publish();
        File.WriteAllText(Path.Combine(fixture.FeedPath, second.Artifact.Path), "corrupted download");
        Assert.Equal(ManagementError.IntegrityFailure, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings, CatalogComponent.Server, second.Id, fixture.Hash))).Code);
        Assert.Equal(first.Id, Assert.Single(store.List()).Id);
        await store.VerifyAsync(first.Id);
    }
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder/CON.txt")]
    [InlineData("folder/other:stream")]
    [InlineData("folder\\escape.txt")]
    public async Task RejectsUnsafeArchivesBeforePublishingAnyDirectory(string path)
    {
        using var fixture = new SignedFeed();
        var package = fixture.AddServer("0.1.0", path);
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        Assert.Equal(ManagementError.UnsafeArchive, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash))).Code);
        Assert.Empty(store.List());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape.txt")));
    }
    [Fact]
    public async Task RejectsInUseRemovalAndRepairsWithoutChangingTheProduct()
    {
        using var fixture = new SignedFeed();
        var package = fixture.AddServer("0.1.0");
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        var installed = await store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash);
        using (var inUse = new FileStream(Path.Combine(installed.Directory, "payload", "Briosa.Server.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(ManagementError.InUse, Assert.Throws<ManagementException>(() => store.Remove(package.Id)).Code);
            Assert.Equal(ManagementError.InUse, (await Assert.ThrowsAsync<ManagementException>(() =>
                store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash, repair: true))).Code);
        }
        await store.VerifyAsync(package.Id);
    }
    [Fact]
    public async Task RecoveryRestoresAnOldPackageAfterInterruptedRepair()
    {
        using var fixture = new SignedFeed();
        var package = fixture.AddServer("0.1.0");
        fixture.Publish();
        var store = new PackageStore(fixture.StorePath);
        var installed = await store.InstallAsync(fixture.Settings, CatalogComponent.Server, package.Id, fixture.Hash);
        var transaction = Path.Combine(fixture.StorePath, "transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        InstallerJson.Write(Path.Combine(transaction, "journal.json"), new RecoveryJournal(package.Id, "repair"));
        Directory.Move(installed.Directory, Path.Combine(transaction, "old"));
        Assert.Equal(ManagementError.RecoveryRequired, Assert.Throws<ManagementException>(() => store.Remove(package.Id)).Code);
        store.Recover();
        await store.VerifyAsync(package.Id);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.StorePath, "transactions")));
    }
    [Fact]
    public async Task RejectsOldCatalogsAfterANewerOneWasAccepted()
    {
        using var fixture = new SignedFeed();
        var first = fixture.AddServer("0.1.0");
        var second = fixture.AddServer("0.2.0");
        fixture.Publish(issued: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
        var store = new PackageStore(fixture.StorePath);
        await store.InstallAsync(fixture.Settings, CatalogComponent.Server, first.Id, fixture.Hash);
        fixture.Publish(issued: DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds());
        Assert.Equal(ManagementError.CatalogRollback, (await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(fixture.Settings, CatalogComponent.Server, second.Id, fixture.Hash))).Code);
        await store.VerifyAsync(first.Id);
    }
    [Fact]
    public void SignaturesBindThePayloadDigestAndValidityWindow()
    {
        using var fixture = new SignedFeed();
        fixture.AddServer("0.1.0"); fixture.Publish();
        var bytes = File.ReadAllBytes(fixture.CatalogPath + ".signature.json");
        Assert.Equal(PublisherTrust.Fingerprint(fixture.PublicKey), PublisherTrust.Verify(fixture.PublicKey, fixture.Hash, bytes, DateTimeOffset.UtcNow).Fingerprint);
        Assert.Equal(ManagementError.InvalidSignature, Assert.Throws<ManagementException>(() => PublisherTrust.Verify(fixture.PublicKey, new string('0', 64), bytes, DateTimeOffset.UtcNow)).Code);
        Assert.Equal(ManagementError.ExpiredCatalog, Assert.Throws<ManagementException>(() => PublisherTrust.Verify(fixture.PublicKey, fixture.Hash, bytes, DateTimeOffset.UtcNow.AddDays(20))).Code);
        using var another = RSA.Create(3072);
        Assert.Equal(ManagementError.InvalidSignature, Assert.Throws<ManagementException>(() => PublisherTrust.Verify(another.ExportSubjectPublicKeyInfoPem(), fixture.Hash, bytes, DateTimeOffset.UtcNow)).Code);
    }
    [Fact]
    public async Task UsesOnlyTheChosenSourcesCredentialForCatalogAndPayload()
    {
        var credentials = new FakeCredentials();
        const string server = "https://server.example.test/feed/catalog.json";
        const string updater = "https://updater.example.test/approved/catalog.json";
        credentials.Save(server, new("", "server-token"));
        credentials.Save(updater, new("", "updater-token"));
        var requests = new List<(string Uri, string? Token)>();
        using var handler = new ReplyHandler(request =>
        {
            requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.Parameter));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":1,\"packages\":[]}") };
        });
        using var client = new ReleaseCatalogClient(handler, credentials: credentials);
        var settings = new InstallerSettings(server, updater, "bearer", "bearer");
        Assert.IsType<CatalogResult<CatalogSnapshot>.Success>(await client.ReadAsync(settings, CatalogComponent.Installer));
        Assert.Equal((updater, "updater-token"), Assert.Single(requests));
        Assert.Equal(settings, Assert.IsType<Outcome<InstallerSettings>.Success>(SettingsCodec.Parse(SettingsCodec.Serialize(settings))).Value);
    }
    [Fact]
    public void PolicyRejectsSiblingOriginsAndPathsAndWrongPublishers()
    {
        using var fixture = new SignedFeed();
        var policy = new EnterprisePolicy(1, ["https://mirror.example.test/approved/"], [PublisherTrust.Fingerprint(fixture.PublicKey)]);
        policy.Validate(new("https://mirror.example.test/approved/catalog.json", PublisherKey: fixture.PublicKey));
        Assert.Throws<ManagementException>(() => policy.Validate(new("https://mirror.example.test/approved-other/catalog.json", PublisherKey: fixture.PublicKey)));
        Assert.Throws<ManagementException>(() => policy.Validate(new("https://public.example.test/approved/catalog.json", PublisherKey: fixture.PublicKey)));
        Assert.Throws<ManagementException>(() => policy.Validate(new("https://mirror.example.test/approved/catalog.json")));
    }
    private sealed class FakeCredentials : ICredentialStore
    {
        private readonly Dictionary<string, SourceCredential> values = [];
        public SourceCredential? Read(string catalog) => values.GetValueOrDefault(catalog);
        public void Save(string catalog, SourceCredential credential) => values[catalog] = credential;
        public void Delete(string catalog) => values.Remove(catalog);
    }
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request));
    }
}
