using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Briosa.Installer.Core;

public static class PackageArchive
{
    public static async Task<Dictionary<string, string>> ExtractAsync(string archivePath, string destination, CatalogPackage package,
        string? provenancePath, CancellationToken token)
    {
        var expectedRoot = package.Component == CatalogComponent.Server
            ? $"briosa-{package.Version}-sa-{package.SpatialAnalyzerTarget}-{package.RuntimeIdentifier}"
            : $"briosa-installer-{package.Version}-{package.RuntimeIdentifier}";
        if (package.RuntimeIdentifier != "win-x64") throw new ManagementException(ManagementError.UnsupportedPlatform);
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > 20000) throw new ManagementException(ManagementError.UnsafeArchive);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            if (!ReleaseCatalogCodec.IsSafeReference(name) || !names.Add(name) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 or 0x6000 or 0x2000 or 0x1000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                (name != expectedRoot && !name.StartsWith(expectedRoot + "/", StringComparison.Ordinal)))
                throw new ManagementException(ManagementError.UnsafeArchive);
            if (entry.Length < 0 || entry.Length > 2L * 1024 * 1024 * 1024 || (total += entry.Length) > 4L * 1024 * 1024 * 1024)
                throw new ManagementException(ManagementError.UnsafeArchive);
            if (!entry.FullName.EndsWith('/'))
            {
                if (name == expectedRoot) throw new ManagementException(ManagementError.UnsafeArchive);
                paths.Add(name);
            }
        }
        foreach (var path in paths)
        {
            var parent = path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (paths.Contains(parent)) throw new ManagementException(ManagementError.UnsafeArchive);
            }
        }
        Directory.CreateDirectory(destination);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            var relative = entry.FullName[(expectedRoot.Length + 1)..];
            var outputPath = SafeFiles.Child(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if ((copied += count) > entry.Length) throw new ManagementException(ManagementError.UnsafeArchive);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            if (copied != entry.Length) throw new ManagementException(ManagementError.IntegrityFailure);
            output.Position = 0;
            hashes.Add(relative, Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false)).ToLowerInvariant());
        }
        var manifestPath = Path.Combine(destination, "manifest.json");
        if (!hashes.ContainsKey("manifest.json") || new FileInfo(manifestPath).Length > 1024 * 1024)
            throw new ManagementException(ManagementError.InvalidManifest);
        var root = InstallerJson.Read<JsonElement>(manifestPath);
        if (root.ValueKind != JsonValueKind.Object) throw new ManagementException(ManagementError.InvalidManifest);
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (Text("artifactName") != expectedRoot || Text("briosaVersion") != package.Version || Text("runtimeIdentifier") != package.RuntimeIdentifier)
            throw new ManagementException(ManagementError.InvalidManifest);
        if (package.Component == CatalogComponent.Server)
        {
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version is not (2 or 3) ||
                Text("spatialAnalyzerTarget") != package.SpatialAnalyzerTarget || Text("protocolPackage") != "briosa" ||
                !root.TryGetProperty("spatialAnalyzerBundled", out var bundled) || bundled.ValueKind != JsonValueKind.False ||
                !hashes.ContainsKey("Briosa.Server.exe") || !hashes.ContainsKey("Briosa.Worker.exe") || provenancePath is null ||
                !File.ReadAllBytes(provenancePath).AsSpan().SequenceEqual(File.ReadAllBytes(manifestPath)))
                throw new ManagementException(ManagementError.InvalidManifest);
            if (version == 3) InstallationRegistration.ValidateCompatibility(root);
        }
        else if (Text("component") != "installer" || !hashes.ContainsKey("Briosa.Installer.exe") || !hashes.ContainsKey("Briosa.Installer.Cli.exe") || !hashes.ContainsKey("Briosa.Launcher.exe") ||
            !root.TryGetProperty("schemaVersion", out var installerSchema) || installerSchema.ValueKind != JsonValueKind.Number || !installerSchema.TryGetInt32(out var installerVersion) || installerVersion != 1 ||
            (provenancePath is not null && !File.ReadAllBytes(provenancePath).AsSpan().SequenceEqual(File.ReadAllBytes(manifestPath))))
            throw new ManagementException(ManagementError.InvalidManifest);
        return hashes;
    }
}
