using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Briosa.Installer.Core;

public sealed record InstallationRegistration(int SchemaVersion, string InstallationId,
    string ProductDirectory, string PackageId, string ServerVersion,
    string SpatialAnalyzerTarget, string RuntimeIdentifier)
{
    public static string IdFor(string directory)
    {
        var path = Path.GetFullPath(directory).Replace('\\', '/').TrimEnd('/');
        var normalized = new string(path.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static InstallationRegistration From(InstalledPackage package)
    {
        if (package.Receipt.Package.Component != CatalogComponent.Server)
            throw new ManagementException(ManagementError.InvalidInput);
        var payload = Path.Combine(package.Directory, "payload");
        SafeFiles.NoLinks(payload);
        var manifest = InstallerJson.Read<JsonElement>(Path.Combine(payload, "manifest.json"));
        string? Text(string name) => manifest.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!manifest.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var version) || version is not (2 or 3) ||
            Text("artifactName") != package.Id || Text("briosaVersion") != package.Version ||
            Text("spatialAnalyzerTarget") != package.Receipt.Package.SpatialAnalyzerTarget ||
            Text("runtimeIdentifier") != package.Receipt.Package.RuntimeIdentifier ||
            Text("protocolPackage") != "briosa" ||
            !manifest.TryGetProperty("spatialAnalyzerBundled", out var bundled) || bundled.ValueKind != JsonValueKind.False ||
            !package.Receipt.Files.ContainsKey("Briosa.Server.exe") ||
            !package.Receipt.Files.ContainsKey("Briosa.Worker.exe"))
            throw new ManagementException(ManagementError.InvalidManifest);
        if (version == 3) ValidateCompatibility(manifest);
        foreach (var name in new[] { "manifest.json", "Briosa.Server.exe", "Briosa.Worker.exe" })
        {
            var file = Path.Combine(payload, name);
            SafeFiles.NoLinks(file);
            if (!File.Exists(file) || !package.Receipt.Files.TryGetValue(name, out var digest) ||
                digest.Length != 64 || !digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new ManagementException(ManagementError.IntegrityFailure);
        }
        using (var input = File.OpenRead(Path.Combine(payload, "manifest.json")))
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != package.Receipt.Files["manifest.json"])
                throw new ManagementException(ManagementError.IntegrityFailure);
        return new(1, IdFor(package.Directory), Path.GetFullPath(package.Directory), package.Id,
            package.Version, package.Receipt.Package.SpatialAnalyzerTarget!, package.Receipt.Package.RuntimeIdentifier);
    }

    internal static void ValidateCompatibility(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("compatibility", out var contract) || contract.ValueKind != JsonValueKind.Object ||
            !contract.TryGetProperty("major", out var major) || major.ValueKind != JsonValueKind.Number ||
            !major.TryGetUInt32(out var majorValue) || majorValue == 0 ||
            !contract.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetUInt32(out _))
            throw new ManagementException(ManagementError.InvalidManifest);
    }
}

// PackageStore has no ambient registry dependency: production composition opts in,
// while portable tests inject an in-memory implementation.
public interface IInstallationRegistry
{
    IReadOnlyList<InstallationRegistration> Read(bool machine);
    void Write(bool machine, InstallationRegistration registration);
    void Remove(bool machine, string installationId);
}

public sealed class WindowsInstallationRegistry : IInstallationRegistry
{
    public const string RegistryPath = @"Software\Briosa\Installations";
    private readonly string registryPath;
    public WindowsInstallationRegistry() : this(RegistryPath) { }
    internal WindowsInstallationRegistry(string registryPath) => this.registryPath = registryPath;

    public IReadOnlyList<InstallationRegistration> Read(bool machine)
    {
        if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform);
        try
        {
            using var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            using var root = hive.OpenSubKey(registryPath);
            if (root is null) return [];
            var result = new List<InstallationRegistration>();
            foreach (var id in root.GetSubKeyNames().Take(1000))
            {
                using var key = root.OpenSubKey(id);
                if (key is null || !key.GetValueNames().Contains("Registration") ||
                    key.GetValueKind("Registration") != RegistryValueKind.String) continue;
                if (key?.GetValue("Registration", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string json || json.Length > 32768) continue;
                try
                {
                    var registration = JsonSerializer.Deserialize<InstallationRegistration>(json, InstallerJson.Options);
                    if (registration is not null && registration.SchemaVersion == 1 &&
                        Path.IsPathFullyQualified(registration.ProductDirectory) &&
                        registration.InstallationId == id && id == InstallationRegistration.IdFor(registration.ProductDirectory))
                        result.Add(registration);
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
                {
                    // Malformed hints cannot become authority for a package mutation.
                }
            }
            return result;
        }
        catch (Exception exception) when (Failure(exception))
        {
            throw new ManagementException(ManagementError.RegistrationIncomplete);
        }
    }

    public void Write(bool machine, InstallationRegistration registration)
    {
        if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform);
        if (registration.InstallationId != InstallationRegistration.IdFor(registration.ProductDirectory))
            throw new ManagementException(ManagementError.InvalidInput);
        try
        {
            using var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = hive.CreateSubKey(registryPath + "\\" + registration.InstallationId, true);
            key.SetValue("Registration", JsonSerializer.Serialize(registration, InstallerJson.Options), RegistryValueKind.String);
        }
        catch (Exception exception) when (Failure(exception))
        {
            throw new ManagementException(ManagementError.RegistrationIncomplete);
        }
    }

    public void Remove(bool machine, string installationId)
    {
        if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform);
        if (installationId.Length != 64 || !installationId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ManagementException(ManagementError.InvalidInput);
        try
        {
            using var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            hive.DeleteSubKeyTree(registryPath + "\\" + installationId, false);
        }
        catch (Exception exception) when (Failure(exception))
        {
            throw new ManagementException(ManagementError.RegistrationIncomplete);
        }
    }

    private static bool Failure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}
