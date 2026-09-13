using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private PackageStore packageStore;
    private readonly ICredentialStore credentials;
    private readonly ISdkDiscovery sdkDiscovery;
    private readonly ActivityStore activity;
    private SourceSettings? serverSecurity, installerSecurity;
    private CancellationTokenSource? operation;
    private SdkReport? sdkReport;
    private readonly Func<string, string, bool>? confirmAction;
    private readonly string? bootstrapPath;
    private bool configuringScope;
    private PackageStore? customStore;
    private bool ConfirmAction(string title, string body) => confirmAction?.Invoke(title, body) ??
        MessageBox.Show(this, body, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void UpdateManagementControls()
    {
        if (CancelOperationButton is null) return;
        var editable = !busy;
        ServerSecurityButton.IsEnabled = editable;
        InstallerSecurityButton.IsEnabled = editable;
        ImportSettingsButton.IsEnabled = editable;
        ExportSettingsButton.IsEnabled = editable;
        StoreScope.IsEnabled = editable;
        InstallerStoreScope.IsEnabled = editable;
        InstallPackageButton.IsEnabled = editable && !dirty && catalogRead is null && catalogSnapshot?.Publisher is not null && CatalogPackages.SelectedItem is CatalogPackage;
        var installed = InstalledPackages.SelectedItem as InstalledPackage;
        VerifyInstalledButton.IsEnabled = editable && installed is not null;
        RepairInstalledButton.IsEnabled = editable && !dirty && installed is not null && snapshot?.Settings is not null;
        RemoveInstalledButton.IsEnabled = editable && installed is not null;
        var installer = InstalledInstallerVersions.SelectedItem as InstalledPackage;
        ActivateInstallerButton.IsEnabled = editable && installer is not null;
        VerifyInstallerButton.IsEnabled = editable && installer is not null;
        RepairInstallerButton.IsEnabled = editable && !dirty && installer is not null && snapshot?.Settings is not null;
        RemoveInstallerButton.IsEnabled = editable && installer is not null;
        RefreshInstallerVersionsButton.IsEnabled = editable;
        RecoverInstallerStoreButton.IsEnabled = editable;
        CheckUpdatesButton.IsEnabled = editable && !dirty && updaterRead is null && snapshot?.Settings is not null;
        CancelUpdateCheckButton.IsEnabled = updaterRead is not null;
        InstallUpdateButton.IsEnabled = editable && !dirty && updaterRead is null && updaterSnapshot?.Publisher is not null &&
            InstallerReleases.SelectedItem is InstallerRelease;
        RecoverStoreButton.IsEnabled = editable;
        InspectSdkButton.IsEnabled = editable;
        ExportSdkButton.IsEnabled = editable;
        CancelOperationButton.IsEnabled = operation is not null;
        CancelUpdateOperationButton.IsEnabled = operation is not null;
    }
    private void UpdateSecurityLabels()
    {
        if (ServerSecurityText is null || InstallerSecurityText is null) return;
        static string Label(SourceSettings source) => $"Authentication: {source.Authentication}. Publisher: {(source.PublisherKey is null ? "not configured" : PublisherTrust.Fingerprint(source.PublisherKey)[..16] + "…")}";
        ServerSecurityText.Text = Label(CurrentSettings().Source(CatalogComponent.Server));
        InstallerSecurityText.Text = SameSource.IsChecked == true ? "Shares server source authentication and publisher." : Label(CurrentSettings().Source(CatalogComponent.Installer));
    }
    private void ServerSecurityClicked(object sender, RoutedEventArgs e) => EditSecurity(CatalogComponent.Server);
    private void InstallerSecurityClicked(object sender, RoutedEventArgs e) => EditSecurity(CatalogComponent.Installer);
    private void EditSecurity(CatalogComponent component)
    {
        if (busy) return;
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure invalid) { StatusText.Text = invalid.Error.Message; return; }
        var dialog = new SourceSecurityDialog(settings.Source(component), credentials) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (component == CatalogComponent.Server || SameSource.IsChecked == true) serverSecurity = dialog.Result;
        else installerSecurity = dialog.Result;
        dirty = true; InvalidateCatalog(); UpdateSecurityLabels();
        StatusText.Text = "Authentication and publisher selection updated. Save settings to apply them.";
    }
    private async void ImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard() || snapshot is null) return;
        var dialog = new OpenFileDialog { Filter = "Installer settings (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("Settings.Import", async _ =>
        {
            var imported = store.Load(new(dialog.FileName, ExplicitFile: dialog.FileName));
            if (imported is not Outcome<SettingsSnapshot>.Success { Value.Settings: { } settings }) throw new ManagementException(ManagementError.InvalidInput);
            if (store.Save(snapshot, settings) is Outcome<SettingsSnapshot>.Failure) throw new ManagementException(ManagementError.InvalidInput);
            await Task.CompletedTask;
        }, installer: true);
        await ReloadAsync();
    }
    private void ExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure failure) { StatusText.Text = failure.Error.Message; return; }
        var dialog = new SaveFileDialog { Filter = "Installer settings (*.json)|*.json", FileName = "briosa-settings.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, SettingsCodec.Serialize(settings)); StatusText.Text = "Settings exported. No token or password was included."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "Settings could not be exported. Check file access."; }
    }
    private void StoreScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StoreLocationText is null || busy || configuringScope) return;
        configuringScope = true;
        var index = ((ComboBox)sender).SelectedIndex;
        StoreScope.SelectedIndex = InstallerStoreScope.SelectedIndex = index;
        configuringScope = false;
        packageStore = StoreScope.SelectedIndex == 2 && customStore is not null ? customStore :
            new PackageStore(StoreScope.SelectedIndex == 1 ? PackageStore.MachineRoot : PackageStore.UserRoot, credentials);
        StoreLocationText.Text = packageStore.Root;
        InstallerStoreLocationText.Text = packageStore.Root;
        InstalledPackages.ItemsSource = null;
        InstalledInstallerVersions.ItemsSource = null;
        InstalledDetailsText.Text = "Refresh to inspect packages in this scope.";
        InstalledInstallerDetailsText.Text = "Refresh to inspect installer versions in this scope.";
        OperationStatusText.Text = UpdateOperationStatusText.Text = "";
        UpdateManagementControls();
    }
    private async void RefreshInstalledClicked(object sender, RoutedEventArgs e)
    { if (!busy) await RunOperationAsync("Package.List", _ => Task.CompletedTask, IsInstallerAction(sender)); }
    // Collapsed expanders need not have a visual ancestry yet. Route by the owning control.
    private bool IsInstallerAction(object sender) => ReferenceEquals(sender, RefreshInstallerVersionsButton) ||
        ReferenceEquals(sender, VerifyInstallerButton) || ReferenceEquals(sender, RepairInstallerButton) ||
        ReferenceEquals(sender, RemoveInstallerButton) || ReferenceEquals(sender, RecoverInstallerStoreButton);
    private InstalledPackage? SelectedInstalledPackage(object sender) =>
        (IsInstallerAction(sender) ? InstalledInstallerVersions : InstalledPackages).SelectedItem as InstalledPackage;
    private void InstalledSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var details = ReferenceEquals(sender, InstalledInstallerVersions) ? InstalledInstallerDetailsText : InstalledDetailsText;
        if (details is null) return;
        details.Text = ((DataGrid)sender).SelectedItem is InstalledPackage package
            ? $"{package.Id}\n{package.Directory}\nPublisher: {package.Receipt.Publisher.Fingerprint}\nPackage SHA-256: {package.Receipt.Package.Artifact.Sha256}\nInstalled: {package.Receipt.InstalledAt:u}" : "";
        UpdateManagementControls();
    }
    private async Task<bool> RunOperationAsync(string code, Func<CancellationToken, Task> action, bool installer = false)
    {
        if (busy) return false;
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        var progress = installer ? UpdateProgress : OperationProgress;
        var status = installer ? UpdateOperationStatusText : OperationStatusText;
        SetBusy(true); progress.IsIndeterminate = true;
        status.Text = "Working…";
        try
        {
            await Task.Run(() => action(cancellation.Token));
            var packages = await Task.Run(packageStore.List);
            InstalledPackages.ItemsSource = packages.Where(p => p.Receipt.Package.Component == CatalogComponent.Server).ToArray();
            InstalledInstallerVersions.ItemsSource = packages.Where(p => p.Receipt.Package.Component == CatalogComponent.Installer).ToArray();
            status.Text = "Completed.";
            activity.Record(code, "Succeeded"); AddActivity(code + ": completed.");
            return true;
        }
        catch (ManagementException e)
        { status.Text = e.Message; activity.Record(code, e.Code.ToString()); AddActivity(code + ": " + e.Code); }
        catch (OperationCanceledException)
        { status.Text = "Operation cancelled."; activity.Record(code, "Cancelled"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        { status.Text = "The operation could not complete. Check access and package integrity; use Recover if an interrupted operation is reported."; activity.Record(code, "Failed"); }
        finally
        {
            operation = null; progress.IsIndeterminate = false; progress.Value = 0; SetBusy(false);
        }
        return false;
    }
    private IProgress<PackageProgress> PackageProgress(bool installer = false) => new Progress<PackageProgress>(value =>
    {
        var progress = installer ? UpdateProgress : OperationProgress;
        (installer ? UpdateOperationStatusText : OperationStatusText).Text = value.Phase;
        progress.IsIndeterminate = value.Total == 0;
        if (value.Total > 0) progress.Value = 100d * value.Bytes / value.Total;
    });
    private async void InstallPackageClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || catalogRead is not null || catalogSnapshot?.Publisher is null || snapshot?.Settings is null ||
            CatalogPackages.SelectedItem is not CatalogPackage { Component: CatalogComponent.Server } package) return;
        var captured = snapshot; var catalog = catalogSnapshot;
        var generation = catalogGeneration;
        if (!await SavedSettingsMatchAsync(captured)) { InvalidateCatalog(); OperationStatusText.Text = "Source settings changed. Reload and refresh before installing."; return; }
        if (busy || dirty || generation != catalogGeneration || catalogClosed || !ReferenceEquals(CatalogPackages.SelectedItem, package)) return;
        if (!ConfirmAction("Review installation", $"Install {package.ComponentName} {package.Version}?\n\nSA target: {package.TargetDisplay}\nSource: {catalog.Source}\nScope: {packageStore.Root}\nDownload: {package.SizeDisplay}\nPublisher: {catalog.Publisher.Fingerprint}\n\nExisting versions will remain installed. This does not start SA or change application configuration.")) return;
        var progress = PackageProgress();
        if (await RunOperationAsync("Package.Install", token => packageStore.InstallAsync(captured.Settings!, CatalogComponent.Server, package.Id, catalog.ContentSha256, progress: progress, token: token,
            configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured)))
        { PackageTabs.SelectedIndex = 1; OperationStatusText.Text = "Package installed. Runtime readiness has not been tested."; }
    }
    private async void VerifyInstalledClicked(object sender, RoutedEventArgs e)
    { if (SelectedInstalledPackage(sender) is { } package) await RunOperationAsync("Package.Verify", token => packageStore.VerifyAsync(package.Id, token), IsInstallerAction(sender)); }
    private async void RepairInstalledClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || SelectedInstalledPackage(sender) is not { } package || snapshot?.Settings is null) return;
        var installer = IsInstallerAction(sender);
        var captured = snapshot;
        if (!await SavedSettingsMatchAsync(captured)) { (installer ? UpdateOperationStatusText : OperationStatusText).Text = "Reload changed source settings before repairing."; return; }
        if (busy || dirty || snapshot != captured || !ReferenceEquals(SelectedInstalledPackage(sender), package)) return;
        if (!ConfirmAction("Review repair", $"Restore the exact installed artifact for {package.Id}?\n\nIts original publisher and payload digest must still match the configured source. Stop applications using this package before proceeding.")) return;
        var progress = PackageProgress(installer);
        await RunOperationAsync("Package.Repair", async token =>
        {
            var loaded = await catalogClient.ReadAsync(captured.Settings!, package.Receipt.Package.Component, token);
            if (loaded is not CatalogResult<CatalogSnapshot>.Success success) throw new ManagementException(ManagementError.SourceUnavailable);
            await packageStore.InstallAsync(captured.Settings!, package.Receipt.Package.Component, package.Id, success.Value.ContentSha256, repair: true, progress: progress, token: token,
                configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured);
        }, installer);
    }
    private async void RemoveInstalledClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedInstalledPackage(sender) is not { } package || busy) return;
        var installer = IsInstallerAction(sender);
        var guidance = installer ? "The selected or running installer cannot be removed. Select another version and close windows using this version first." :
            "Your engineering team must confirm its applications no longer need this version. The installer does not track those applications.";
        if (!ConfirmAction("Review removal", $"Remove {package.Id}?\n\nLocation: {package.Directory}\n\n{guidance}")) return;
        await RunOperationAsync("Package.Remove", _ => { packageStore.Remove(package.Id); return Task.CompletedTask; }, installer);
    }
    private async void RecoverStoreClicked(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmAction("Review recovery", "Recover interrupted package operations in this store? Incomplete staging will be cleaned up; an old package is restored if a repair stopped before its replacement was committed.")) return;
        await RunOperationAsync("Package.Recover", _ => { packageStore.Recover(); return Task.CompletedTask; }, IsInstallerAction(sender));
    }
    private void CancelOperationClicked(object sender, RoutedEventArgs e) => operation?.Cancel();
    private async void ActivateInstallerClicked(object sender, RoutedEventArgs e)
    {
        if (InstalledInstallerVersions.SelectedItem is not InstalledPackage package || busy || package.Receipt.Package.Component != CatalogComponent.Installer) return;
        if (!ConfirmAction("Review installer update", $"Use installer {package.Version} on the next launch?\n\nSource settings and installed server packages will be preserved. The current app remains available until you close it.")) return;
        if (await RunOperationAsync("Installer.Activate", async token =>
        {
            await packageStore.ActivateInstallerAsync(package.Id, token);
            if (bootstrapPath is not null) await BootstrapUpdater.RefreshAsync(packageStore, package.Id, bootstrapPath, token);
        }, installer: true))
            await OfferInstallerRestartAsync(package.Version);
    }

    private async Task OfferInstallerRestartAsync(string version)
    {
        UpdateOperationStatusText.Text = $"Installer {version} selected for the next launch. Reopen through Briosa.Launcher.exe to use it.";
        if (!ConfirmAction("Restart installer", "Open the selected installer version now? This window will close. Source settings and server packages are preserved.")) return;
        SetBusy(true);
        var started = false;
        try
        {
            var target = await packageStore.ResolveActiveInstallerAsync();
            if (target is null) throw new ManagementException(ManagementError.PackageNotFound);
            var start = new ProcessStartInfo(target) { UseShellExecute = false };
            start.ArgumentList.Add("--store"); start.ArgumentList.Add(packageStore.Root);
            if (bootstrapPath is not null) { start.ArgumentList.Add("--bootstrap"); start.ArgumentList.Add(bootstrapPath); }
            if (paths.ExplicitFile is not null) { start.ArgumentList.Add("--config"); start.ArgumentList.Add(paths.ExplicitFile); }
            using var process = Process.Start(start);
            started = process is not null;
        }
        catch (Exception ex) when (ex is ManagementException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { UpdateOperationStatusText.Text = "The new installer could not be started. This window remains available."; }
        finally { SetBusy(false); }
        if (started) Close();
    }
    private async void InspectSdkClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            sdkReport = await Task.Run(sdkDiscovery.Inspect);
            SdkObservations.ItemsSource = sdkReport.Observations;
            SdkGuidanceText.Text = sdkReport.Observations.Count == 0 ? "No SA installation or SDK registration was found in the inspected Windows registry locations. " + sdkReport.Guidance : sdkReport.Guidance;
            activity.Record("Sdk.Inspect", "Succeeded");
        }
        catch (ManagementException ex) { SdkGuidanceText.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    private void SdkSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SdkDetailsText is null) return;
        SdkDetailsText.Text = SdkObservations.SelectedItem is SdkObservation item ? $"{item.Status}\n{item.Location}" : "";
    }
    private void ExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Filter = "Support report (*.json)|*.json", FileName = "briosa-support.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { activity.Export(dialog.FileName, sdkReport); AddActivity("Sanitized support report exported."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AddActivity("Support report could not be saved. Check file access."); }
    }
}
