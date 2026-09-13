using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow : Window
{
    private readonly ConfigurationPaths paths;
    private readonly SettingsStore store = new();
    private SettingsSnapshot? snapshot;
    private bool populating;
    private bool dirty;
    private bool busy;

    public MainWindow(ConfigurationPaths paths, ReleaseCatalogClient? catalogClient = null, PackageStore? packageStore = null,
        ICredentialStore? credentials = null, ISdkDiscovery? sdkDiscovery = null, Func<string, string, bool>? confirmAction = null, string? bootstrapPath = null)
    {
        this.paths = paths;
        this.catalogClient = catalogClient ?? new ReleaseCatalogClient(credentials: credentials);
        ownsCatalogClient = catalogClient is null;
        this.packageStore = packageStore ?? new PackageStore(credentials: credentials);
        this.credentials = credentials ?? new WindowsCredentialStore();
        this.sdkDiscovery = sdkDiscovery ?? new WindowsSdkDiscovery();
        this.confirmAction = confirmAction;
        this.bootstrapPath = bootstrapPath;
        activity = new ActivityStore(System.IO.Path.GetDirectoryName(paths.ExplicitFile ?? paths.UserFile)!);
        InitializeComponent();
        ShowSelectedPage();
        configuringScope = true;
        if (this.packageStore.Root.TrimEnd('\\', '/').Equals(PackageStore.MachineRoot, StringComparison.OrdinalIgnoreCase)) StoreScope.SelectedIndex = 1;
        else if (!this.packageStore.Root.TrimEnd('\\', '/').Equals(PackageStore.UserRoot, StringComparison.OrdinalIgnoreCase))
        {
            customStore = this.packageStore;
            StoreScope.Items.Add(new ComboBoxItem { Content = "Explicit package directory" });
            StoreScope.SelectedIndex = 2;
        }
        configuringScope = false;
        SettingsPath.Text = paths.ExplicitFile ?? paths.UserFile;
        BuildVersionText.Text = "Version " + (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?.InformationalVersion.Split('+')[0] ?? "development");
        StoreLocationText.Text = this.packageStore.Root;
        foreach (var entry in activity.Read().Reverse()) AddActivity($"{entry.Time:u} {entry.Operation}: {entry.Outcome}");
        UpdateManagementControls();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        InvalidateCatalog();
        SetBusy(true);
        try
        {
            var loaded = await Task.Run(() => store.Load(paths));
            snapshot = loaded is Outcome<SettingsSnapshot>.Success success ? success.Value : null;
            if (loaded is Outcome<SettingsSnapshot>.Failure failure)
            {
                StatusText.Text = $"{failure.Error.Code}: {failure.Error.Message}";
                CatalogStatusText.Text = "Settings could not be loaded. Open Settings to review the configuration.";
                AddActivity("Settings could not be loaded.");
                return;
            }
            InstallerSettings? defaults = null;
            if (snapshot!.Origin == SettingsOrigin.SetupRequired)
            {
                try { defaults = DistributionDefaults.Load(); }
                catch (ManagementException) { StatusText.Text = "The distribution's public defaults are invalid. Configure an explicit source."; }
            }
            var editorSettings = snapshot.Settings ?? defaults;
            populating = true;
            ServerCatalog.Text = editorSettings?.ServerCatalog ?? "";
            InstallerCatalog.Text = editorSettings?.InstallerCatalog ?? "";
            SameSource.IsChecked = editorSettings?.InstallerCatalog is null;
            serverSecurity = editorSettings?.Source(CatalogComponent.Server);
            installerSecurity = editorSettings?.InstallerCatalog is null ? null : editorSettings.Source(CatalogComponent.Installer);
            populating = false;
            dirty = false;
            UpdateEffectiveSource();
            UpdateSecurityLabels();
            try { PolicyText.Text = EnterprisePolicy.Load() is null ? "Source settings are editable. No administrator policy is configured." : "Managed by your organization: source and publisher restrictions apply."; }
            catch (ManagementException e) { PolicyText.Text = e.Message; }
            StatusText.Text = snapshot.Origin switch
            {
                SettingsOrigin.SetupRequired => "Choose your catalog and save to get started.",
                SettingsOrigin.MachineDefaults => "Machine defaults loaded. Saving creates your personal settings file.",
                _ => "Settings loaded. Catalog access has not been tested.",
            };
            AddActivity("Settings loaded.");
            if (snapshot.Settings is null)
                CatalogStatusText.Text = "Open Settings to configure package sources, then save and refresh the catalog.";
        }
        finally { populating = false; SetBusy(false); }
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (snapshot is null || busy) return;
        var settings = CurrentSettings();
        SetBusy(true);
        try
        {
            var saved = await Task.Run(() => store.Save(snapshot, settings));
            if (saved is Outcome<SettingsSnapshot>.Failure failure)
            {
                StatusText.Text = $"{failure.Error.Code}: {failure.Error.Message}";
                AddActivity("Settings were not saved.");
                return;
            }
            snapshot = ((Outcome<SettingsSnapshot>.Success)saved).Value;
            dirty = false;
            InvalidateCatalog();
            StatusText.Text = "Settings saved. Catalog access has not been tested.";
            AddActivity("Source settings saved.");
        }
        finally { SetBusy(false); }
    }

    private async void ReloadClicked(object sender, RoutedEventArgs e)
    {
        if (!busy && ConfirmDiscard()) await ReloadAsync();
    }

    private void SourceChanged(object sender, RoutedEventArgs e)
    {
        if (EffectiveSource is null || SameSource is null || InstallerCatalog is null) return;
        UpdateEffectiveSource();
        if (!populating)
        {
            dirty = true;
            InvalidateCatalog();
            UpdateSecurityLabels();
        }
    }

    private void UpdateEffectiveSource()
    {
        InstallerCatalog.IsEnabled = SameSource.IsChecked != true && !busy;
        var effective = CurrentSettings().EffectiveInstallerCatalog;
        EffectiveSource.Text = string.IsNullOrWhiteSpace(effective) ? "Choose a catalog for installer updates." : "Effective update catalog: " + effective;
    }

    private InstallerSettings CurrentSettings()
    {
        var server = serverSecurity?.Catalog == ServerCatalog.Text ? serverSecurity : new SourceSettings(ServerCatalog.Text, PublisherKey: serverSecurity?.PublisherKey);
        var updater = SameSource.IsChecked == true ? null : installerSecurity?.Catalog == InstallerCatalog.Text ? installerSecurity : new SourceSettings(InstallerCatalog.Text, PublisherKey: installerSecurity?.PublisherKey ?? serverSecurity?.PublisherKey);
        return new(server.Catalog, updater?.Catalog, server.Authentication, updater?.Authentication ?? "anonymous", server.PublisherKey, updater?.PublisherKey);
    }

    private void JsonClicked(object sender, RoutedEventArgs e)
    {
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure failure)
        {
            StatusText.Text = failure.Error.Message;
            return;
        }
        var text = new TextBox
        {
            Text = SettingsCodec.Serialize(settings), IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14,
            Margin = new Thickness(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        System.Windows.Automation.AutomationProperties.SetName(text, "Settings JSON preview");
        new Window { Owner = this, Title = "Settings JSON — current editor values", Width = 740, Height = 420,
            Content = text, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsPage is null) return;
        ShowSelectedPage();
    }

    private void ShowSelectedPage()
    {
        var pages = new[]
        {
            (Item: InstallationsNavigation, Panel: InstallationsPage, Description: "Manage independent server versions for exact SpatialAnalyzer releases."),
            (Item: SdkNavigation, Panel: SdkPage, Description: "Understand SDK setup before planning maintenance."),
            (Item: ActivityNavigation, Panel: ActivityPage, Description: "Review package-management and settings activity."),
            (Item: SettingsNavigation, Panel: SettingsPage, Description: "Configure Briosa, including package sources and installer updates."),
        };
        foreach (var page in pages)
        {
            var selected = Navigation.SelectedItem == page.Item;
            page.Panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (!selected) continue;
            PageTitle.Text = page.Item.Content.ToString();
            PageDescription.Text = page.Description;
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        SaveButton.IsEnabled = !value && snapshot is not null;
        ReloadButton.IsEnabled = !value;
        JsonButton.IsEnabled = !value;
        ServerCatalog.IsEnabled = !value;
        SameSource.IsEnabled = !value;
        UpdateEffectiveSource();
        UpdateCatalogControls();
        UpdateManagementControls();
    }

    private void AddActivity(string message)
    {
        ActivityList.Items.Insert(0, message);
        if (ActivityList.Items.Count > 50) ActivityList.Items.RemoveAt(50);
    }

    private bool ConfirmDiscard() => !dirty || MessageBox.Show(this,
        "Discard your unsaved settings?", "Briosa Installer", MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = busy || !ConfirmDiscard();
        if (!e.Cancel) catalogRead?.Cancel();
    }
}
