using System.Text.Json;
using Briosa.Installer.Core;

namespace Briosa.Installer.Cli;

public static class ManagementCommands
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        try
        {
            var group = args[0];
            var single = group is "settings-import" or "settings-export";
            if (!single && args.Length < 2) throw new ManagementException(ManagementError.InvalidInput);
            var action = single ? group : args[1];
            var allowed = group switch
            {
                "packages" => new[] { "--config", "--store", "--id", "--component", "--catalog-sha256", "--yes" },
                "credentials" => ["--config", "--component", "--mode", "--username"],
                "trust" => ["--config", "--component", "--key", "--fingerprint"],
                "diagnostics" => ["--config", "--output"],
                "app" => ["--store", "--id", "--yes", "--bootstrap"],
                "settings-import" => ["--config", "--input"],
                "settings-export" => ["--config", "--output"],
                _ => throw new ManagementException(ManagementError.InvalidInput),
            };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = single ? 1 : 2; index < args.Length; index++)
            {
                var key = args[index];
                if (!allowed.Contains(key) || options.ContainsKey(key)) throw new ManagementException(ManagementError.InvalidInput);
                if (key == "--yes") options.Add(key, "true");
                else
                {
                    if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new ManagementException(ManagementError.InvalidInput);
                    options.Add(key, args[index]);
                }
            }
            string Required(string key) => options.GetValueOrDefault(key) ?? throw new ManagementException(ManagementError.InvalidInput);
            void Confirm() { if (!options.ContainsKey("--yes")) throw new ManagementException(ManagementError.InvalidInput); }
            CatalogComponent Component() => Required("--component") switch
            { "server" => CatalogComponent.Server, "installer" => CatalogComponent.Installer, _ => throw new ManagementException(ManagementError.InvalidInput) };
            var paths = ConfigurationPaths.ForCurrentUser(options.GetValueOrDefault("--config"));
            SettingsSnapshot Load()
            {
                var result = new SettingsStore().Load(paths);
                return result is Outcome<SettingsSnapshot>.Success success ? success.Value : throw new ManagementException(ManagementError.InvalidInput);
            }
            InstallerSettings Settings() => Load().Settings ?? throw new ManagementException(ManagementError.InvalidInput);
            void Save(SettingsSnapshot snapshot, InstallerSettings value)
            { if (new SettingsStore().Save(snapshot, value) is Outcome<SettingsSnapshot>.Failure) throw new ManagementException(ManagementError.InvalidInput); }
            var packages = new PackageStore(options.GetValueOrDefault("--store"));
            var activity = new ActivityStore(Path.GetDirectoryName(paths.ExplicitFile ?? paths.UserFile)!);
            if (group == "packages")
            {
                switch (action)
                {
                    case "list": output.WriteLine(JsonSerializer.Serialize(packages.List(), InstallerJson.Options)); return 0;
                    case "verify": await packages.VerifyAsync(Required("--id"), token); break;
                    case "recover": Confirm(); packages.Recover(); break;
                    case "remove": Confirm(); packages.Remove(Required("--id")); break;
                    case "install": case "repair":
                        Confirm();
                        var captured = Load();
                        var installed = await packages.InstallAsync(captured.Settings ?? throw new ManagementException(ManagementError.InvalidInput), Component(), Required("--id"), Required("--catalog-sha256"), action == "repair", token: token,
                            configurationStillMatches: () => Load() == captured);
                        output.WriteLine(JsonSerializer.Serialize(installed, InstallerJson.Options));
                        break;
                    default: throw new ManagementException(ManagementError.InvalidInput);
                }
                activity.Record("Package." + action, "Succeeded");
            }
            else if (group == "app")
            {
                if (action != "activate") throw new ManagementException(ManagementError.InvalidInput);
                Confirm(); await packages.ActivateInstallerAsync(Required("--id"), token);
                if (options.TryGetValue("--bootstrap", out var bootstrap)) await BootstrapUpdater.RefreshAsync(packages, Required("--id"), bootstrap, token);
                output.WriteLine("Installer version selected. Restart through Briosa.Launcher.exe to use it. Source settings and server packages are preserved.");
            }
            else if (group is "credentials" or "trust")
            {
                var snapshot = Load();
                var settings = snapshot.Settings ?? throw new ManagementException(ManagementError.InvalidInput);
                var component = Component();
                var shared = component == CatalogComponent.Server || settings.InstallerCatalog is null;
                var source = settings.Source(component);
                if (group == "credentials")
                {
                    var vault = new WindowsCredentialStore();
                    if (action == "remove") { vault.Delete(source.Catalog); source = source with { Authentication = "anonymous" }; }
                    else if (action == "set")
                    {
                        var mode = Required("--mode");
                        if (mode is not ("anonymous" or "windows" or "bearer" or "basic")) throw new ManagementException(ManagementError.InvalidInput);
                        source = source with { Authentication = mode };
                        var candidate = shared ? settings with { ServerAuthentication = mode } : settings with { InstallerAuthentication = mode };
                        if (SettingsCodec.Validate(candidate) is Outcome<InstallerSettings>.Failure) throw new ManagementException(ManagementError.InvalidInput);
                        if (mode is "bearer" or "basic")
                            vault.Save(source.Catalog, new(mode == "basic" ? Required("--username") : "", ReadSecret()));
                    }
                    else throw new ManagementException(ManagementError.InvalidInput);
                }
                else if (action == "clear") source = source with { PublisherKey = null };
                else if (action == "import")
                {
                    var keyPath = Required("--key");
                    if (new FileInfo(keyPath).Length > 4096) throw new ManagementException(ManagementError.InvalidInput);
                    var pem = File.ReadAllText(keyPath);
                    var fingerprint = PublisherTrust.Fingerprint(pem);
                    if (!fingerprint.Equals(Required("--fingerprint"), StringComparison.OrdinalIgnoreCase)) throw new ManagementException(ManagementError.UntrustedPublisher);
                    source = source with { PublisherKey = pem };
                    output.WriteLine("Publisher fingerprint: " + fingerprint);
                }
                else throw new ManagementException(ManagementError.InvalidInput);
                var updated = shared ? settings with { ServerAuthentication = source.Authentication, ServerPublisherKey = source.PublisherKey }
                    : settings with { InstallerAuthentication = source.Authentication, InstallerPublisherKey = source.PublisherKey };
                Save(snapshot, updated);
                activity.Record(group == "trust" ? "Publisher.Settings" : "Credential.Settings", "Succeeded");
            }
            else if (group == "diagnostics")
            {
                if (action == "sdk") output.WriteLine(JsonSerializer.Serialize(new WindowsSdkDiscovery().Inspect(), InstallerJson.Options));
                else if (action == "export") activity.Export(Required("--output"), new WindowsSdkDiscovery().Inspect());
                else throw new ManagementException(ManagementError.InvalidInput);
            }
            else if (group == "settings-import")
            {
                var snapshot = Load();
                var importPaths = new ConfigurationPaths(Required("--input"), ExplicitFile: Required("--input"));
                if (new SettingsStore().Load(importPaths) is not Outcome<SettingsSnapshot>.Success { Value.Settings: { } value }) throw new ManagementException(ManagementError.InvalidInput);
                Save(snapshot, value);
            }
            else if (group == "settings-export")
            {
                var destination = Path.GetFullPath(Required("--output"));
                using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(file);
                writer.Write(SettingsCodec.Serialize(Settings()));
            }
            if (!(group == "packages" && action is "install" or "repair") && !(group == "diagnostics" && action == "sdk")) output.WriteLine("Completed.");
            return 0;
        }
        catch (ManagementException e) { error.WriteLine($"{e.Code}: {e.Message}"); return e.Code == ManagementError.Cancelled ? 130 : 6; }
        catch (OperationCanceledException) { error.WriteLine("Cancelled."); return 130; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException or System.Security.Cryptography.CryptographicException or NotSupportedException)
        { error.WriteLine("OperationFailed: Check access, configuration, and package integrity. No alternate source was contacted."); return 6; }
    }
    private static string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.In.ReadLine() ?? throw new ManagementException(ManagementError.InvalidInput);
        Console.Error.Write("Token/password (hidden): ");
        var secret = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; }
            else if (!char.IsControl(key.KeyChar) && secret.Length < 2560) secret.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return secret.ToString();
    }
}
