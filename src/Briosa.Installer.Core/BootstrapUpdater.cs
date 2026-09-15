using System.Security.Cryptography;
using System.Text.Json;

namespace Briosa.Installer.Core;

public static class BootstrapUpdater
{
    public static async Task RefreshAsync(PackageStore store, string id, string launcherPath, CancellationToken token = default)
    {
        var destination = Path.GetFullPath(launcherPath);
        if (!Path.GetFileName(destination).Equals("Briosa.Launcher.exe", StringComparison.OrdinalIgnoreCase)) throw new ManagementException(ManagementError.InvalidInput);
        SafeFiles.NoLinks(destination);
        var folder = Path.GetDirectoryName(destination)!;
        // Managed payloads stay immutable. Only an existing standalone distribution is refreshed.
        if (File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "receipt.json"))) throw new ManagementException(ManagementError.InvalidInput);
        var manifestPath = Path.Combine(folder, "manifest.json");
        SafeFiles.NoLinks(manifestPath);
        var manifest = InstallerJson.Read<JsonElement>(manifestPath);
        if (manifest.ValueKind != JsonValueKind.Object || !manifest.TryGetProperty("component", out var component) ||
            component.ValueKind != JsonValueKind.String || component.GetString() != "installer" || !File.Exists(destination))
            throw new ManagementException(ManagementError.InvalidManifest);
        await store.VerifyAsync(id, token).ConfigureAwait(false);
        var package = store.List().SingleOrDefault(p => p.Id == id && p.Receipt.Package.Component == CatalogComponent.Installer)
            ?? throw new ManagementException(ManagementError.PackageNotFound);
        var source = SafeFiles.Child(package.Directory, "payload/Briosa.Launcher.exe");
        EnterprisePolicy.Load()?.ValidateInstalled(package.Receipt.Publisher.Fingerprint, store.Root);
        SafeFiles.NoLinks(source);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, token).ConfigureAwait(false);
                output.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false)).ToLowerInvariant();
                if (hash != package.Receipt.Files["Briosa.Launcher.exe"]) throw new ManagementException(ManagementError.IntegrityFailure);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            SafeFiles.NoLinks(destination);
            File.Replace(temporary, destination, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new ManagementException(ManagementError.BootstrapUpdateFailed); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
