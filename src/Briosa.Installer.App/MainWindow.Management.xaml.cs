using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private PackageStore packageStore;
    private readonly ICredentialStore credentials;
    private readonly ISdkDiscovery sdkDiscovery;
    private readonly ActivityStore activity;
    private CancellationTokenSource? operation;
    private readonly Func<string, string, bool>? confirmAction;
    private readonly string? bootstrapPath;
    private bool configuringScope, installerOperation;
    private int operationGeneration;
    private PackageStore? customStore;

    private bool ConfirmAction(string title, string body, string action = "Continue", IEnumerable<ReviewFact>? facts = null, string? details = null) =>
        confirmAction?.Invoke(title, body + "\n" + string.Join("\n", (facts ?? []).Select(f => f.Label + ": " + f.Value))) ??
        new ReviewDialog(title, body, action, facts, details) { Owner = this }.ShowDialog() == true;

    private void UpdateScopeLabels()
    {
        var label = StoreScope.SelectedIndex switch { 0 => "Current user", 1 => "All users", _ => "Explicit package directory" };
        ScopeSummaryButton.Content = label + " · Change location";
        ScopeSummaryButton.ToolTip = packageStore.Root;
        StoreLocationText.Text = packageStore.Root;
        InstallerStoreLocationText.Text = "Shared package location: " + label + "\n" + packageStore.Root;
    }
    private async void StoreScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || busy || configuringScope) return;
        packageStore = StoreScope.SelectedIndex == 2 && customStore is not null ? customStore :
            new PackageStore(StoreScope.SelectedIndex == 1 ? PackageStore.MachineRoot : PackageStore.UserRoot, credentials);
        pendingInstallerVersion = null; installedPackages = [];
        UpdateScopeLabels(); OperationStatusText.Text = UpdateOperationStatusText.Text = "";
        await RefreshInventoryAsync();
    }
    private async void RefreshInstalledClicked(object sender, RoutedEventArgs e)
    { if (!busy) await RefreshInventoryAsync(); }
    private async Task RefreshInventoryAsync()
    {
        if (busy) return;
        SetBusy(true);
        try { await ReadInventoryAsync(); }
        finally { SetBusy(false); RebuildInventory(); }
    }
    private async Task ReadInventoryAsync()
    {
        try { installedPackages = await Task.Run(packageStore.List); inventoryFailure = null; }
        catch (ManagementException e)
        {
            inventoryFailure = e.Message; installedPackages = [];
            RecoverStoreButton.Visibility = Show(e.Code == ManagementError.RecoveryRequired);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { inventoryFailure = "Local inventory could not be read. Check this package location and its permissions."; installedPackages = []; }
        var selected = (InstalledInstallerVersions.SelectedItem as DownloadedInstaller)?.Package.Id;
        var installers = installedPackages.Where(p => p.Receipt.Package.Component == CatalogComponent.Installer)
            .OrderByDescending(p => p.Version, Comparer<string>.Create(CompareVersions)).Select(p => new DownloadedInstaller(p)).ToArray();
        InstalledInstallerVersions.ItemsSource = installers;
        InstalledInstallerVersions.SelectedItem = installers.FirstOrDefault(p => p.Package.Id == selected);
        InstalledInstallerVersions.Visibility = Show(installers.Length > 0);
        DownloadedEmptyText.Visibility = Show(installers.Length == 0);
        DownloadedEmptyText.Text = inventoryFailure ?? "No installer versions have been downloaded to this location.";
        try
        {
            var selectedInstaller = await Task.Run(packageStore.ReadInstallerSelection);
            pendingInstallerVersion = selectedInstaller is not null && CompareVersions(selectedInstaller.Version, currentInstallerVersion) != 0 ? selectedInstaller.Version : null;
            CurrentInstallerVersionText.Text = "Running version " + currentInstallerVersion +
                (selectedInstaller is null ? "" : "\nSelected for next launch: " + selectedInstaller.Version);
        }
        catch (Exception e) when (e is ManagementException or IOException or UnauthorizedAccessException or JsonException)
        { pendingInstallerVersion = null; UpdateOperationStatusText.Text = "The next-launch selection could not be read. Review downloaded versions or recover the package location in Advanced."; }
        if (inventoryFailure is not null) OperationStatusText.Text = inventoryFailure;
        RebuildInventory();
    }
    private bool IsInstallerAction(object sender) => ReferenceEquals(sender, VerifyInstallerButton) || ReferenceEquals(sender, RepairInstallerButton) ||
        ReferenceEquals(sender, RemoveInstallerButton) || ReferenceEquals(sender, RecoverInstallerStoreButton);
    private InstalledPackage? SelectedInstalledPackage(object sender) => IsInstallerAction(sender)
        ? (InstalledInstallerVersions.SelectedItem as DownloadedInstaller)?.Package : (CatalogPackages.SelectedItem as ServerRow)?.Installed;
    private void InstalledSelectionChanged(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateInterface(); }
    private void InstallerDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (InstalledInstallerVersions.SelectedItem is DownloadedInstaller item)
            new DetailsDialog("Downloaded installer details", $"Briosa Installer {item.Version}\n" + InstalledDetails(item.Package)) { Owner = this }.ShowDialog();
    }
    private async Task<bool> RunOperationAsync(string code, Func<CancellationToken, Task> action, bool installer = false, CatalogPackage? package = null)
    {
        if (busy) return false;
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        operationGeneration++; installerOperation = installer;
        var progress = installer ? UpdateProgress : OperationProgress;
        var status = installer ? UpdateOperationStatusText : OperationStatusText;
        var timer = Stopwatch.StartNew();
        var inventoryRead = false;
        SetBusy(true); progress.IsIndeterminate = true;
        status.Text = "Working…";
        try
        {
            await Task.Run(() => action(cancellation.Token));
            await ReadInventoryAsync();
            inventoryRead = true;
            status.Text = (code switch
            {
                "Package.Install" => $"Server {package?.Version} installed for SA {package?.TargetDisplay}. Existing versions are preserved.",
                "Package.Verify" => $"Briosa {package?.ComponentName} {package?.Version}: files verified against the installed receipt.",
                "Package.Repair" => $"Briosa {package?.ComponentName} {package?.Version} repaired and verified.",
                "Package.Remove" => $"Briosa {package?.ComponentName} {package?.Version} removed. Other versions are unchanged.",
                "Package.Recover" => "Interrupted package operations recovered.",
                _ => "Completed.",
            }) + (inventoryFailure is null ? "" : "\n" + inventoryFailure);
            RecoverStoreButton.Visibility = Visibility.Collapsed;
            RecordActivity(code, "Succeeded", package, timer.ElapsedMilliseconds);
            return true;
        }
        catch (ManagementException e)
        {
            status.Text = e.Message;
            RecoverStoreButton.Visibility = Show(e.Code == ManagementError.RecoveryRequired);
            RecordActivity(code, e.Code.ToString(), package, timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        { status.Text = "Operation cancelled. Existing complete packages were preserved."; RecordActivity(code, "Cancelled", package, timer.ElapsedMilliseconds); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            status.Text = "The operation could not complete. Check access and package integrity. If an interrupted operation is reported, use Recover in Settings → Advanced.";
            RecordActivity(code, "Failed", package, timer.ElapsedMilliseconds);
        }
        finally
        {
            // A package may have committed before a later bootstrap or inventory failure.
            if (!inventoryRead) await ReadInventoryAsync();
            operation = null; progress.IsIndeterminate = false; progress.Value = 0; SetBusy(false);
        }
        return false;
    }
    private IProgress<PackageProgress> PackageProgress(bool installer = false)
    {
        var generation = operationGeneration + 1;
        return new Progress<PackageProgress>(value =>
        {
            if (operation is null || generation != operationGeneration) return;
            var progress = installer ? UpdateProgress : OperationProgress;
            (installer ? UpdateOperationStatusText : OperationStatusText).Text = value.Phase;
            progress.IsIndeterminate = value.Total == 0;
            if (value.Total > 0) progress.Value = 100d * value.Bytes / value.Total;
        });
    }
    private async void InstallPackageClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || externalChange || inventoryFailure is not null || catalogRead is not null || catalogSnapshot?.Publisher is null || snapshot?.Settings is null ||
            CatalogPackages.SelectedItem is not ServerRow { Available: true, Installed: null } row) return;
        reviewing = true; UpdateInterface();
        try
        {
            var package = row.Package; var captured = snapshot; var catalog = catalogSnapshot; var generation = catalogGeneration;
            if (!await SavedSettingsMatchAsync(captured))
            { InvalidateCatalog(); OperationStatusText.Text = "Source settings changed. Reload Settings and check the source before installing."; return; }
            if (busy || dirty || generation != catalogGeneration || catalogClosed || !ReferenceEquals(CatalogPackages.SelectedItem, row)) return;
            if (!ConfirmAction("Install server", "This adds an independent server version. Existing versions remain installed; application teams choose when to use it.", "Install",
                [new("Briosa server", package.Version), new("SpatialAnalyzer release", package.TargetDisplay), new("Package source", catalog.Source),
             new("Install location", packageStore.Root), new("Download", package.SizeDisplay), new("Verification", "Publisher verified. Payload integrity will be checked during installation.")],
                "Approved publisher SHA-256: " + catalog.Publisher.Fingerprint + "\nCatalog SHA-256: " + catalog.ContentSha256)) return;
            var progress = PackageProgress();
            if (await RunOperationAsync("Package.Install", token => packageStore.InstallAsync(captured.Settings!, CatalogComponent.Server, package.Id, catalog.ContentSha256,
                progress: progress, token: token, configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured), package: package))
            { InventoryFilter.SelectedIndex = 0; RebuildInventory(package.Id); }
        }
        finally { reviewing = false; UpdateInterface(); }
    }
    private async void VerifyInstalledClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedInstalledPackage(sender) is { } package)
            await RunOperationAsync("Package.Verify", token => packageStore.VerifyAsync(package.Id, token), IsInstallerAction(sender), package.Receipt.Package);
    }
    private async void RepairInstalledClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || externalChange || SelectedInstalledPackage(sender) is not { } package || snapshot?.Settings is null) return;
        reviewing = true; UpdateInterface();
        try
        {
            var installer = IsInstallerAction(sender); var captured = snapshot;
            if (!await SavedSettingsMatchAsync(captured))
            { (installer ? UpdateOperationStatusText : OperationStatusText).Text = "Reload changed source settings before repairing."; return; }
            if (busy || dirty || snapshot != captured || SelectedInstalledPackage(sender)?.Id != package.Id) return;
            if (!ConfirmAction("Repair package", "Restore the exact installed artifact. Its original publisher and payload digest must still match. Close applications using this package before proceeding.", "Repair",
                [new("Version", package.Version), new("Product", package.Component == "server" ? "Briosa server for SA " + package.Target : "Briosa Installer"), new("Location", package.Directory)])) return;
            var progress = PackageProgress(installer);
            await RunOperationAsync("Package.Repair", async token =>
            {
                var loaded = await catalogClient.ReadAsync(captured.Settings!, package.Receipt.Package.Component, token);
                if (loaded is not CatalogResult<CatalogSnapshot>.Success success) throw new ManagementException(ManagementError.SourceUnavailable);
                await packageStore.InstallAsync(captured.Settings!, package.Receipt.Package.Component, package.Id, success.Value.ContentSha256, repair: true, progress: progress, token: token,
                    configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured);
            }, installer, package.Receipt.Package);
        }
        finally { reviewing = false; UpdateInterface(); }
    }
    private async void RemoveInstalledClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedInstalledPackage(sender) is not { } package || busy) return;
        var installer = IsInstallerAction(sender);
        var guidance = installer ? "The selected or running installer cannot be removed. Choose another version and close windows using this version first." :
            "Your engineering team should confirm its applications no longer need this version. Other server versions will remain installed.";
        if (!ConfirmAction("Remove package", guidance, "Remove", [new("Version", package.Version), new("Product", package.Component == "server" ? "Briosa server for SA " + package.Target : "Briosa Installer"), new("Location", package.Directory)])) return;
        await RunOperationAsync("Package.Remove", _ => { packageStore.Remove(package.Id); return Task.CompletedTask; }, installer, package.Receipt.Package);
    }
    private async void RecoverStoreClicked(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmAction("Recover interrupted operations", "Clean up incomplete staging in this package location. If a repair stopped before committing its replacement, the previous package is restored.", "Recover", [new("Location", packageStore.Root)])) return;
        await RunOperationAsync("Package.Recover", _ => { packageStore.Recover(); return Task.CompletedTask; }, IsInstallerAction(sender));
    }
    private void CancelOperationClicked(object sender, RoutedEventArgs e) => operation?.Cancel();

    private async void ActivateInstallerClicked(object sender, RoutedEventArgs e)
    {
        if (InstalledInstallerVersions.SelectedItem is not DownloadedInstaller { Package: var package } || busy || dirty) return;
        var rollback = CompareVersions(package.Version, currentInstallerVersion) < 0;
        if (!ConfirmAction(rollback ? "Return to previous installer" : "Use downloaded installer", "Select this verified version for the next launch. Your settings and installed servers are preserved.", rollback ? "Select older version" : "Use version",
            [new("Running version", currentInstallerVersion), new("Selected version", package.Version)])) return;
        if (await RunOperationAsync("Installer.Activate", async token =>
        {
            await packageStore.ActivateInstallerAsync(package.Id, token);
            if (bootstrapPath is not null) await BootstrapUpdater.RefreshAsync(packageStore, package.Id, bootstrapPath, token);
        }, true, package.Receipt.Package)) PrepareRestart(package.Version);
    }
    private void PrepareRestart(string version)
    {
        pendingInstallerVersion = version;
        UpdateOperationStatusText.Text = $"Installer {version} selected for the next launch. Restart when you are ready.";
        RestartInstallerButton.Content = CompareVersions(version, currentInstallerVersion) < 0 ? "Restart in selected version…" : "Restart to update…";
        UpdateInterface();
    }
    private async void RestartInstallerClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || pendingInstallerVersion is null ||
            !ConfirmAction("Restart installer", "Open the selected installer now? This window will close. Settings and installed servers are preserved.", "Restart")) return;
        SetBusy(true); var started = false;
        try
        {
            var target = await packageStore.ResolveActiveInstallerAsync();
            if (target is null) throw new ManagementException(ManagementError.PackageNotFound);
            var start = new ProcessStartInfo(target) { UseShellExecute = false };
            start.ArgumentList.Add("--store"); start.ArgumentList.Add(packageStore.Root);
            if (bootstrapPath is not null) { start.ArgumentList.Add("--bootstrap"); start.ArgumentList.Add(bootstrapPath); }
            if (paths.ExplicitFile is not null) { start.ArgumentList.Add("--config"); start.ArgumentList.Add(paths.ExplicitFile); }
            using var process = Process.Start(start); started = process is not null;
        }
        catch (Exception ex) when (ex is ManagementException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { UpdateOperationStatusText.Text = "The selected installer could not start. This window remains available; verify the downloaded version or select another."; }
        finally { SetBusy(false); }
        if (started) Close();
    }
}
