using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public sealed record InstallerRelease(CatalogPackage Package, int? Comparison)
{
    public string Version => Package.Version;
    public string SizeDisplay => Package.SizeDisplay;
    public string Relationship => Comparison switch { > 0 => "Newer version", < 0 => "Older version — rollback", 0 => "Running version", _ => "Running version unknown" };
    public string AccessibleName => $"Briosa Installer {Version}, {Relationship}";
}
public partial class MainWindow
{
    private readonly string currentInstallerVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?.InformationalVersion.Split('+')[0] ?? "development";
    private CancellationTokenSource? updaterRead;
    private CatalogSnapshot? updaterSnapshot;
    private InstallerRelease? recommendedUpdate;
    private string? pendingInstallerVersion;
    private int updaterGeneration;

    private void InvalidateInstallerCatalog()
    {
        if (!initialized) return;
        updaterGeneration++; updaterRead?.Cancel(); updaterSnapshot = null; recommendedUpdate = null;
        InstallerReleases.ItemsSource = null;
        UpdateStatusText.Text = dirty || externalChange ? "Save or reload source settings before checking for updates." : "Check your saved source for a newer version.";
        UpdateCheckedText.Text = ""; UpdateInterface();
    }
    private void CancelUpdateCheckClicked(object sender, RoutedEventArgs e) => updaterRead?.Cancel();
    private void InstallerReleaseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        UseReleaseButton.Content = InstallerReleases.SelectedItem is InstallerRelease { Comparison: < 0 } ? "Review rollback…" : "Review selected version…";
        UpdateInterface();
    }
    private async void CheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || externalChange || updaterRead is not null || snapshot?.Settings is null) return;
        var captured = snapshot;
        InvalidateInstallerCatalog(); var generation = updaterGeneration;
        using var cancellation = new CancellationTokenSource(); updaterRead = cancellation;
        UpdateStatusText.Text = "Checking the saved installer update source…"; UpdateInterface();
        try
        {
            if (!await SavedSettingsMatchAsync(captured, sourcesOnly: true))
            { if (generation == updaterGeneration) UpdateStatusText.Text = "Source settings changed. Reload Settings before checking."; return; }
            if (generation != updaterGeneration || catalogClosed) return;
            var result = await catalogClient.ReadAsync(captured.Settings!, CatalogComponent.Installer, cancellation.Token);
            if (generation != updaterGeneration || catalogClosed) return;
            if (!await SavedSettingsMatchAsync(captured, sourcesOnly: true))
            { if (generation == updaterGeneration) UpdateStatusText.Text = "Source settings changed during this check. Reload and check again."; return; }
            if (generation != updaterGeneration || catalogClosed) return;
            if (cancellation.IsCancellationRequested)
            { UpdateStatusText.Text = "Update check cancelled."; RecordActivity("Installer.Check", "Cancelled"); return; }
            if (result is CatalogResult<CatalogSnapshot>.Failure failure)
            { UpdateStatusText.Text = failure.Error.Message; RecordActivity("Installer.Check", failure.Error.Code.ToString()); return; }
            updaterSnapshot = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            var releases = updaterSnapshot.Packages.Where(p => p.Component == CatalogComponent.Installer && p.RuntimeIdentifier == "win-x64")
                .OrderByDescending(p => p.Version, Comparer<string>.Create(CompareVersions))
                .Select(p => new InstallerRelease(p, ReleaseVersion.IsValid(currentInstallerVersion) ? ReleaseVersion.Compare(p.Version, currentInstallerVersion) : null)).ToArray();
            InstallerReleases.ItemsSource = releases;
            InstallerReleases.SelectedIndex = -1; // Recovery is always a deliberate choice.
            recommendedUpdate = releases.FirstOrDefault(p => p.Comparison > 0);
            UpdateStatusText.Text = recommendedUpdate is not null ? $"Version {recommendedUpdate.Version} is available · {recommendedUpdate.SizeDisplay}" :
                releases.Length == 0 ? "No Windows x64 installer releases are listed in this source." :
                ReleaseVersion.IsValid(currentInstallerVersion) ? "No newer installer version is available from this source." :
                "The running build cannot be compared. Review a specific release under Previous versions.";
            UpdateCheckedText.Text = $"Checked {DateTime.Now:g} · " + (updaterSnapshot.Publisher is null ? "Configure an approved publisher to enable downloads." : "Publisher signature verified");
            InstallerTestText.Text = TestSummary(updaterSnapshot);
            if (captured.Settings!.InstallerCatalog is null) ServerTestText.Text = TestSummary(updaterSnapshot);
            RecordActivity("Installer.Check", "Succeeded");
        }
        finally
        {
            if (ReferenceEquals(updaterRead, cancellation)) updaterRead = null;
            if (!catalogClosed) UpdateInterface();
        }
    }
    private async void InstallUpdateClicked(object sender, RoutedEventArgs e)
    { if (recommendedUpdate is { Comparison: > 0 } release) await AcquireInstallerAsync(release); }
    private async void UseReleaseClicked(object sender, RoutedEventArgs e)
    { if (InstallerReleases.SelectedItem is InstallerRelease release) await AcquireInstallerAsync(release); }

    private async Task AcquireInstallerAsync(InstallerRelease release)
    {
        if (busy || dirty || externalChange || updaterRead is not null || updaterSnapshot?.Publisher is null || snapshot?.Settings is null) return;
        reviewing = true; UpdateInterface();
        try
        {
            var captured = snapshot; var catalog = updaterSnapshot; var generation = updaterGeneration;
            if (!catalog.Packages.Contains(release.Package)) return;
            if (!await SavedSettingsMatchAsync(captured, sourcesOnly: true))
            { InvalidateCatalog(); UpdateOperationStatusText.Text = "Source settings changed. Reload Settings and check again."; return; }
            if (busy || dirty || generation != updaterGeneration || catalogClosed) return;
            var package = release.Package; var rollback = release.Comparison < 0;
            if (!ConfirmAction(rollback ? "Return to previous installer" : "Prepare installer update",
                "Download and verify this installer, then select it for the next launch. Settings and installed servers are preserved. You can restart when ready.",
                rollback ? "Prepare rollback" : "Download and prepare",
                [new("Running version", currentInstallerVersion), new("Selected version", package.Version + " · " + release.Relationship),
             new("Source", catalog.Source), new("Destination", packageStore.Root), new("Download", package.SizeDisplay)],
                "Publisher verified: " + catalog.Publisher.Fingerprint + "\nPayload integrity is checked before selection.")) return;
            var progress = PackageProgress(true);
            if (await RunOperationAsync("Installer.Update", async token =>
            {
                var existing = packageStore.List().SingleOrDefault(p => p.Id == package.Id);
                if (existing is null)
                    await packageStore.InstallAsync(captured.Settings!, CatalogComponent.Installer, package.Id, catalog.ContentSha256,
                        progress: progress, token: token, configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured);
                else if (existing.Receipt.Package != package || existing.Receipt.Publisher.Fingerprint != catalog.Publisher.Fingerprint)
                    throw new ManagementException(ManagementError.IntegrityFailure);
                if (store.Load(paths) is not Outcome<SettingsSnapshot>.Success current || current.Value != captured) throw new ManagementException(ManagementError.InvalidInput);
                await packageStore.ActivateInstallerAsync(package.Id, token);
                if (bootstrapPath is not null) await BootstrapUpdater.RefreshAsync(packageStore, package.Id, bootstrapPath, token);
            }, true, package)) PrepareRestart(package.Version);
        }
        finally { reviewing = false; UpdateInterface(); }
    }
}
