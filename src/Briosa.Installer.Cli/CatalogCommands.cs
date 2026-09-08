using System.Text.Json;
using Briosa.Installer.Core;

namespace Briosa.Installer.Cli;

public static class CatalogCommands
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, ConfigurationPaths defaults,
        ReleaseCatalogClient? client = null, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is not ("list" or "preview")) return Usage(error);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || args[index] is not ("--config" or "--component" or "--id") ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[index], args[index + 1])) return Usage(error);
        }
        if (!options.TryGetValue("--component", out var componentName) || componentName is not ("server" or "installer") ||
            (args[0] == "preview") != options.ContainsKey("--id")) return Usage(error);
        try
        {
            var paths = options.TryGetValue("--config", out var file) ? defaults with { ExplicitFile = Path.GetFullPath(file) } : defaults;
            var loaded = new SettingsStore().Load(paths);
            if (loaded is Outcome<SettingsSnapshot>.Failure failure)
            {
                error.WriteLine($"{failure.Error.Code}: {failure.Error.Message}");
                return 2;
            }
            var settings = ((Outcome<SettingsSnapshot>.Success)loaded).Value.Settings;
            if (settings is null) { error.WriteLine("SetupRequired: Configure a catalog before use."); return 3; }
            using var ownedClient = client is null ? new ReleaseCatalogClient() : null;
            var result = await (client ?? ownedClient!).ReadAsync(settings,
                componentName == "server" ? CatalogComponent.Server : CatalogComponent.Installer, cancellationToken).ConfigureAwait(false);
            if (result is CatalogResult<CatalogSnapshot>.Failure readFailure) return Fail(error, readFailure.Error);
            var catalog = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            object display;
            if (args[0] == "preview")
            {
                var preview = ReleaseCatalogCodec.Preview(catalog, options["--id"]);
                if (preview is CatalogResult<PackagePreview>.Failure invalid) return Fail(error, invalid.Error);
                display = ((CatalogResult<PackagePreview>.Success)preview).Value;
            }
            else display = new { component = componentName, catalogSource = catalog.Source, catalogSha256 = catalog.ContentSha256,
                publisherVerification = "notPerformed", packages = catalog.Packages };
            output.WriteLine(JsonSerializer.Serialize(display, new JsonSerializerOptions
            {
                WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            }));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail(error, new(CatalogError.SourceUnavailable));
        }
    }

    private static int Fail(TextWriter error, CatalogFailure failure)
    {
        error.WriteLine($"{failure.Code}: {failure.Message}");
        return failure.Code == CatalogError.Cancelled ? 130 : 5;
    }

    private static int Usage(TextWriter writer)
    {
        writer.WriteLine("catalog list --component server|installer [--config <file>]");
        writer.WriteLine("catalog preview --component server|installer --id <package-id> [--config <file>]");
        writer.WriteLine("Reads metadata only; publisher verification, downloads, and installation are not performed.");
        return 2;
    }
}
