using System.IO;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.App;
using Briosa.Installer.Core;
using Briosa.Installer.Tests;

internal static partial class Program
{
    private static void ExercisePublicSourceChoice(string directory)
    {
        using var feed = new SignedFeed();
        var defaults = new InstallerSettings("https://public.example.com/catalog.json", ServerPublisherKey: feed.Settings.ServerPublisherKey);
        File.WriteAllText(Path.Combine(feed.Root, "public-source.json"), SettingsCodec.Serialize(defaults));
        foreach (var choosePublic in new[] { false, true })
        {
            var config = Path.Combine(directory, $"public-choice-{choosePublic}.json");
            using var handler = new CatalogHandler();
            using var client = new ReleaseCatalogClient(handler);
            var window = new MainWindow(new(config), client, new PackageStore(Path.Combine(feed.Root, $"store-{choosePublic}")),
                sdkDiscovery: new FakeSdkDiscovery(), distributionDefaults: () => DistributionDefaults.Load(feed.Root));
            Load(window); Saved(window);
            Require(handler.Requests.Count == 0 && !File.Exists(config), "Bundled defaults were persisted or requested before choosing a source.");
            Require(Find<Button>(window, "UsePublicSourceButton").Visibility == Visibility.Visible, "Public source choice is missing.");
            Page(window, "SettingsNavigation");
            Find<ComboBox>(window, "ThemeSelector").SelectedIndex = 2; Saved(window);
            Page(window, "ActivityNavigation"); Page(window, "InstallationsNavigation");
            Require(handler.Requests.Count == 0 && ReadSettings(config).ServerCatalog.Length == 0, "Appearance or navigation selected the public source.");
            window.Close();
            window = new MainWindow(new(config), client, new PackageStore(Path.Combine(feed.Root, $"store-{choosePublic}")),
                sdkDiscovery: new FakeSdkDiscovery(), distributionDefaults: () => DistributionDefaults.Load(feed.Root));
            Load(window);
            Require(handler.Requests.Count == 0 && Find<Button>(window, "UsePublicSourceButton").Visibility == Visibility.Visible,
                "Appearance-only settings lost first-use choice on restart.");
            if (choosePublic)
            {
                Click(window, "UsePublicSourceButton"); Saved(window);
                PumpUntil(() => handler.Requests.Count > 0);
                Require(ReadSettings(config).ServerPublisherKey == defaults.ServerPublisherKey && ReadSettings(config).Theme == "dark",
                    "Public source choice lost the bundled publisher or appearance.");
            }
            else
            {
                Click(window, "EmptyActionButton");
                Find<TextBox>(window, "ServerCatalog").Text = "https://internal.example.com/catalog.json"; Saved(window);
                Page(window, "InstallationsNavigation"); PumpUntil(() => handler.Requests.Count > 0);
            }
            PumpUntil(() => Find<Button>(window, "CancelCatalogButton").Visibility != Visibility.Visible);
            Require(handler.Requests.All(url => url.StartsWith(choosePublic ? "https://public.example.com/" : "https://internal.example.com/", StringComparison.Ordinal)),
                "First-use requests escaped the chosen source.");
            window.Close();
        }
    }

    private static InstallerSettings ReadSettings(string path) =>
        ((Outcome<SettingsSnapshot>.Success)new SettingsStore().Load(new(path))).Value.Settings!;
}
