using System.Text.Json;
using System.Text.Json.Nodes;

namespace Briosa.Installer.Core;

public enum ConfigurationError
{
    InvalidJson,
    UnsupportedSchema,
    InvalidProperties,
    InvalidCatalog,
    MissingFile,
    ReadFailed,
    WriteFailed,
    SaveConflict,
    FileTooLarge,
    InvalidTheme,
}

public sealed record ConfigurationFailure(ConfigurationError Code)
{
    public string Message => Code switch
    {
        ConfigurationError.InvalidJson => "Settings must contain valid JSON.",
        ConfigurationError.UnsupportedSchema => "This settings schema version is not supported.",
        ConfigurationError.InvalidProperties => "Settings contain missing, duplicate, unknown, or incorrectly typed fields.",
        ConfigurationError.InvalidCatalog => "Use an HTTPS catalog URL without credentials, a query, or a fragment, or an absolute Windows file/share path.",
        ConfigurationError.MissingFile => "The explicitly selected settings file does not exist. Use settings init to create a new file.",
        ConfigurationError.ReadFailed => "Settings could not be read. Check file access and encoding.",
        ConfigurationError.WriteFailed => "Settings could not be saved. Check directory access and whether another editor is saving.",
        ConfigurationError.SaveConflict => "Settings changed since they were loaded. Reload before saving; the file was not overwritten.",
        ConfigurationError.FileTooLarge => "The settings file exceeds the 1 MiB limit.",
        ConfigurationError.InvalidTheme => "Choose system, light, or dark for the appearance theme.",
        _ => "Settings could not be processed.",
    };
}

public abstract record Outcome<T>
{
    private Outcome() { }
    public sealed record Success(T Value) : Outcome<T>;
    public sealed record Failure(ConfigurationFailure Error) : Outcome<T>;
    public static Outcome<T> Fail(ConfigurationError code) => new Failure(new(code));
}

public sealed record InstallerSettings(string ServerCatalog, string? InstallerCatalog = null,
    string ServerAuthentication = "anonymous", string InstallerAuthentication = "anonymous",
    string? ServerPublisherKey = null, string? InstallerPublisherKey = null, string Theme = "system")
{
    public string EffectiveInstallerCatalog => InstallerCatalog ?? ServerCatalog;
    public SourceSettings Source(CatalogComponent component) => component == CatalogComponent.Server || InstallerCatalog is null
        ? new(ServerCatalog, ServerAuthentication, ServerPublisherKey)
        : new(InstallerCatalog, InstallerAuthentication, InstallerPublisherKey);
}

public sealed record SourceSettings(string Catalog, string Authentication = "anonymous", string? PublisherKey = null);

public static class SettingsCodec
{
    public static Outcome<InstallerSettings> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (!HasProperties(root, ["schemaVersion", "source"], ["installerUpdates", "appearance"]))
                return Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidProperties);
            var version = root.GetProperty("schemaVersion");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                return Outcome<InstallerSettings>.Fail(ConfigurationError.UnsupportedSchema);
            if (!TrySource(root.GetProperty("source"), out var server))
                return Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidProperties);
            SourceSettings? installer = null;
            if (root.TryGetProperty("installerUpdates", out var updates))
            {
                if (!HasProperties(updates, ["source"], []) || !TrySource(updates.GetProperty("source"), out installer))
                    return Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidProperties);
            }
            var theme = "system";
            if (root.TryGetProperty("appearance", out var appearance))
            {
                if (!HasProperties(appearance, ["theme"], []) || appearance.GetProperty("theme").ValueKind != JsonValueKind.String)
                    return Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidProperties);
                theme = appearance.GetProperty("theme").GetString()!;
            }
            return Validate(new(server!.Catalog, installer?.Catalog, server.Authentication,
                installer?.Authentication ?? "anonymous", server.PublisherKey, installer?.PublisherKey, theme));
        }
        catch (JsonException)
        {
            return Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidJson);
        }
    }

    public static Outcome<InstallerSettings> Validate(InstallerSettings settings) =>
        settings.Theme is not ("system" or "light" or "dark") ? Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidTheme) :
        IsCatalog(settings.ServerCatalog) && (settings.InstallerCatalog is null || IsCatalog(settings.InstallerCatalog)) &&
        ValidSecurity(settings.Source(CatalogComponent.Server)) && ValidSecurity(settings.Source(CatalogComponent.Installer)) &&
        (settings.InstallerCatalog is not null || (settings.InstallerAuthentication == "anonymous" && settings.InstallerPublisherKey is null))
            ? new Outcome<InstallerSettings>.Success(settings)
            : Outcome<InstallerSettings>.Fail(ConfigurationError.InvalidCatalog);

    public static string Serialize(InstallerSettings settings)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["source"] = SourceJson(settings.Source(CatalogComponent.Server)),
        };
        if (settings.InstallerCatalog is not null)
            root["installerUpdates"] = new JsonObject
            {
                ["source"] = SourceJson(settings.Source(CatalogComponent.Installer)),
            };
        // Keep existing documents unchanged when using the default appearance.
        if (settings.Theme != "system") root["appearance"] = new JsonObject { ["theme"] = settings.Theme };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static bool HasProperties(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || (!required.Contains(property.Name) && !optional.Contains(property.Name)))
                return false;
        return required.All(names.Contains);
    }

    private static JsonObject SourceJson(SourceSettings source)
    {
        var result = new JsonObject { ["catalog"] = source.Catalog };
        if (source.Authentication != "anonymous") result["authentication"] = source.Authentication;
        if (source.PublisherKey is not null) result["publisherKey"] = source.PublisherKey;
        return result;
    }

    private static bool ValidSecurity(SourceSettings source) =>
        source.Authentication is "anonymous" or "bearer" or "basic" or "windows" &&
        (source.Authentication == "anonymous" || source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        (source.PublisherKey is null || PublisherTrust.IsPublicKey(source.PublisherKey));

    private static bool TrySource(JsonElement value, out SourceSettings? catalog)
    {
        catalog = null;
        if (!HasProperties(value, ["catalog"], ["authentication", "publisherKey"])) return false;
        var field = value.GetProperty("catalog");
        if (field.ValueKind != JsonValueKind.String) return false;
        string authentication = "anonymous";
        string? key = null;
        if (value.TryGetProperty("authentication", out var auth))
        {
            if (auth.ValueKind != JsonValueKind.String) return false;
            authentication = auth.GetString()!;
        }
        if (value.TryGetProperty("publisherKey", out var publisher))
        {
            if (publisher.ValueKind != JsonValueKind.String) return false;
            key = publisher.GetString();
        }
        catalog = new(field.GetString()!, authentication, key);
        return true;
    }

    private static bool IsCatalog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            return uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.Host.Length > 0;
        // Parse Windows paths consistently even in portable core tests.
        var path = value.Replace('/', '\\');
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        string remainder;
        if (path.Length >= 4 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
            remainder = path[3..];
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            remainder = path[2..];
            if (remainder.Split('\\').Length < 3) return false;
        }
        else return false;
        return remainder.Split('\\').All(part => part.Length > 0 && part is not "." and not ".." &&
            !part.EndsWith(' ') && !part.EndsWith('.') && part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) < 0);
    }
}
