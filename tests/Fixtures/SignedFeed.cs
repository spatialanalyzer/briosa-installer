using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Briosa.Installer.Core;
namespace Briosa.Installer.Tests;

internal sealed class SignedFeed : IDisposable
{
    private readonly RSA key = RSA.Create(3072);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Briosa.Complete.Tests", Guid.NewGuid().ToString("N"));
    public string FeedPath => Path.Combine(Root, "feed");
    public string StorePath => Path.Combine(Root, "store");
    public string CatalogPath => Path.Combine(FeedPath, "catalog.json");
    public string PublicKey => key.ExportSubjectPublicKeyInfoPem();
    public string Hash { get; private set; } = "";
    public InstallerSettings Settings => new(CatalogPath, ServerPublisherKey: PublicKey);
    private readonly List<CatalogPackage> packages = [];
    public SignedFeed() => Directory.CreateDirectory(FeedPath);
    public CatalogPackage AddServer(string version, string? extraEntry = null, string? probeDirectory = null)
    {
        const string target = "2099.1.0101.1";
        var name = $"briosa-{version}-sa-{target}-win-x64";
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, artifactName = name, briosaVersion = version,
            runtimeIdentifier = "win-x64", spatialAnalyzerTarget = target, protocolPackage = "briosa", spatialAnalyzerBundled = false }, InstallerJson.Options);
        var provenance = Path.Combine(FeedPath, name + ".provenance.json");
        File.WriteAllBytes(provenance, manifest);
        var zipPath = Path.Combine(FeedPath, name + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = zip.CreateEntry(name + "/manifest.json").Open()) writer.Write(manifest);
            foreach (var file in new[] { "Briosa.Server.exe", "Briosa.Worker.exe" })
            { using var writer = new StreamWriter(zip.CreateEntry(name + "/" + file).Open()); writer.Write("Inert test content; never executed."); }
            if (extraEntry is not null) { using var writer = new StreamWriter(zip.CreateEntry(name + "/" + extraEntry).Open()); writer.Write("Unsafe fixture"); }
            if (probeDirectory is not null)
                foreach (var file in Directory.EnumerateFiles(probeDirectory))
                    zip.CreateEntryFromFile(file, name + "/" + Path.GetFileName(file));
        }
        var package = new CatalogPackage(name, CatalogComponent.Server, version, "win-x64", target, Reference(zipPath), Reference(provenance));
        packages.Add(package);
        return package;
    }
    private static ArtifactReference Reference(string path) => new(Path.GetFileName(path), new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    public CatalogPackage AddInstaller(string version)
    {
        var name = $"briosa-installer-{version}-win-x64";
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, component = "installer", artifactName = name, briosaVersion = version, runtimeIdentifier = "win-x64" }, InstallerJson.Options);
        var provenance = Path.Combine(FeedPath, name + ".provenance.json"); File.WriteAllBytes(provenance, manifest);
        var zipPath = Path.Combine(FeedPath, name + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = zip.CreateEntry(name + "/manifest.json").Open()) writer.Write(manifest);
            foreach (var file in new[] { "Briosa.Installer.exe", "Briosa.Installer.Cli.exe", "Briosa.Launcher.exe" })
            { using var writer = new StreamWriter(zip.CreateEntry(name + "/" + file).Open()); writer.Write("Inert installer fixture. Never executed."); }
        }
        var package = new CatalogPackage(name, CatalogComponent.Installer, version, "win-x64", null, Reference(zipPath), Reference(provenance));
        packages.Add(package); return package;
    }
    public void Publish(long? issued = null)
    {
        var rows = packages.Select(p =>
        {
            var row = new Dictionary<string, object?> { ["id"] = p.Id, ["component"] = p.ComponentName, ["version"] = p.Version,
                ["runtimeIdentifier"] = p.RuntimeIdentifier, ["artifact"] = p.Artifact, ["provenance"] = p.Provenance };
            if (p.Component == CatalogComponent.Server) row["spatialAnalyzerTarget"] = p.SpatialAnalyzerTarget;
            return row;
        });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, packages = rows }, InstallerJson.Options);
        File.WriteAllBytes(CatalogPath, bytes);
        Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var time = issued ?? DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var expires = time + 7 * 86400;
        var signature = key.SignData(PublisherTrust.SigningBytes(Hash, time, expires), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        InstallerJson.Write(CatalogPath + ".signature.json", new CatalogSignature(1, PublisherTrust.Algorithm, Hash, time, expires, Convert.ToBase64String(signature)));
    }
    public void Dispose()
    {
        key.Dispose();
        SafeFiles.DeleteTree(Path.Combine(Path.GetTempPath(), "Briosa.Complete.Tests"), Root);
    }
}
