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
#pragma warning disable WPF0001 // Exercise the same system Fluent theme as App.xaml without invoking app startup.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
#pragma warning restore WPF0001
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Briosa.Installer;component/Styles.xaml", UriKind.Absolute) });
            using var brandTheme = new BrandTheme(app);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ExerciseSources(directory);
            ExerciseAutomaticCatalogLoading();
            ExercisePackageWorkflow(args);
            ExerciseCredentialBoundary();
            app.Shutdown();
            Console.WriteLine("WPF smoke passed: first use, source testing/save/discard, mirror isolation, stale checks, grouped inventory, filters, signed side-by-side install, verify/repair/remove, updates/explicit rollback, SDK summaries, persistent Activity, credential boundary, and compact layout. No native window or real SDK was used.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static T Find<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing control: {name}");
    private static void Click(MainWindow window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Ready(MainWindow window) => PumpUntil(() => !window.IsWorking);
    private static void Page(MainWindow window, string name) => Find<ListBox>(window, "Navigation").SelectedItem = Find<ListBoxItem>(window, name);
    private static void Load(MainWindow window)
    {
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Ready(window);
        PumpUntil(() => Find<Button>(window, "CancelCatalogButton").Visibility != Visibility.Visible);
    }
    private static void ExerciseAutomaticCatalogLoading()
    {
        using var feed = new SignedFeed();
        var first = feed.AddServer("0.1.0"); feed.Publish();
        var store = new PackageStore(feed.StorePath);
        store.InstallAsync(feed.Settings, CatalogComponent.Server, first.Id, feed.Hash).GetAwaiter().GetResult();
        var config = Path.Combine(feed.Root, "settings.json");
        var settings = new InstallerSettings("https://mirror.example.com/servers/catalog.json", "https://mirror.example.com/updater/catalog.json");
        File.WriteAllText(config, SettingsCodec.Serialize(settings));
        using var handler = new CatalogHandler { HoldNext = true };
        using var catalogs = new ReleaseCatalogClient(handler);
        var window = new MainWindow(new(config), catalogs, store, sdkDiscovery: new FakeSdkDiscovery(), confirmAction: (_, _) => true);
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        PumpUntil(() => handler.Pending is not null);
        var rows = Find<ListBox>(window, "CatalogPackages");
        Require(rows.Items.OfType<ServerRow>().Single().Installed?.Id == first.Id && !window.IsWorking, "Automatic source loading hid local inventory or blocked navigation.");
        Require(Find<Button>(window, "RefreshCatalogButton").Content.ToString() == "_Refresh", "Manual catalog action is not Refresh.");
        Page(window, "SettingsNavigation"); Page(window, "InstallationsNavigation");
        Require(handler.Requests.Count == 1 && handler.Requests[0] == settings.ServerCatalog, "Startup fetched another source or navigation duplicated its request.");
        handler.Pending!.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(rows.Items.OfType<ServerRow>().Single().Installed?.Id == first.Id, "A startup source failure lost installed versions.");
        Page(window, "SettingsNavigation"); Page(window, "InstallationsNavigation");
        Require(handler.Requests.Count == 1, "Page navigation retried a failed source automatically.");

        var second = feed.AddServer("0.2.0"); feed.Publish();
        store.InstallAsync(feed.Settings, CatalogComponent.Server, second.Id, feed.Hash).GetAwaiter().GetResult();
        Click(window, "RefreshCatalogButton"); PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(handler.Requests.Count == 2 && rows.Items.OfType<ServerRow>().Count(r => r.Installed is not null) == 2 &&
            rows.Items.OfType<ServerRow>().Any(r => r.Available), "Refresh did not reload both local installations and available servers.");
        Page(window, "SettingsNavigation"); Page(window, "InstallationsNavigation");
        Require(handler.Requests.Count == 2, "Returning to a populated page refetched its catalog.");

        // Saving a source while a previous read is still completing must eventually load the new source.
        handler.HoldNext = true; Click(window, "RefreshCatalogButton"); PumpUntil(() => handler.Pending is not null);
        Page(window, "SettingsNavigation");
        Find<TextBox>(window, "ServerCatalog").Text = "https://replacement.example.com/servers/catalog.json";
        Click(window, "SaveButton"); Ready(window);
        Page(window, "InstallationsNavigation");
        handler.Pending!.SetResult(handler.Response());
        PumpUntil(() => handler.Requests.Count == 4 && Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(handler.Requests.Last() == "https://replacement.example.com/servers/catalog.json" && rows.Items.OfType<ServerRow>().Any(r => r.Available), "A saved source remained unloaded behind the cancelled startup read.");
        window.Close();
    }
    private static void ExerciseSources(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        using var handler = new CatalogHandler();
        using var catalogs = new ReleaseCatalogClient(handler);
        var window = new MainWindow(new(path), catalogs, new PackageStore(Path.Combine(directory, "store")), sdkDiscovery: new FakeSdkDiscovery(), confirmAction: (_, _) => true);
        Load(window);
        Require(Find<Grid>(window, "InstallationsPage").Visibility == Visibility.Visible && handler.Requests.Count == 0, "First use fetched a public source or opened the wrong page.");
        Require(Find<Border>(window, "InventoryEmpty").Visibility == Visibility.Visible, "First use lacks an actionable empty state.");
        Click(window, "EmptyActionButton");
        Require(Find<Grid>(window, "SettingsPage").Visibility == Visibility.Visible && Find<TabControl>(window, "SettingsSections").SelectedIndex == 0, "Configure source did not open source settings.");
        Find<TextBox>(window, "ServerCatalog").Text = "https://artifacts.example.com/artifactory/briosa-servers/catalog.json";
        Find<CheckBox>(window, "SameSource").IsChecked = false;
        var updater = Find<TextBox>(window, "InstallerCatalog");
        updater.Text = "https://artifacts.example.com/artifactory/briosa-installer/catalog.json";
        Require(Find<StackPanel>(window, "InstallerSourceOverride").Visibility == Visibility.Visible, "Updater override was not revealed.");
        Click(window, "TestServerButton");
        PumpUntil(() => Find<Button>(window, "TestServerButton").IsEnabled);
        Require(Find<TextBlock>(window, "ServerTestText").Text.Contains("installation is blocked", StringComparison.Ordinal), "Unsigned source test confused browsing and install trust.");
        Require(!File.Exists(path), "Testing wrote settings.");
        Click(window, "SaveButton"); Ready(window);
        var settings = ((Outcome<SettingsSnapshot>.Success)new SettingsStore().Load(new(path))).Value.Settings!;
        Require(settings.InstallerCatalog == updater.Text, "The separate update source was lost.");
        Require(Find<WrapPanel>(window, "SaveChangesBar").Visibility == Visibility.Collapsed && !Find<Button>(window, "SaveButton").IsEnabled, "Saved settings still appear dirty.");
        var rows = Find<ListBox>(window, "CatalogPackages");
        Require(rows.Items.Count == 3 && rows.Items.OfType<ServerRow>().All(r => r.Package.Component == CatalogComponent.Server), "Test/save did not return the server-only catalog.");
        rows.SelectedIndex = 0;
        Require(!Find<Button>(window, "InstallPackageButton").IsEnabled && Find<Border>(window, "ServerSelectionPanel").Visibility == Visibility.Visible, "Unsigned metadata allowed installation or lost the summary.");
        var titles = new[] { "Installations", "SDK Setup", "Activity", "Settings" };
        var panels = new[] { "InstallationsPage", "SdkPage", "ActivityPage", "SettingsPage" };
        for (var i = 0; i < titles.Length; i++)
        {
            Find<ListBox>(window, "Navigation").SelectedIndex = i;
            Require(Find<TextBlock>(window, "PageTitle").Text == titles[i] && Find<Grid>(window, panels[i]).Visibility == Visibility.Visible, "Navigation order changed.");
        }
        Click(window, "CheckUpdatesButton"); PumpUntil(() => Find<Button>(window, "CheckUpdatesButton").IsEnabled);
        var installerRows = Find<DataGrid>(window, "InstallerReleases");
        Require(installerRows.Items.Count == 1 && installerRows.SelectedIndex == -1 && handler.Requests.Last().Contains("briosa-installer/catalog.json", StringComparison.Ordinal), "Updater routing or deliberate release selection failed.");
        Require(!Find<Button>(window, "InstallUpdateButton").IsEnabled && rows.Items.Count == 3, "Untrusted update enabled acquisition or replaced server results.");

        handler.HoldNext = true; Click(window, "CheckUpdatesButton"); PumpUntil(() => handler.Pending is not null);
        updater.Text = "https://new-updater.example.com/briosa/catalog.json";
        handler.Pending!.SetResult(handler.Response()); PumpUntil(() => !Find<Button>(window, "CancelUpdateCheckButton").IsEnabled);
        Require(installerRows.Items.Count == 0 && rows.Items.Count == 0, "Late update results survived a source edit.");
        Click(window, "SaveButton"); Ready(window);
        handler.FailNext = true; var beforeFailure = handler.Requests.Count;
        Click(window, "CheckUpdatesButton"); PumpUntil(() => Find<Button>(window, "CheckUpdatesButton").IsEnabled);
        Require(handler.Requests.Count == beforeFailure + 1 && handler.Requests.Last().Contains("new-updater.example.com", StringComparison.Ordinal) && installerRows.Items.Count == 0,
            "Failed enterprise override fell back or retained stale releases.");

        handler.HoldNext = true; Click(window, "TestServerButton"); PumpUntil(() => handler.Pending is not null);
        Find<TextBox>(window, "ServerCatalog").Text = "https://new-mirror.example.com/briosa/catalog.json";
        handler.Pending!.SetResult(handler.Response()); PumpUntil(() => Find<Button>(window, "TestServerButton").IsEnabled);
        Require(Find<TextBlock>(window, "ServerTestText").Text.StartsWith("Not tested", StringComparison.Ordinal), "A late source test validated different editor values.");
        Click(window, "DiscardButton"); Ready(window);
        Require(Find<TextBox>(window, "ServerCatalog").Text == settings.ServerCatalog, "Discard did not restore saved settings.");
        Click(window, "RefreshCatalogButton"); PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        rows.SelectedIndex = 0;
        File.WriteAllText(path, SettingsCodec.Serialize(settings with { ServerCatalog = "https://external.example.com/catalog.json" }));
        Click(window, "RefreshCatalogButton"); PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(rows.Items.Count == 0 && Find<TextBlock>(window, "CatalogStatusText").Text.Contains("Settings changed", StringComparison.Ordinal), "External settings edits were silently used.");
        Click(window, "ReloadButton"); Ready(window);
        Require(Find<TextBox>(window, "ServerCatalog").Text.Contains("external.example.com", StringComparison.Ordinal), "Reload did not pick up external edits.");
        Require(handler.Requests.All(uri => uri.EndsWith("catalog.json", StringComparison.Ordinal)), "Browsing fetched payloads.");
        window.Close();
    }

    private static void ExercisePackageWorkflow(string[] args)
    {
        using var feed = new SignedFeed();
        var first = feed.AddServer("0.1.0"); var second = feed.AddServer("0.2.0"); var installer = feed.AddInstaller("0.3.0"); var older = feed.AddInstaller("0.0.1"); feed.Publish();
        var config = Path.Combine(feed.Root, "settings.json"); File.WriteAllText(config, SettingsCodec.Serialize(feed.Settings));
        var store = new PackageStore(feed.StorePath);
        var window = new MainWindow(new(config), packageStore: store, sdkDiscovery: new FakeSdkDiscovery(), confirmAction: (title, _) => title != "Restart installer");
        Load(window);
        var rows = Find<ListBox>(window, "CatalogPackages");
        Require(rows.Items.Count == 2, "Configured startup did not populate available servers automatically.");
        Require(rows.Items.OfType<ServerRow>().First().Version == "0.2.0", "Newest server is not first within its target.");
        foreach (var id in new[] { first.Id, second.Id })
        {
            rows.SelectedItem = rows.Items.OfType<ServerRow>().Single(p => p.Id == id);
            Require(Find<Button>(window, "InstallPackageButton").IsEnabled, "Verified server installation was disabled.");
            Click(window, "InstallPackageButton"); Ready(window);
            Require(rows.Items.OfType<ServerRow>().Single(p => p.Id == id).Installed is not null, "Installed state was not joined to the available server.");
        }
        Require(store.List().Count == 2, "Installing a new server removed an older version.");
        Find<ComboBox>(window, "InventoryFilter").SelectedIndex = 2;
        Require(rows.Items.Count == 0 && Find<TextBlock>(window, "EmptyTitle").Text == "No matching servers", "Available filter includes installed servers or has no empty state.");
        Click(window, "EmptyActionButton");
        Find<TextBox>(window, "ServerSearch").Text = "0.1.0";
        Require(rows.Items.Count == 1, "Search did not filter server versions.");
        Find<TextBox>(window, "ServerSearch").Clear();
        // Source failure and an empty filter must keep their distinct recovery actions.
        Find<TextBox>(window, "ServerSearch").Text = "no matching release";
        var catalogBytes = File.ReadAllBytes(feed.CatalogPath);
        File.WriteAllText(feed.CatalogPath, "broken catalog fixture");
        Click(window, "RefreshCatalogButton"); PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(Find<Button>(window, "EmptyActionButton").Content.ToString() == "Try again", "A failed check was presented as an empty source.");
        File.WriteAllBytes(feed.CatalogPath, catalogBytes);
        Click(window, "EmptyActionButton"); PumpUntil(() => Find<Button>(window, "RefreshCatalogButton").IsEnabled);
        Require(Find<TextBox>(window, "ServerSearch").Text == "no matching release" && Find<TextBlock>(window, "EmptyTitle").Text == "No matching servers", "Retry cleared filters instead of checking the source.");
        Click(window, "EmptyActionButton");
        Require(rows.Items.Count == 2, "Clearing filters lost installed servers after source recovery.");
        rows.SelectedItem = rows.Items.OfType<ServerRow>().Single(p => p.Id == first.Id);
        var payload = Path.Combine(store.List().Single(p => p.Id == first.Id).Directory, "payload", "Briosa.Server.exe");
        File.WriteAllText(payload, "deliberately damaged inert fixture");
        Click(window, "VerifyInstalledButton"); Ready(window);
        Require(Find<TextBlock>(window, "OperationStatusText").Text.Contains("do not match", StringComparison.Ordinal), "Verification hid file damage.");
        Click(window, "RepairInstalledButton"); Ready(window);
        store.VerifyAsync(first.Id).GetAwaiter().GetResult();
        Require(Find<ListBox>(window, "ActivityList").Items.OfType<ActivityView>().Any(a => a.Entry.Operation == "Package.Repair" && a.Entry.Outcome == "Succeeded"), "Repair did not reach persistent Activity.");
        rows.SelectedItem = rows.Items.OfType<ServerRow>().Single(p => p.Id == first.Id);
        Click(window, "RemoveInstalledButton"); Ready(window);
        Require(store.List().Single().Id == second.Id && rows.Items.OfType<ServerRow>().Single(r => r.Id == first.Id).Installed is null, "Removal affected another release or left stale installed state.");

        Page(window, "SettingsNavigation"); Find<TabControl>(window, "SettingsSections").SelectedIndex = 1;
        Click(window, "CheckUpdatesButton"); PumpUntil(() => Find<Button>(window, "CheckUpdatesButton").IsEnabled);
        var releases = Find<DataGrid>(window, "InstallerReleases");
        Require(releases.Items.Count == 2 && releases.SelectedIndex == -1 && Find<Button>(window, "InstallUpdateButton").IsEnabled, "A normal update check selected a recovery release or failed to offer a newer one.");
        Click(window, "InstallUpdateButton"); Ready(window);
        var downloaded = Find<DataGrid>(window, "InstalledInstallerVersions");
        Require(downloaded.Items.Count == 1 && Find<Button>(window, "RestartInstallerButton").Visibility == Visibility.Visible, "Downloaded update did not offer restart when ready.");
        Require(rows.Items.Count == 2 && rows.Items.OfType<ServerRow>().Count(r => r.Installed is not null) == 1, "Installer update leaked into server inventory.");
        downloaded.SelectedItem = downloaded.Items.OfType<DownloadedInstaller>().Single();
        var installerPayload = Path.Combine(store.List().Single(p => p.Id == installer.Id).Directory, "payload", "Briosa.Installer.exe");
        File.WriteAllText(installerPayload, "deliberately damaged inert installer");
        Click(window, "VerifyInstallerButton"); Ready(window);
        Require(Find<TextBlock>(window, "UpdateOperationStatusText").Text.Contains("do not match", StringComparison.Ordinal), "Installer verification hid damage.");
        Click(window, "RepairInstallerButton"); Ready(window); store.VerifyAsync(installer.Id).GetAwaiter().GetResult();
        downloaded.SelectedItem = downloaded.Items.OfType<DownloadedInstaller>().Single();
        Click(window, "RemoveInstallerButton"); Ready(window);
        Require(store.List().Any(p => p.Id == installer.Id), "Selected installer was removed.");
        releases.SelectedItem = releases.Items.OfType<InstallerRelease>().Single(p => p.Package.Id == older.Id);
        Click(window, "UseReleaseButton"); Ready(window);
        Require(store.ResolveActiveInstallerAsync().GetAwaiter().GetResult()!.Contains(older.Id, StringComparison.Ordinal), "Explicit rollback was not selected.");
        downloaded.SelectedItem = downloaded.Items.OfType<DownloadedInstaller>().Single(p => p.Package.Id == installer.Id);
        Click(window, "RemoveInstallerButton"); Ready(window);
        Require(store.List().Count == 2, "Removing an inactive installer affected server inventory.");

        // A source containing only an older release must not turn rollback into the primary update action.
        using var oldFeed = new SignedFeed(); oldFeed.AddInstaller("0.0.1"); oldFeed.Publish();
        var oldConfig = Path.Combine(oldFeed.Root, "settings.json"); File.WriteAllText(oldConfig, SettingsCodec.Serialize(oldFeed.Settings));
        var oldWindow = new MainWindow(new(oldConfig), packageStore: new PackageStore(oldFeed.StorePath), sdkDiscovery: new FakeSdkDiscovery(), confirmAction: (_, _) => true);
        Load(oldWindow); Click(oldWindow, "CheckUpdatesButton"); PumpUntil(() => Find<Button>(oldWindow, "CheckUpdatesButton").IsEnabled);
        Require(Find<DataGrid>(oldWindow, "InstallerReleases").SelectedIndex == -1 && Find<Button>(oldWindow, "InstallUpdateButton").Visibility == Visibility.Collapsed &&
            Find<TextBlock>(oldWindow, "UpdateStatusText").Text.Contains("No newer", StringComparison.Ordinal), "No-newer-update state promoted rollback.");
        oldWindow.Close();

        Page(window, "SdkNavigation"); Click(window, "InspectSdkButton"); Ready(window);
        Require(Find<DataGrid>(window, "SdkObservations").Items.Count == 1 && Find<TextBlock>(window, "SdkSummaryText").Text.Contains("not been validated", StringComparison.Ordinal), "SDK summary overstated readiness.");
        Page(window, "ActivityNavigation");
        Require(Find<ListBox>(window, "ActivityList").Items.OfType<ActivityView>().Any(a => a.Entry.Operation == "Sdk.Inspect"), "New SDK inspection is missing from current Activity.");
        var persisted = new ActivityStore(feed.Root).Read();
        Require(persisted.Any(a => a.Operation == "Package.Install" && a.Target == second.SpatialAnalyzerTarget && a.DurationMs is not null), "Activity lost safe package context or duration.");
        Find<ComboBox>(window, "ActivityFilter").SelectedIndex = 1;
        Require(Find<ListBox>(window, "ActivityList").Items.OfType<ActivityView>().All(a => a.Entry.Outcome != "Succeeded"), "Needs-attention filter contains successes.");

        Page(window, "InstallationsNavigation"); rows.SelectedItem = rows.Items.OfType<ServerRow>().Single(r => r.Id == second.Id);
        var root = (FrameworkElement)window.Content;
        Layout(root, 820, 580);
        var logo = Find<Image>(window, "AppBrand");
        Require(logo.ActualWidth >= 160 && logo.Source is BitmapSource, "Brand logo is missing or smaller than the approved minimum.");
        var typeface = new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Require(typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.FontUri.ToString().Contains("Inter-Variable.ttf", StringComparison.OrdinalIgnoreCase), "The bundled Inter font was not resolved.");
        var actions = Find<Border>(window, "ServerSelectionPanel");
        var actionPoint = actions.TranslatePoint(new Point(0, actions.ActualHeight), root);
        Require(actions.ActualHeight > 0 && actionPoint.Y <= 580, "Server actions fall below the compact viewport.");
        Require(rows.ActualHeight >= 100, "Compact layout leaves no usable inventory.");
        if (args.Length > 0)
        {
            Render(root, args[0], 1140, 800);
            if (args.Length > 1) { Page(window, "SettingsNavigation"); Find<TabControl>(window, "SettingsSections").SelectedIndex = 0; Render(root, args[1], 1140, 800); }
            if (args.Length > 2) { Page(window, "InstallationsNavigation"); Render(root, args[2], 820, 580); }
        }
#pragma warning disable WPF0001
        var originalTheme = Application.Current.ThemeMode;
        Application.Current.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
        BrandTheme.ApplySystem(Application.Current);
        PumpDispatcher();
        if (args.Length > 0) Render(root, args[0] + ".light.png", 1140, 800);
#pragma warning disable WPF0001
        Application.Current.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        BrandTheme.ApplySystem(Application.Current);
        PumpDispatcher();
        if (args.Length > 0)
        {
            Render(root, args[0] + ".dark.png", 1140, 800);
            Page(window, "SettingsNavigation"); Find<TabControl>(window, "SettingsSections").SelectedIndex = 0;
            Render(root, args[0] + ".dark-settings.png", 1140, 800);
        }
        BrandTheme.Apply(Application.Current, dark: true, highContrast: true);
        Require(!Application.Current.Resources.Keys.Cast<object>().Contains("AccentButtonBackground") &&
            ((SolidColorBrush)Application.Current.Resources["BriosaNavigationBrush"]).Color == SystemColors.WindowColor,
            "High contrast did not release native controls and navigation to system colors.");
#pragma warning disable WPF0001
        Application.Current.ThemeMode = originalTheme;
#pragma warning restore WPF0001
        BrandTheme.ApplySystem(Application.Current);
        Require(File.ReadAllText(config) == SettingsCodec.Serialize(feed.Settings), "Package operations changed source settings.");
        window.Close();
    }

    private static void ExerciseCredentialBoundary()
    {
        var credentials = new FakeCredentials();
        var dialog = new SourceSecurityDialog(new SourceSettings("https://mirror.example.com/catalog.json", "bearer"), credentials);
        var controls = Descendants(dialog).OfType<FrameworkElement>().ToArray();
        var secret = controls.OfType<PasswordBox>().Single(p => p.Name == "CredentialSecret");
        secret.Password = "inert-test-token";
        Require(credentials.Value is null, "Typing a secret saved it before an explicit action.");
        controls.OfType<Button>().Single(b => b.Name == "SaveCredentialButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(credentials.Value?.Secret == "inert-test-token" && dialog.CredentialChanged && secret.Password.Length == 0, "Explicit credential save did not persist and clear the secret.");
        Require(controls.OfType<TextBlock>().Single(t => t.Name == "CredentialStatus").Text.Contains("Discard", StringComparison.Ordinal), "Immediate credential effect is not explained.");
        dialog.Close();
    }
    private sealed class FakeCredentials : ICredentialStore
    {
        public SourceCredential? Value { get; private set; }
        public SourceCredential? Read(string catalog) => Value;
        public void Save(string catalog, SourceCredential credential) => Value = credential;
        public void Delete(string catalog) => Value = null;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        for (var i = 0; i < 3; i++) { PumpDispatcher(); root.UpdateLayout(); }
    }
    private static void Render(FrameworkElement root, string output, int width, int height)
    {
        Layout(root, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.GetFullPath(output)); encoder.Save(stream);
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
