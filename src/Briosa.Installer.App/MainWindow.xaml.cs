using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow : Window
{
    private readonly ConfigurationPaths paths;
    private readonly SettingsStore store = new();
    private SettingsSnapshot? snapshot;
    private InstallerSettings? editorBaseline;
    private bool initialized, populating, dirty, busy, reviewing, externalChange, checkingExternal;
    private bool startupComplete;
    private readonly LiveStatus[] liveStatuses;
    public bool IsWorking => busy || reviewing;

    public MainWindow(ConfigurationPaths paths, ReleaseCatalogClient? catalogClient = null, PackageStore? packageStore = null,
        ICredentialStore? credentials = null, ISdkDiscovery? sdkDiscovery = null, Func<string, string, bool>? confirmAction = null, string? bootstrapPath = null,
        ISdkRegistrationService? sdkRegistration = null)
    {
        this.paths = paths;
        this.credentials = credentials ?? new WindowsCredentialStore();
        this.catalogClient = catalogClient ?? new ReleaseCatalogClient(credentials: this.credentials);
        ownsCatalogClient = catalogClient is null;
        this.packageStore = packageStore ?? new PackageStore(credentials: this.credentials);
        this.sdkDiscovery = sdkDiscovery ?? new WindowsSdkDiscovery();
        this.sdkRegistration = sdkRegistration ?? new SdkRegistrationService();
        this.confirmAction = confirmAction; this.bootstrapPath = bootstrapPath;
        activity = new ActivityStore(System.IO.Path.GetDirectoryName(paths.ExplicitFile ?? paths.UserFile)!);
        InitializeComponent();
        settingsTimer.Tick += async (_, _) => { settingsTimer.Stop(); await PersistSettingsAsync(); };
        liveStatuses = new[] { StatusText, CatalogStatusText, OperationStatusText, UpdateStatusText,
            UpdateOperationStatusText, ServerTestText, InstallerTestText, SdkSummaryText, SdkRecommendationText, SdkNextStepText, SdkRefreshStatusText, SdkMaintenanceStatusText, ActivityStatusText }
            .Select(text => new LiveStatus(text)).ToArray();
        configuringScope = true;
        if (this.packageStore.Root.TrimEnd('\\', '/').Equals(PackageStore.MachineRoot, StringComparison.OrdinalIgnoreCase)) StoreScope.SelectedIndex = 1;
        else if (!this.packageStore.Root.TrimEnd('\\', '/').Equals(PackageStore.UserRoot, StringComparison.OrdinalIgnoreCase))
        {
            customStore = this.packageStore;
            StoreScope.Items.Add(new ComboBoxItem { Content = "Explicit package directory" }); StoreScope.SelectedIndex = 2;
        }
        configuringScope = false; initialized = true;
        SettingsPath.Text = paths.ExplicitFile ?? paths.UserFile;
        BuildVersionText.Text = currentInstallerVersion;
        CurrentInstallerVersionText.Text = "Running version " + currentInstallerVersion;
        UpdateScopeLabels(); ShowSelectedPage(); RefreshActivity(); SetBusy(true);
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        await RefreshInventoryAsync();
        startupComplete = true;
        var initialFocus = System.Windows.Input.Keyboard.FocusedElement;
        await EnsureSdkSetupAsync();
        await EnsureServerCatalogAsync();
        if (!catalogClosed && Navigation.SelectedItem == InstallationsNavigation && System.Windows.Input.Keyboard.FocusedElement == initialFocus)
        {
            if (InventoryEmpty.Visibility == Visibility.Visible) EmptyActionButton.Focus(); else ServerSearch.Focus();
        }
    }

    private async Task ReloadAsync()
    {
        settingsTimer.Stop();
        if (writingSettings && settingsWrite is not null) await settingsWrite;
        InvalidateCatalog(); InvalidateSourceTests(); SetBusy(true);
        try
        {
            var loaded = await Task.Run(() => store.Load(paths));
            snapshot = loaded is Outcome<SettingsSnapshot>.Success success ? success.Value : null;
            externalChange = false; settingsError = null;
            if (loaded is Outcome<SettingsSnapshot>.Failure failure)
            {
                settingsError = failure.Error.Message;
                StatusText.Text = failure.Error.Message; CatalogStatusText.Text = "Settings need attention. Open package source settings.";
                RecordActivity("Settings.Load", failure.Error.Code.ToString()); return;
            }
            InstallerSettings? defaults = null;
            if (snapshot!.Origin == SettingsOrigin.SetupRequired)
            {
                try { defaults = DistributionDefaults.Load(); }
                catch (ManagementException) { StatusText.Text = "Public defaults could not be read. Enter an explicit catalog."; }
            }
            PopulateEditor(snapshot.Settings ?? defaults);
            dirty = false;
            try
            {
                PolicyText.Text = EnterprisePolicy.Load() is null ? "" : "Managed by your organization: source and publisher restrictions apply.";
                PolicyText.Visibility = Show(PolicyText.Text.Length > 0);
            }
            catch (ManagementException e) { PolicyText.Text = e.Message; PolicyText.Visibility = Visibility.Visible; }
            StatusText.Text = snapshot.Origin switch
            {
                SettingsOrigin.SetupRequired => defaults is null ? "Enter a catalog to get started. Changes are saved automatically." : "The default source is ready. Enter your enterprise mirror to change it.",
                SettingsOrigin.MachineDefaults => "Using your organization's defaults. Changes are saved to your personal settings file.",
                _ => "Settings are saved automatically.",
            };
            if (defaults is not null) ScheduleSettings();
        }
        finally { populating = false; SetBusy(false); RebuildInventory(); }
    }

    private void PopulateEditor(InstallerSettings? settings)
    {
        populating = true;
        ServerCatalog.Text = settings?.ServerCatalog ?? ""; InstallerCatalog.Text = settings?.InstallerCatalog ?? "";
        SameSource.IsChecked = settings?.InstallerCatalog is null;
        serverSecurity = settings?.Source(CatalogComponent.Server);
        installerSecurity = settings?.InstallerCatalog is null ? null : settings.Source(CatalogComponent.Installer);
        ThemeSelector.SelectedIndex = settings?.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        BrandTheme.ApplyPreference(Application.Current, settings?.Theme ?? "system");
        editorBaseline = CurrentSettings(); populating = false;
        UpdateEffectiveSource(); UpdateSecurityLabels();
    }

    private async void ReloadClicked(object sender, RoutedEventArgs e)
    {
        if (!busy && !reviewing)
        {
            await ReloadAsync();
            await EnsureServerCatalogAsync();
        }
    }

    private void SourceChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized || populating) return;
        UpdateEffectiveSource();
        InvalidateCatalog(); InvalidateSourceTests(); UpdateSecurityLabels();
        ScheduleSettings(debounce: sender is TextBox);
    }

    private InstallerSettings CurrentSettings()
    {
        var server = ServerCatalog.Text.Length == 0 ? new SourceSettings("") : serverSecurity?.Catalog == ServerCatalog.Text ? serverSecurity : new SourceSettings(ServerCatalog.Text, PublisherKey: serverSecurity?.PublisherKey);
        var updater = SameSource.IsChecked == true ? null : installerSecurity?.Catalog == InstallerCatalog.Text ? installerSecurity :
            new SourceSettings(InstallerCatalog.Text, PublisherKey: installerSecurity?.PublisherKey ?? serverSecurity?.PublisherKey);
        return new(server.Catalog, updater?.Catalog, server.Authentication, updater?.Authentication ?? "anonymous", server.PublisherKey, updater?.PublisherKey,
            (ThemeSelector.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "system");
    }

    private void UpdateEffectiveSource()
    {
        if (!initialized) return;
        InstallerSourceOverride.Visibility = Show(SameSource.IsChecked != true);
        InstallerCatalog.IsEnabled = SameSource.IsChecked != true && !busy && !reviewing;
        var effective = CurrentSettings().EffectiveInstallerCatalog;
        EffectiveSource.Text = string.IsNullOrWhiteSpace(effective) ? "Choose the server source above, or use a separate update source." :
            (SameSource.IsChecked == true ? "Shares the server catalog, authentication, and publisher.\n" : "Separate source for installer updates.\n") + effective;
        SourceSummaryButton.Content = !HasSource ? "Configure package source" : "Package source · " + SourceLabel(snapshot!.Settings!.ServerCatalog);
        SourceSummaryButton.ToolTip = snapshot?.Settings?.ServerCatalog;
        UpdateSourceText.Text = !HasSource ? "Configure a source in Package sources." : "Update source: " + SourceLabel(snapshot!.Settings!.EffectiveInstallerCatalog);
        UpdateSourceText.ToolTip = snapshot?.Settings?.EffectiveInstallerCatalog;
    }

    private static string SourceLabel(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https") return uri.Host;
        return source.StartsWith(@"\\", StringComparison.Ordinal) ? "Network catalog" : "Local catalog";
    }

    private async void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        ShowSelectedPage();
        if (settingsTimer.IsEnabled) await PersistSettingsAsync();
        await EnsureSdkSetupAsync();
        await EnsureServerCatalogAsync();
    }
    private void ShowSelectedPage()
    {
        var pages = new[]
        {
            (Item: InstallationsNavigation, Panel: InstallationsPage, Description: "Briosa gRPC servers for your SpatialAnalyzer releases."),
            (Item: SdkNavigation, Panel: SdkPage, Description: "Understand installed SA products and SDK registration."),
            (Item: ActivityNavigation, Panel: ActivityPage, Description: "Package operations, source checks, and setup history."),
            (Item: SettingsNavigation, Panel: SettingsPage, Description: "Package sources, installer updates, and preferences."),
        };
        foreach (var page in pages)
        {
            var selected = Navigation.SelectedItem == page.Item;
            page.Panel.Visibility = Show(selected);
            if (selected) { PageTitle.Text = page.Item.Content.ToString(); PageDescription.Text = page.Description; }
        }
        if (Navigation.SelectedItem == ActivityNavigation) RefreshActivity();
    }
    private void ConfigureSourceClicked(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = SettingsNavigation; SettingsSections.SelectedItem = SourcesSection;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { ServerCatalog.BringIntoView(); ServerCatalog.Focus(); }));
    }
    private void OpenStorageClicked(object sender, RoutedEventArgs e)
    { Navigation.SelectedItem = SettingsNavigation; SettingsSections.SelectedItem = AdvancedSection; StoreScope.Focus(); }
    private void SetBusy(bool value) { busy = value; UpdateInterface(); }
    private static Visibility Show(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateInterface()
    {
        if (!initialized) return;
        var editable = !busy && !reviewing;
        var configured = HasSource && !dirty && !externalChange && !IsSavingSettings;
        SettingsRecoveryBar.Visibility = Show(settingsError is not null);
        UnsavedNavigationText.Visibility = Show(settingsError is not null);
        RetrySettingsButton.IsEnabled = editable && !externalChange && snapshot is not null && !IsSavingSettings;
        ReloadButton.IsEnabled = ReloadSettingsButton.IsEnabled = editable && !writingSettings;
        JsonButton.IsEnabled = ImportSettingsButton.IsEnabled = ExportSettingsButton.IsEnabled = editable;
        ServerCatalog.IsEnabled = SameSource.IsEnabled = editable;
        ThemeSelector.IsEnabled = editable;
        ServerSecurityButton.IsEnabled = InstallerSecurityButton.IsEnabled = editable && sourceTest is null;
        TestServerButton.IsEnabled = TestInstallerButton.IsEnabled = editable && sourceTest is null;
        CancelSourceTestButton.Visibility = Show(sourceTest is not null);
        StoreScope.IsEnabled = editable; RefreshInstallerVersionsButton.IsEnabled = editable;
        UpdateEffectiveSource();
        RefreshCatalogButton.IsEnabled = editable && configured && catalogRead is null;
        CancelCatalogButton.IsEnabled = catalogRead is not null; CancelCatalogButton.Visibility = Show(catalogRead is not null);
        var row = CatalogPackages.SelectedItem as ServerRow;
        ServerSelectionPanel.Visibility = Show(row is not null);
        if (row is not null) SelectedServerText.Text = $"Server {row.Version} · SA {row.Target}";
        InstallPackageButton.Visibility = Show(row?.Installed is null);
        InstallPackageButton.IsEnabled = editable && configured && inventoryFailure is null && catalogRead is null && catalogSnapshot?.Publisher is not null && row is { Available: true, Installed: null };
        InstallPackageButton.ToolTip = catalogSnapshot?.Publisher is null ? "Configure an approved publisher in Settings to enable installation." : "Review and install this server version";
        VerifyInstalledButton.Visibility = RepairInstalledButton.Visibility = RemoveInstalledButton.Visibility = Show(row?.Installed is not null);
        VerifyInstalledButton.IsEnabled = RemoveInstalledButton.IsEnabled = editable && row?.Installed is not null;
        RepairInstalledButton.IsEnabled = editable && configured && row?.Installed is not null;
        ServerDetailsButton.IsEnabled = editable && row is not null;
        var installer = InstalledInstallerVersions.SelectedItem as DownloadedInstaller;
        ActivateInstallerButton.IsEnabled = VerifyInstallerButton.IsEnabled = RemoveInstallerButton.IsEnabled = InstallerDetailsButton.IsEnabled = editable && installer is not null;
        RepairInstallerButton.IsEnabled = editable && configured && installer is not null;
        CheckUpdatesButton.IsEnabled = editable && configured && updaterRead is null;
        CancelUpdateCheckButton.IsEnabled = updaterRead is not null; CancelUpdateCheckButton.Visibility = Show(updaterRead is not null);
        InstallUpdateButton.Visibility = Show(recommendedUpdate is not null && pendingInstallerVersion is null);
        InstallUpdateButton.IsEnabled = editable && configured && updaterRead is null && updaterSnapshot?.Publisher is not null && recommendedUpdate is not null;
        UseReleaseButton.IsEnabled = editable && configured && updaterRead is null && updaterSnapshot?.Publisher is not null && InstallerReleases.SelectedItem is InstallerRelease;
        RestartInstallerButton.Visibility = Show(pendingInstallerVersion is not null);
        RestartInstallerButton.Content = pendingInstallerVersion is not null && CompareVersions(pendingInstallerVersion, currentInstallerVersion) < 0 ? "Restart in selected version…" : "Restart to update…";
        RestartInstallerButton.IsEnabled = editable && !dirty;
        CancelOperationButton.IsEnabled = CancelUpdateOperationButton.IsEnabled = operation is not null;
        CancelOperationButton.Visibility = Show(operation is not null && !installerOperation);
        CancelUpdateOperationButton.Visibility = Show(operation is not null && installerOperation);
        OperationProgress.Visibility = Show(operation is not null && !installerOperation);
        UpdateProgress.Visibility = Show(operation is not null && installerOperation);
        RecoverStoreButton.IsEnabled = RecoverInstallerStoreButton.IsEnabled = editable;
        RefreshSdkButton.IsEnabled = editable && startupComplete && !sdkReading;
        // Keep the read-only evidence readable/selectable during maintenance.
        // Only actions need disabling; the native disabled DataGrid paints a white surface.
        ChangeSdkButton.IsEnabled = editable && !sdkReading && !sdkChanging && sdkReport?.Observations.Any(o => o.Kind == "Installed SA product") == true;
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closingAfterSave) return;
        if (IsWorking)
        { e.Cancel = true; OperationStatusText.Text = UpdateOperationStatusText.Text = "Wait for the current operation, or cancel it before closing."; return; }
        if (dirty || IsSavingSettings)
        {
            e.Cancel = true;
            await PersistSettingsAsync();
            if (dirty && !ConfirmAction("Settings could not be applied", "Some settings could not be saved. Close with the last saved configuration?", "Close")) return;
            closingAfterSave = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        if (!e.Cancel || closingAfterSave) { settingsTimer.Stop(); catalogRead?.Cancel(); updaterRead?.Cancel(); sourceTest?.Cancel(); }
    }
    private async void WindowActivated(object? sender, EventArgs e)
    {
        if (!initialized || busy || IsSavingSettings || checkingExternal || snapshot is null || catalogClosed) return;
        checkingExternal = true;
        var captured = snapshot;
        try
        {
            if (!await SavedSettingsMatchAsync(captured) && captured == snapshot && !catalogClosed)
            {
                if (!dirty) { await ReloadAsync(); await EnsureServerCatalogAsync(); return; }
                externalChange = true; InvalidateCatalog();
                settingsError = "The settings file changed in another window. Reload from disk to continue.";
                StatusText.Text = settingsError;
                CatalogStatusText.Text = "Settings changed on disk. Open Settings and reload.";
                UpdateInterface();
            }
        }
        finally { checkingExternal = false; }
    }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!initialized) return;
        var compact = ActualWidth < 980;
        NavigationColumn.Width = new GridLength(208);
        PageHost.Margin = compact ? new Thickness(20, 18, 20, 18) : new Thickness(28, 24, 28, 24);

    }
}
