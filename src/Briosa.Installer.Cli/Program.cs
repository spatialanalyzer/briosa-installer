using Briosa.Installer.Core;

namespace Briosa.Installer.Cli;

public static class Program
{
    public static int Main(string[] args) => CliApplication.Run(args, Console.Out, Console.Error, ConfigurationPaths.ForCurrentUser());
}

public static class CliApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ConfigurationPaths defaults)
    {
        if (args.Length == 0 || args.SequenceEqual(["--help"]))
        {
            Help(output);
            return 0;
        }
        if (args.Length < 2 || args[0] != "settings" || args[1] is not ("show" or "validate" or "set" or "init"))
            return Usage(error);
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 2; index < args.Length; index++)
        {
            var key = args[index];
            if (key is not ("--config" or "--server-catalog" or "--installer-catalog" or "--same-source") || options.ContainsKey(key))
                return Usage(error);
            if (key == "--same-source") options.Add(key, null);
            else
            {
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) return Usage(error);
                options.Add(key, args[index]);
            }
        }
        if (options.ContainsKey("--same-source") && options.ContainsKey("--installer-catalog")) return Usage(error);
        var command = args[1];
        if (command is "show" or "validate" && options.Keys.Any(key => key != "--config")) return Usage(error);
        if (command == "init" && (!options.ContainsKey("--config") || !options.ContainsKey("--server-catalog"))) return Usage(error);
        try
        {
            var paths = options.TryGetValue("--config", out var config) ? defaults with { ExplicitFile = Path.GetFullPath(config!) } : defaults;
            var store = new SettingsStore();
            Outcome<SettingsSnapshot> loaded = command == "init"
                ? new Outcome<SettingsSnapshot>.Success(new(null, SettingsOrigin.ExplicitFile, paths.ExplicitFile!, null))
                : store.Load(paths);
            if (loaded is Outcome<SettingsSnapshot>.Failure failed) return Fail(error, failed.Error);
            var snapshot = ((Outcome<SettingsSnapshot>.Success)loaded).Value;
            if (command is "show" or "validate")
            {
                if (snapshot.Settings is null)
                {
                    error.WriteLine("SetupRequired: Configure a catalog before use. No public catalog request was made.");
                    return 3;
                }
                output.Write(command == "show" ? SettingsCodec.Serialize(snapshot.Settings) : "Settings are valid. Catalog access has not been tested.\n");
                return 0;
            }
            var server = options.GetValueOrDefault("--server-catalog") ?? snapshot.Settings?.ServerCatalog;
            if (server is null) return Usage(error);
            var installer = options.ContainsKey("--same-source") ? null :
                options.GetValueOrDefault("--installer-catalog") ?? snapshot.Settings?.InstallerCatalog;
            var saved = store.Save(snapshot, new(server, installer));
            if (saved is Outcome<SettingsSnapshot>.Failure saveFailure) return Fail(error, saveFailure.Error);
            output.WriteLine("Settings saved. Catalog access has not been tested.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return Fail(error, new(ConfigurationError.ReadFailed));
        }
    }

    private static int Fail(TextWriter error, ConfigurationFailure failure)
    {
        error.WriteLine($"{failure.Code}: {failure.Message}");
        return failure.Code == ConfigurationError.SaveConflict ? 4 : 2;
    }

    private static int Usage(TextWriter error) { Help(error); return 2; }

    private static void Help(TextWriter writer) => writer.WriteLine("""
        Briosa Installer CLI — source-settings development preview

        settings show|validate [--config <file>]
        settings set [--config <file>] [--server-catalog <location>]
                     [--installer-catalog <location> | --same-source]
        settings init --config <new-file> --server-catalog <location>
                      [--installer-catalog <location> | --same-source]

        Locations are HTTPS catalog URLs or absolute Windows file/share paths.
        init creates a new file and refuses to overwrite an existing file.
        set preserves an existing updater override unless --same-source is supplied.
        This preview does not contact catalogs, download packages, or change SA.
        Exit codes: 0 success, 2 invalid input or file error, 3 setup required, 4 save conflict.
        """);
}
