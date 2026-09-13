using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Briosa.Installer.App;
using Briosa.Installer.Core;
using Briosa.Installer.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Briosa.Installer.App.Smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Briosa.Installer;component/Styles.xaml", UriKind.Absolute),
            });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var path = Path.Combine(directory, "settings.json");
            using var handler = new CatalogHandler();
            using var catalogs = new ReleaseCatalogClient(handler);
            var window = new MainWindow(new(path), catalogs, new PackageStore(Path.Combine(directory, "store")), sdkDiscovery: new FakeSdkDiscovery());
            // Exercise our own WPF tree without displaying a native window or
            // accessing any existing user settings, desktop application, or SDK.
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var save = Find<Button>(window, "SaveButton");
            PumpUntil(() => save.IsEnabled);
            var navigation = Find<ListBox>(window, "Navigation");
            Require(Find<TextBlock>(window, "PageTitle").Text == "Installations" &&
                Find<StackPanel>(window, "InstallationsPage").Visibility == Visibility.Visible,
                "The app did not open on Installations.");
            navigation.SelectedItem = Find<ListBoxItem>(window, "SettingsNavigation");
            Find<TextBox>(window, "ServerCatalog").Text = "https://artifacts.example.com/artifactory/briosa-servers/catalog.json";
            Find<CheckBox>(window, "SameSource").IsChecked = false;
            var updater = Find<TextBox>(window, "InstallerCatalog");
            Require(updater.IsEnabled, "Separate update catalog field did not enable.");
            updater.Text = "https://artifacts.example.com/artifactory/briosa-installer/catalog.json";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            var result = new SettingsStore().Load(new(path));
            Require(result is Outcome<SettingsSnapshot>.Success { Value.Settings.InstallerCatalog: not null }, "WPF save did not persist both catalogs.");
            Find<Button>(window, "ReloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            Require(updater.Text.EndsWith("briosa-installer/catalog.json", StringComparison.Ordinal), "WPF reload lost the update source.");
            Require(handler.Requests.Count == 0, "Settings editing made an unexpected network request.");
            var titles = new[] { "Installations", "SDK Setup", "Activity", "Settings" };
            var panels = new[] { "InstallationsPage", "SdkPage", "ActivityPage", "SettingsPage" };
            for (var index = 0; index < 4; index++)
            {
                navigation.SelectedIndex = index;
                Require(((ListBoxItem)navigation.Items[index]).Content.ToString() == titles[index] &&
                    Find<TextBlock>(window, "PageTitle").Text == titles[index] &&
                    Find<StackPanel>(window, panels[index]).Visibility == Visibility.Visible,
                    "Navigation did not open the expected page.");
            }
            navigation.SelectedItem = Find<ListBoxItem>(window, "InstallationsNavigation");
            var refresh = Find<Button>(window, "RefreshCatalogButton");
            var rows = Find<DataGrid>(window, "CatalogPackages");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refresh.IsEnabled);
            Require(rows.Items.Count == 3, "Server catalog did not list its three fixture packages.");
            rows.SelectedIndex = 0;
            var preview = Find<TextBox>(window, "PackagePreviewText");
            Find<Button>(window, "PreviewPackageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => preview.Text.Length > 0);
            Require(preview.Text.Contains("Not verified", StringComparison.Ordinal) && !Find<Button>(window, "InstallPackageButton").IsEnabled, "An unverified catalog enabled installation.");
            Require(rows.Items.OfType<CatalogPackage>().All(p => p.Component == CatalogComponent.Server),
                "Installations included installer downloads.");
            Require(window.FindName("CatalogComponentSelector") is null, "Installations still exposes a component selector.");
            navigation.SelectedItem = Find<ListBoxItem>(window, "SettingsNavigation");
            var checkUpdates = Find<Button>(window, "CheckUpdatesButton");
            var installerRows = Find<DataGrid>(window, "InstallerReleases");
            checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => checkUpdates.IsEnabled);
            Require(installerRows.Items.Count == 1 && handler.Requests.Last().Contains("briosa-installer/catalog.json", StringComparison.Ordinal),
                "Settings update check did not use its independent source.");
            Require(installerRows.Items.OfType<InstallerRelease>().All(p => p.Package.Component == CatalogComponent.Installer) &&
                !Find<Button>(window, "InstallUpdateButton").IsEnabled, "An unsigned updater catalog enabled installation or included servers.");
            Require(rows.Items.Count == 3 && preview.Text.Length > 0, "Checking installer updates replaced the server catalog.");
            handler.HoldNext = true;
            checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => handler.Pending is not null);
            updater.Text = "https://new-updater.example.com/briosa/catalog.json";
            handler.Pending!.SetResult(handler.Response());
            PumpUntil(() => !Find<Button>(window, "CancelUpdateCheckButton").IsEnabled);
            Require(installerRows.Items.Count == 0 && !Find<Button>(window, "InstallUpdateButton").IsEnabled,
                "A stale installer check restored results after settings changed.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => save.IsEnabled);
            handler.FailNext = true;
            var beforeFailure = handler.Requests.Count;
            checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => checkUpdates.IsEnabled);
            Require(installerRows.Items.Count == 0 && handler.Requests.Count == beforeFailure + 1 &&
                handler.Requests.Last().Contains("new-updater.example.com", StringComparison.Ordinal),
                "A failed updater override used another source or retained results.");
            checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => checkUpdates.IsEnabled);
            navigation.SelectedItem = Find<ListBoxItem>(window, "InstallationsNavigation");
            handler.HoldNext = true;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => handler.Pending is not null);
            Find<TextBox>(window, "ServerCatalog").Text = "https://new-mirror.example.com/briosa/catalog.json";
            handler.Pending!.SetResult(handler.Response());
            PumpUntil(() => !Find<Button>(window, "CancelCatalogButton").IsEnabled);
            Require(rows.Items.Count == 0 && preview.Text.Length == 0 && installerRows.Items.Count == 0,
                "Late results restored a catalog after its source changed.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refresh.IsEnabled);
            rows.SelectedIndex = 0;
            Find<Button>(window, "PreviewPackageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => preview.Text.Length > 0);
            Require(handler.Requests.All(uri => uri.EndsWith("catalog.json", StringComparison.Ordinal)), "Browsing fetched a referenced payload.");
            checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => checkUpdates.IsEnabled);
            navigation.SelectedItem = Find<ListBoxItem>(window, "SdkNavigation");
            Find<Button>(window, "InspectSdkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => Find<Button>(window, "InspectSdkButton").IsEnabled);
            Require(Find<DataGrid>(window, "SdkObservations").Items.Count == 1, "SDK diagnostics did not display the injected evidence.");
            navigation.SelectedItem = Find<ListBoxItem>(window, "InstallationsNavigation");
            if (args.Length > 0)
            {
                // The preview uses a symbolic settings path instead of the temporary
                // test directory. Every other element is the actual WPF control tree.
                Find<TextBox>(window, "SettingsPath").Text = @"%LOCALAPPDATA%\Briosa\Installer\settings.json";
                Find<TextBlock>(window, "StoreLocationText").Text = @"%LOCALAPPDATA%\Briosa\Packages";
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1120, 820));
                root.Arrange(new Rect(0, 0, 1120, 820));
                root.UpdateLayout();
                // DataGrid completes column sizing through deferred dispatcher work.
                for (var pass = 0; pass < 3; pass++)
                {
                    PumpDispatcher();
                    root.UpdateLayout();
                }
                var bitmap = new RenderTargetBitmap(1120, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.GetFullPath(args[0]));
                encoder.Save(stream);
                if (args.Length > 1)
                {
                    navigation.SelectedItem = Find<ListBoxItem>(window, "SettingsNavigation");
                    root.Measure(new Size(1120, 820));
                    root.Arrange(new Rect(0, 0, 1120, 820));
                    root.UpdateLayout(); PumpDispatcher(); root.UpdateLayout();
                    var settingsBitmap = new RenderTargetBitmap(1120, 820, 96, 96, PixelFormats.Pbgra32);
                    settingsBitmap.Render(root);
                    var settingsEncoder = new PngBitmapEncoder();
                    settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                    using var settingsStream = File.Create(Path.GetFullPath(args[1]));
                    settingsEncoder.Save(settingsStream);
                }
            }
            window.Close();
            ExercisePackageWorkflow();
            app.Shutdown();
            Console.WriteLine("WPF smoke passed: settings, catalogs, signed installation, verification, repair, removal, installer selection, SDK handoff evidence, stale-result cancellation, and navigation. No native window, real network, or SDK was used.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static T Find<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing control: {name}");

    private static void ExercisePackageWorkflow()
    {
        using var feed = new SignedFeed();
        var first = feed.AddServer("0.1.0"); var second = feed.AddServer("0.2.0"); var installer = feed.AddInstaller("0.3.0");
        var olderInstaller = feed.AddInstaller("0.0.1"); feed.Publish();
        var config = Path.Combine(feed.Root, "settings.json"); File.WriteAllText(config, SettingsCodec.Serialize(feed.Settings));
        var store = new PackageStore(feed.StorePath);
        var window = new MainWindow(new(config), packageStore: store, sdkDiscovery: new FakeSdkDiscovery(), confirmAction: (title, _) => title != "Restart installer");
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var save = Find<Button>(window, "SaveButton"); PumpUntil(() => save.IsEnabled);
        Find<ListBox>(window, "Navigation").SelectedItem = Find<ListBoxItem>(window, "InstallationsNavigation");
        var refresh = Find<Button>(window, "RefreshCatalogButton");
        var available = Find<DataGrid>(window, "CatalogPackages");
        var installed = Find<DataGrid>(window, "InstalledPackages");
        var tabs = Find<TabControl>(window, "PackageTabs");
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => refresh.IsEnabled);
        foreach (var id in new[] { first.Id, second.Id })
        {
            tabs.SelectedIndex = 0;
            available.SelectedItem = available.Items.OfType<CatalogPackage>().Single(p => p.Id == id);
            Require(Find<Button>(window, "InstallPackageButton").IsEnabled, "A signed package did not enable installation.");
            var before = installed.Items.Count;
            Find<Button>(window, "InstallPackageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => installed.Items.Count == before + 1 && save.IsEnabled);
        }
        installed.SelectedItem = installed.Items.OfType<InstalledPackage>().Single(p => p.Id == first.Id);
        var payload = Path.Combine(store.List().Single(p => p.Id == first.Id).Directory, "payload", "Briosa.Server.exe");
        File.WriteAllText(payload, "deliberately damaged inert fixture");
        Find<Button>(window, "VerifyInstalledButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => save.IsEnabled);
        Require(Find<TextBlock>(window, "OperationStatusText").Text.Contains("do not match", StringComparison.Ordinal), "GUI verify did not report damaged files.");
        Find<Button>(window, "RepairInstalledButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => Find<ListBox>(window, "ActivityList").Items.Cast<string>().Contains("Package.Repair: completed.") && save.IsEnabled);
        store.VerifyAsync(first.Id).GetAwaiter().GetResult();
        installed.SelectedItem = installed.Items.OfType<InstalledPackage>().Single(p => p.Id == first.Id);
        Find<Button>(window, "RemoveInstalledButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => installed.Items.Count == 1 && save.IsEnabled);
        Require(store.List().Single().Id == second.Id, "GUI removal affected another version.");
        Find<ListBox>(window, "Navigation").SelectedItem = Find<ListBoxItem>(window, "SettingsNavigation");
        var checkUpdates = Find<Button>(window, "CheckUpdatesButton");
        var installerRows = Find<DataGrid>(window, "InstallerReleases");
        var downloaded = Find<DataGrid>(window, "InstalledInstallerVersions");
        checkUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => checkUpdates.IsEnabled);
        Require(installerRows.Items.Count == 2 && ((InstallerRelease)installerRows.SelectedItem).Package.Id == installer.Id &&
            installerRows.Items.OfType<InstallerRelease>().Any(p => p.Comparison < 0),
            "Settings did not select the newest installer or label the older release for rollback.");
        Require(Find<Button>(window, "InstallUpdateButton").IsEnabled, "A verified installer did not enable an update.");
        Find<Button>(window, "InstallUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => File.Exists(Path.Combine(feed.StorePath, "active-installer.json")) && save.IsEnabled);
        Require(installed.Items.Count == 1 && downloaded.Items.Count == 1 && available.Items.Count == 2,
            "Installer acquisition leaked into server available or installed lists.");
        Require(Find<TextBlock>(window, "UpdateOperationStatusText").Text.Contains("0.3.0 selected", StringComparison.Ordinal),
            "Installer completion was not reported in Settings.");
        downloaded.SelectedItem = downloaded.Items.OfType<InstalledPackage>().Single(p => p.Id == installer.Id);
        Find<Button>(window, "VerifyInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => save.IsEnabled);
        Require(Find<TextBlock>(window, "UpdateOperationStatusText").Text == "Completed.", "Settings could not verify its downloaded installer.");
        var installerPayload = Path.Combine(store.List().Single(p => p.Id == installer.Id).Directory, "payload", "Briosa.Installer.exe");
        File.WriteAllText(installerPayload, "deliberately damaged inert installer");
        downloaded.SelectedItem = downloaded.Items.OfType<InstalledPackage>().Single(p => p.Id == installer.Id);
        Find<Button>(window, "VerifyInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => save.IsEnabled);
        Require(Find<TextBlock>(window, "UpdateOperationStatusText").Text.Contains("do not match", StringComparison.Ordinal),
            "Settings verification did not report a damaged installer.");
        Find<Button>(window, "RepairInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => Find<TextBlock>(window, "UpdateOperationStatusText").Text == "Completed." && save.IsEnabled);
        store.VerifyAsync(installer.Id).GetAwaiter().GetResult();
        downloaded.SelectedItem = downloaded.Items.OfType<InstalledPackage>().Single(p => p.Id == installer.Id);
        Find<Button>(window, "RemoveInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => save.IsEnabled);
        Require(store.List().Any(p => p.Id == installer.Id) && Find<TextBlock>(window, "UpdateOperationStatusText").Text != "Completed.",
            "Settings allowed removal of the selected installer.");
        Find<Button>(window, "InstallUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => Find<TextBlock>(window, "UpdateOperationStatusText").Text.Contains("0.3.0 selected", StringComparison.Ordinal) && save.IsEnabled);
        Require(downloaded.Items.Count == 1, "Using an already downloaded release did not reuse the existing verified version.");
        downloaded.SelectedItem = downloaded.Items.OfType<InstalledPackage>().Single(p => p.Id == installer.Id);
        Find<Button>(window, "ActivateInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => save.IsEnabled);
        Require(store.ResolveActiveInstallerAsync().GetAwaiter().GetResult()!.EndsWith("Briosa.Installer.exe", StringComparison.Ordinal), "GUI installer selection was not persisted.");
        Require(File.ReadAllText(config) == SettingsCodec.Serialize(feed.Settings), "Package operations changed source settings.");
        Find<ComboBox>(window, "InstallerStoreScope").SelectedIndex = 0;
        Require(Find<ComboBox>(window, "StoreScope").SelectedIndex == 0 && installed.Items.Count == 0 && downloaded.Items.Count == 0,
            "Settings scope change did not synchronize or clear inventories.");
        Find<ComboBox>(window, "StoreScope").SelectedIndex = 2;
        Require(Find<ComboBox>(window, "InstallerStoreScope").SelectedIndex == 2, "Server scope change left a different updater destination.");
        Find<Button>(window, "RefreshInstallerVersionsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => save.IsEnabled);
        Require(installed.Items.Count == 1 && downloaded.Items.Count == 1, "Refreshing Settings did not split the shared inventory.");
        installerRows.SelectedItem = installerRows.Items.OfType<InstallerRelease>().Single(p => p.Package.Id == olderInstaller.Id);
        Find<Button>(window, "InstallUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => downloaded.Items.Count == 2 && save.IsEnabled);
        Require(store.ResolveActiveInstallerAsync().GetAwaiter().GetResult()!.Contains(olderInstaller.Id, StringComparison.Ordinal),
            "Settings could not deliberately select an older installer.");
        downloaded.SelectedItem = downloaded.Items.OfType<InstalledPackage>().Single(p => p.Id == installer.Id);
        Find<Button>(window, "RemoveInstallerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpUntil(() => save.IsEnabled);
        Require(downloaded.Items.Count == 1 && installed.Items.Count == 1 &&
            store.List().All(p => p.Id != installer.Id), "Settings removal affected servers or failed to remove the inactive installer.");
        window.Close();
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool FailNext { get; set; }
        public bool HoldNext
        {
            get; set
            {
                field = value;
                if (value) Pending = null;
            }
        }
        public TaskCompletionSource<HttpResponseMessage>? Pending { get; private set; }
        public HttpResponseMessage Response() => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog.json"))),
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (FailNext) { FailNext = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            if (!HoldNext) return Task.FromResult(Response());
            HoldNext = false;
            Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Pending.Task;
        }
    }
    private sealed class FakeSdkDiscovery : ISdkDiscovery
    {
        public SdkReport Inspect() => new(DateTimeOffset.UtcNow, [new("Registered SDK candidate", "Fixture / 64-bit", "2099.1.0101.1", "Fixture only", "Activation not observed")], "Fixture handoff; no registry access or SDK activation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF settings operation did not complete.");
            PumpDispatcher();
            Thread.Sleep(10);
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
