using System.Text.Json;
using System.Text.Json.Serialization;

namespace Briosa.Installer.Core;

public enum ManagementError
{
    InvalidInput, SourceUnavailable, AuthenticationRequired, AccessDenied, RedirectRejected,
    LimitExceeded, UntrustedPublisher, InvalidSignature, ExpiredCatalog, CatalogRollback,
    IntegrityFailure, UnsafeArchive, InvalidManifest, PackageNotFound, AlreadyInstalled,
    StoreBusy, InUse, Cancelled, TimedOut, PolicyDenied, RecoveryRequired, UnsupportedPlatform, BootstrapUpdateFailed,
    RegistrationIncomplete,
}

public sealed class ManagementException(ManagementError code) : Exception(MessageFor(code))
{
    public ManagementError Code { get; } = code;
    public static string MessageFor(ManagementError code) => code switch
    {
        ManagementError.AuthenticationRequired => "The source requires a valid credential. Update it in Settings and retry.",
        ManagementError.AccessDenied => "Access was denied. Check your repository permissions or run machine deployment from an authorized administrator terminal.",
        ManagementError.RedirectRejected => "The source redirected the request. Configure its final catalog location.",
        ManagementError.UntrustedPublisher => "Import an approved publisher public key before installing. Confirm its fingerprint through a trusted channel.",
        ManagementError.InvalidSignature => "Catalog publisher verification failed. No package was installed.",
        ManagementError.ExpiredCatalog => "The signed catalog is expired or not yet valid. Obtain a current catalog from your source.",
        ManagementError.CatalogRollback => "This catalog is older than one previously accepted from this publisher and source.",
        ManagementError.IntegrityFailure => "Downloaded or installed files do not match the verified package.",
        ManagementError.UnsafeArchive => "The archive contains an unsafe path, link, duplicate entry, or exceeds extraction limits.",
        ManagementError.InvalidManifest => "Package contents do not match the selected product identity.",
        ManagementError.PackageNotFound => "The selected package is unavailable in the catalog or package store.",
        ManagementError.AlreadyInstalled => "This exact package is already installed. Use Verify or Repair to maintain it.",
        ManagementError.StoreBusy => "Another package operation is using this store. Wait for it to finish and retry.",
        ManagementError.InUse => "Package files are in use. Stop the owning application normally, then retry.",
        ManagementError.RecoveryRequired => "An interrupted operation needs recovery. Run Recover before changing this store.",
        ManagementError.RegistrationIncomplete => "Package files were committed, but installation registration is incomplete. Run Recover or Register installations to reconcile discovery.",
        ManagementError.PolicyDenied => "Administrator policy does not permit this source, publisher, or operation.",
        ManagementError.LimitExceeded => "The download exceeds its declared size or the supported package limit.",
        ManagementError.Cancelled => "The operation was cancelled. Existing complete packages were preserved.",
        ManagementError.TimedOut => "The source operation timed out. No alternate source was contacted.",
        ManagementError.UnsupportedPlatform => "This operation requires Windows x64.",
        ManagementError.BootstrapUpdateFailed => "The installer version was selected, but its bootstrap launcher could not be refreshed. Close the launcher and retry from an authorized writable distribution folder.",
        ManagementError.InvalidInput => "The command or configuration contains invalid or unsupported values.",
        _ => "The selected source could not be read. No alternate source was contacted.",
    };
}

public static class InstallerJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    public static T Read<T>(string path, int maximumBytes = 1024 * 1024)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > maximumBytes) throw new ManagementException(ManagementError.LimitExceeded);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        UniqueProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new ManagementException(ManagementError.InvalidInput);
    }
    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ManagementException(ManagementError.InvalidInput);
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) UniqueProperties(item);
    }
    public static void Write<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
