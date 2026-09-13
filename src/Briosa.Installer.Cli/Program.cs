using Briosa.Installer.Core;

namespace Briosa.Installer.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.FirstOrDefault() is "packages" or "credentials" or "trust" or "diagnostics" or "app" or "settings-import" or "settings-export")
                return ManagementCommands.RunAsync(args, Console.Out, Console.Error, cancellation.Token).GetAwaiter().GetResult();
            return args.FirstOrDefault() == "catalog"
                ? CatalogCommands.RunAsync(args[1..], Console.Out, Console.Error, ConfigurationPaths.ForCurrentUser(), cancellationToken: cancellation.Token).GetAwaiter().GetResult()
                : CliApplication.Run(args, Console.Out, Console.Error, ConfigurationPaths.ForCurrentUser());
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
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
            if (key is not ("--config" or "--server-catalog" or "--installer-catalog" or "--same-source" or "--theme") || options.ContainsKey(key))
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
            var previous = snapshot.Settings ?? new(server);
            var settings = previous with
            {
                ServerCatalog = server, InstallerCatalog = installer,
                ServerAuthentication = previous.ServerCatalog == server ? previous.ServerAuthentication : "anonymous",
                ServerPublisherKey = previous.ServerPublisherKey,
                InstallerAuthentication = previous.InstallerCatalog == installer && installer is not null ? previous.InstallerAuthentication : "anonymous",
                InstallerPublisherKey = installer is null ? null : previous.InstallerPublisherKey ?? previous.ServerPublisherKey,
                Theme = options.GetValueOrDefault("--theme") ?? previous.Theme,
            };
            var saved = store.Save(snapshot, settings);
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
        Briosa Installer CLI

        settings show|validate [--config <file>]
        settings set [--config <file>] [--server-catalog <location>]
                     [--installer-catalog <location> | --same-source] [--theme system|light|dark]
        settings init --config <new-file> --server-catalog <location>
                      [--installer-catalog <location> | --same-source] [--theme system|light|dark]
        catalog list --component server|installer [--config <file>]
        catalog preview --component server|installer --id <package-id> [--config <file>]

        packages list|verify|recover|remove|install|repair [--store <directory>]
                 [--id <package-id>] [--component server|installer] [--config <file>]
                 [--catalog-sha256 <reviewed-catalog-digest>] [--yes]
        credentials set|remove --component server|installer [--config <file>]
                    [--mode bearer|basic|windows|anonymous] [--username <name>]
        trust import|clear --component server|installer [--config <file>]
              [--key <public-key.pem> --fingerprint <approved-sha256>]
        diagnostics sdk|export [--output <file>] [--config <file>]
        app activate --id <installed-installer-id> [--store <directory>]
                     [--bootstrap <standalone-Briosa.Launcher.exe>] --yes
        settings-import --input <file> [--config <file>]
        settings-export --output <file> [--config <file>]

        Locations are HTTPS catalog URLs or absolute Windows file/share paths.
        init creates a new file and refuses to overwrite an existing file.
        set preserves an existing updater override unless --same-source is supplied.
        Install/repair require --yes and the catalog digest shown by catalog preview.
        Credentials are read from standard input, never a command-line secret.
        User store is the default; machine deployment uses an authorized administrator terminal.
        No command activates or modifies SpatialAnalyzer.
        Exit codes: 0 success, 2 invalid input or file error, 3 setup required, 4 save conflict.
        Catalog commands also return 5 for catalog failures and 130 for cancellation.
        """);
}
