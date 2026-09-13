using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private readonly ReleaseCatalogClient catalogClient;
    private readonly bool ownsCatalogClient;
    private CancellationTokenSource? catalogRead;
    private CatalogSnapshot? catalogSnapshot;
    private int catalogGeneration;
    private bool catalogClosed, rebuildingInventory;
    private string? catalogFailure, inventoryFailure;
    private enum EmptyAction { ConfigureSource, CheckSource, RefreshInventory, ClearFilters }
    private EmptyAction emptyAction;
    private IReadOnlyList<InstalledPackage> installedPackages = [];
    private List<ServerRow> serverRows = [];

    private void InvalidateCatalog() { InvalidateServerCatalog(); InvalidateInstallerCatalog(); }
    private void InvalidateServerCatalog()
    {
        if (!initialized) return;
        catalogGeneration++; catalogRead?.Cancel(); catalogSnapshot = null; catalogFailure = null;
        CatalogStatusText.Text = dirty || externalChange ? "Finish configuring your source in Settings." : "Available releases have not been checked.";
        RebuildInventory(); UpdateInterface();
    }
    private void CancelCatalogClicked(object sender, RoutedEventArgs e) => catalogRead?.Cancel();
    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateInterface(); }
    private async Task<bool> SavedSettingsMatchAsync(SettingsSnapshot captured, bool sourcesOnly = false)
    {
        var loaded = await Task.Run(() => store.Load(paths));
        return loaded is Outcome<SettingsSnapshot>.Success success &&
            (sourcesOnly ? SameSources(success.Value.Settings, captured.Settings) : success.Value == captured);
    }
    private async void RefreshCatalogClicked(object sender, RoutedEventArgs e)
    {
        if (busy || reviewing || dirty || externalChange || IsSavingSettings || !HasSource || catalogRead is not null || snapshot?.Settings is null) return;
        await RefreshInventoryAsync();
        await RefreshServerCatalogAsync();
    }
    private Task EnsureServerCatalogAsync() => startupComplete && Navigation.SelectedItem == InstallationsNavigation &&
        catalogSnapshot is null && catalogFailure is null ? RefreshServerCatalogAsync() : Task.CompletedTask;

    private async Task RefreshServerCatalogAsync()
    {
        if (busy || reviewing || dirty || externalChange || IsSavingSettings || !HasSource || catalogClosed || catalogRead is not null || snapshot?.Settings is null) return;
        var captured = snapshot;
        InvalidateServerCatalog(); var generation = catalogGeneration;
        using var cancellation = new CancellationTokenSource(); catalogRead = cancellation;
        CatalogStatusText.Text = "Loading available servers…"; RebuildInventory(); UpdateInterface();
        try
        {
            if (!await SavedSettingsMatchAsync(captured, sourcesOnly: true))
            { if (generation == catalogGeneration) CatalogStatusText.Text = catalogFailure = "Settings changed on disk. Reload Settings before checking."; return; }
            if (generation != catalogGeneration || catalogClosed) return;
            var result = await catalogClient.ReadAsync(captured.Settings!, CatalogComponent.Server, cancellation.Token);
            if (generation != catalogGeneration || catalogClosed) return;
            if (!await SavedSettingsMatchAsync(captured, sourcesOnly: true))
            { if (generation == catalogGeneration) CatalogStatusText.Text = catalogFailure = "Settings changed during the check. Reload Settings and check again."; return; }
            if (generation != catalogGeneration || catalogClosed) return;
            if (cancellation.IsCancellationRequested)
            { CatalogStatusText.Text = catalogFailure = "Source check cancelled. Installed versions are still available."; RecordActivity("Catalog.Read", "Cancelled"); return; }
            if (result is CatalogResult<CatalogSnapshot>.Failure failure)
            {
                CatalogStatusText.Text = catalogFailure = failure.Error.Message;
                ServerTestText.Text = "Source check failed: " + failure.Error.Message;
                RecordActivity("Catalog.Read", failure.Error.Code.ToString()); return;
            }
            catalogSnapshot = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            CatalogStatusText.Text = CatalogSummary(catalogSnapshot);
            ServerTestText.Text = TestSummary(catalogSnapshot); testedServerSettings = captured.Settings; testedServerCatalog = catalogSnapshot;
            RecordActivity("Catalog.Read", "Succeeded");
        }
        finally
        {
            if (ReferenceEquals(catalogRead, cancellation)) catalogRead = null;
            if (!catalogClosed)
            {
                RebuildInventory(); UpdateInterface();
                // A newly saved source may have been waiting for this cancelled read to finish.
                if (generation != catalogGeneration) await EnsureServerCatalogAsync();
            }
        }
    }
    private static string CatalogSummary(CatalogSnapshot value) => $"Checked {DateTime.Now:t} · {value.Packages.Count} server releases · " +
        (value.Publisher is null ? "Publisher not configured" : "Publisher verified");

    private static int CompareVersions(string? first, string? second) =>
        first is not null && second is not null && ReleaseVersion.IsValid(first) && ReleaseVersion.IsValid(second)
            ? ReleaseVersion.Compare(first, second) : StringComparer.Ordinal.Compare(first, second);

    private void RebuildInventory(string? selectId = null)
    {
        if (!initialized || rebuildingInventory) return;
        rebuildingInventory = true;
        try
        {
            selectId ??= (CatalogPackages.SelectedItem as ServerRow)?.Id;
            var local = installedPackages.Where(p => p.Receipt.Package.Component == CatalogComponent.Server).ToDictionary(p => p.Id, StringComparer.Ordinal);
            var available = (catalogSnapshot?.Packages ?? []).Where(p => p.Component == CatalogComponent.Server && p.RuntimeIdentifier == "win-x64").ToDictionary(p => p.Id, StringComparer.Ordinal);
            var latest = available.Values.GroupBy(p => p.TargetDisplay).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version, Comparer<string>.Create(CompareVersions)).First().Version);
            serverRows = local.Keys.Union(available.Keys).Select(id =>
            {
                var package = available.GetValueOrDefault(id) ?? local[id].Receipt.Package;
                return new ServerRow(package, local.GetValueOrDefault(id), available.ContainsKey(id),
                    latest.TryGetValue(package.TargetDisplay, out var version) && CompareVersions(package.Version, version) == 0);
            }).OrderByDescending(p => p.Target, StringComparer.Ordinal).ThenByDescending(p => p.Version, Comparer<string>.Create(CompareVersions)).ToList();
            var target = TargetFilter.SelectedItem as string;
            var targets = new[] { "All SA releases" }.Concat(serverRows.Select(p => p.Target).Distinct()).ToArray();
            TargetFilter.ItemsSource = targets; TargetFilter.SelectedItem = targets.Contains(target) ? target : targets[0];
            ApplyInventoryFilter(selectId);
        }
        finally { rebuildingInventory = false; }
    }
    private void InventoryFilterChanged(object sender, RoutedEventArgs e)
    { if (initialized && !rebuildingInventory) ApplyInventoryFilter((CatalogPackages.SelectedItem as ServerRow)?.Id); }
    private void ApplyInventoryFilter(string? selectId = null)
    {
        var search = ServerSearch.Text.Trim();
        var target = TargetFilter.SelectedItem as string;
        var filtered = serverRows.Where(r =>
            (string.IsNullOrEmpty(search) || r.Version.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Target.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (target is null or "All SA releases" || r.Target == target) &&
            (InventoryFilter.SelectedIndex != 1 || r.Installed is not null) &&
            (InventoryFilter.SelectedIndex != 2 || r.Available && r.Installed is null)).ToList();
        var view = new ListCollectionView(filtered);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServerRow.Target)));
        CatalogPackages.ItemsSource = view;
        CatalogPackages.SelectedItem = filtered.FirstOrDefault(r => r.Id == selectId);
        InventoryEmpty.Visibility = Show(filtered.Count == 0);
        InventoryFilters.Visibility = Show(serverRows.Count > 0);
        EmptyActionButton.Visibility = Visibility.Visible;
        if (!HasSource || dirty || externalChange)
        {
            EmptyTitle.Text = !HasSource ? "Get your first Briosa server" : "Your source settings need attention";
            EmptyDescription.Text = "Choose a package source in Settings. You can use your enterprise mirror or an offline catalog.";
            EmptyActionButton.Content = "Configure package source";
            emptyAction = EmptyAction.ConfigureSource;
        }
        else if (catalogRead is not null || busy && serverRows.Count == 0)
        {
            EmptyTitle.Text = "Reading package information"; EmptyDescription.Text = "Your installed versions will appear here.";
            EmptyActionButton.Visibility = Visibility.Collapsed;
        }
        else if (inventoryFailure is not null || catalogFailure is not null)
        {
            EmptyTitle.Text = "Package information could not be read";
            EmptyDescription.Text = inventoryFailure ?? catalogFailure;
            EmptyActionButton.Content = inventoryFailure is not null ? "Refresh local inventory" : "Try again";
            emptyAction = inventoryFailure is not null ? EmptyAction.RefreshInventory : EmptyAction.CheckSource;
        }
        else if (serverRows.Count > 0)
        {
            EmptyTitle.Text = "No matching servers"; EmptyDescription.Text = "Try another SA release, search, or status filter."; EmptyActionButton.Content = "Clear filters";
            emptyAction = EmptyAction.ClearFilters;
        }
        else
        {
            EmptyTitle.Text = catalogSnapshot is null ? "No servers installed here" : "No server releases in this source";
            EmptyDescription.Text = catalogSnapshot is null ? "Refresh to load releases from your saved package source." : "Choose another package source, or refresh after your mirror is updated.";
            EmptyActionButton.Content = catalogSnapshot is null ? "Refresh" : "Change package source";
            emptyAction = catalogSnapshot is null ? EmptyAction.CheckSource : EmptyAction.ConfigureSource;
        }
        UpdateInterface();
    }
    private async void EmptyActionClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        switch (emptyAction)
        {
            case EmptyAction.ConfigureSource: ConfigureSourceClicked(sender, e); break;
            case EmptyAction.RefreshInventory: await RefreshInventoryAsync(); break;
            case EmptyAction.ClearFilters:
                ServerSearch.Clear(); TargetFilter.SelectedIndex = 0; InventoryFilter.SelectedIndex = 0; ApplyInventoryFilter(); break;
            case EmptyAction.CheckSource: RefreshCatalogClicked(sender, e); break;
        }
    }
    private void ServerDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (CatalogPackages.SelectedItem is not ServerRow row) return;
        var text = $"Briosa server {row.Version}\nSpatialAnalyzer {row.Target}\n{row.Status}\n";
        if (row.Installed is { } local) text += InstalledDetails(local);
        if (row.Available && catalogSnapshot is { } catalog)
        {
            var result = ReleaseCatalogCodec.Preview(catalog, row.Id);
            if (result is CatalogResult<PackagePreview>.Success success)
            {
                var preview = success.Value;
                text += $"\nSource: {preview.CatalogSource}\nDownload: {row.Package.SizeDisplay}\nPlatform: {row.Package.RuntimeIdentifier}\n" +
                    $"Publisher: {(preview.Publisher is null ? "Not verified — configure an approved publisher in Settings" : "Verified")}\n" +
                    $"Publisher fingerprint: {preview.Publisher?.Fingerprint}\nCatalog SHA-256: {preview.CatalogSha256}\n" +
                    $"Payload: {preview.ArtifactLocation}\nPayload SHA-256: {row.Package.Artifact.Sha256}\nProvenance: {preview.ProvenanceLocation ?? "Not declared"}\n" +
                    "\nPayload checks run during installation. Package installation does not establish SA runtime readiness.";
            }
        }
        new DetailsDialog("Server details", text) { Owner = this }.ShowDialog();
    }
    private static string InstalledDetails(InstalledPackage package) =>
        $"\nInstalled: {package.Receipt.InstalledAt.ToLocalTime():g}\nLocation: {package.Directory}\n" +
        $"Publisher fingerprint: {package.Receipt.Publisher.Fingerprint}\nPackage SHA-256: {package.Receipt.Package.Artifact.Sha256}\n";
    protected override void OnClosed(EventArgs e)
    {
        catalogClosed = true; catalogGeneration++; updaterGeneration++; sourceTestGeneration++;
        catalogRead?.Cancel(); updaterRead?.Cancel(); sourceTest?.Cancel();
        foreach (var status in liveStatuses) status.Dispose();
        if (ownsCatalogClient) catalogClient.Dispose();
        base.OnClosed(e);
    }
}
