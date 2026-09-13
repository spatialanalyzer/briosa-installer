using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public sealed record InstallerRelease(CatalogPackage Package, int? Comparison)
{
    public string Version => Package.Version;
    public string SizeDisplay => Package.SizeDisplay;
    public string Relationship => Comparison switch
    {
        > 0 => "Newer version",
        < 0 => "Older version (rollback)",
        0 => "Same version",
        _ => "Running version unknown",
    };
}

public partial class MainWindow
{
    private readonly string currentInstallerVersion = System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?
        .InformationalVersion.Split('+')[0] ?? "development";
    private CancellationTokenSource? updaterRead;
    private CatalogSnapshot? updaterSnapshot;
    private int updaterGeneration;

    private void InvalidateInstallerCatalog()
    {
        if (InstallerReleases is null) return;
        updaterGeneration++;
        updaterRead?.Cancel();
        updaterSnapshot = null;
        InstallerReleases.ItemsSource = null;
        UpdateStatusText.Text = dirty ? "Save or reload settings before checking for updates." : "Check for updates using your saved source settings.";
        UpdateManagementControls();
    }

    private void CancelUpdateCheckClicked(object sender, RoutedEventArgs e) => updaterRead?.Cancel();
    private void InstallerReleaseSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateManagementControls();

    private async void CheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || updaterRead is not null || snapshot?.Settings is null) return;
        var captured = snapshot;
        InvalidateInstallerCatalog();
        var generation = updaterGeneration;
        using var cancellation = new CancellationTokenSource();
        updaterRead = cancellation;
        UpdateManagementControls();
        UpdateStatusText.Text = "Checking the configured installer update source…";
        try
        {
            if (!await SavedSettingsMatchAsync(captured))
            {
                if (generation == updaterGeneration) UpdateStatusText.Text = "Source settings changed or cannot be read. Reload Settings before checking.";
                return;
            }
            if (generation != updaterGeneration || catalogClosed) return;
            var result = await catalogClient.ReadAsync(captured.Settings!, CatalogComponent.Installer, cancellation.Token);
            if (generation != updaterGeneration || catalogClosed) return;
            if (!await SavedSettingsMatchAsync(captured))
            {
                if (generation == updaterGeneration) UpdateStatusText.Text = "Source settings changed while checking. Reload Settings and check again.";
                return;
            }
            if (generation != updaterGeneration || catalogClosed) return;
            if (cancellation.IsCancellationRequested)
            {
                UpdateStatusText.Text = "The update check was cancelled.";
                return;
            }
            if (result is CatalogResult<CatalogSnapshot>.Failure failure)
            {
                UpdateStatusText.Text = failure.Error.Message;
                AddActivity($"Installer update check: {failure.Error.Code}.");
                return;
            }
            updaterSnapshot = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            var releases = updaterSnapshot.Packages.Where(p => p.RuntimeIdentifier == "win-x64")
                .OrderByDescending(p => p.Version, Comparer<string>.Create(ReleaseVersion.Compare))
                .Select(p => new InstallerRelease(p, ReleaseVersion.IsValid(currentInstallerVersion)
                    ? ReleaseVersion.Compare(p.Version, currentInstallerVersion) : null)).ToArray();
            InstallerReleases.ItemsSource = releases;
            InstallerReleases.SelectedIndex = releases.Length == 0 ? -1 : 0;
            var newer = releases.Count(p => p.Comparison > 0);
            UpdateStatusText.Text = (releases.Length == 0 ? "No Windows x64 installer releases are listed in this source." :
                newer > 0 ? $"{newer} newer installer version{(newer == 1 ? "" : "s")} available." :
                ReleaseVersion.IsValid(currentInstallerVersion) ? "No newer installer version is listed in this source." :
                "Installer releases found. The running build's version cannot be compared.") +
                " " + (updaterSnapshot.Publisher is null
                    ? "Configure an approved publisher key for the update source to enable installation."
                    : "Publisher signature verified.") + "\nSource: " + updaterSnapshot.Source;
            AddActivity("Installer update source checked.");
        }
        finally
        {
            if (ReferenceEquals(updaterRead, cancellation)) updaterRead = null;
            if (!catalogClosed) UpdateManagementControls();
        }
    }

    private async void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || updaterRead is not null || updaterSnapshot?.Publisher is null ||
            snapshot?.Settings is null || InstallerReleases.SelectedItem is not InstallerRelease release) return;
        var captured = snapshot;
        var catalog = updaterSnapshot;
        var generation = updaterGeneration;
        if (!await SavedSettingsMatchAsync(captured))
        {
            InvalidateCatalog();
            UpdateOperationStatusText.Text = "Source settings changed. Reload Settings and check again.";
            return;
        }
        if (busy || dirty || generation != updaterGeneration || catalogClosed ||
            !ReferenceEquals(InstallerReleases.SelectedItem, release)) return;
        var package = release.Package;
        if (!ConfirmAction("Review installer update",
            $"Use Briosa Installer {package.Version}?\n\nRunning version: {currentInstallerVersion}\n{release.Relationship}\nSource: {catalog.Source}\nDestination: {packageStore.Root}\nDownload: {package.SizeDisplay}\nPublisher: {catalog.Publisher.Fingerprint}\n\nThe verified version will be selected for the next launch. Settings and installed servers will be preserved. You can restart when ready.")) return;
        var progress = PackageProgress(installer: true);
        if (await RunOperationAsync("Installer.Update", async token =>
        {
            var existing = packageStore.List().SingleOrDefault(p => p.Id == package.Id);
            if (existing is null)
                await packageStore.InstallAsync(captured.Settings!, CatalogComponent.Installer, package.Id, catalog.ContentSha256,
                    progress: progress, token: token,
                    configurationStillMatches: () => store.Load(paths) is Outcome<SettingsSnapshot>.Success saved && saved.Value == captured);
            else if (existing.Receipt.Package != package || existing.Receipt.Publisher.Fingerprint != catalog.Publisher.Fingerprint)
                throw new ManagementException(ManagementError.IntegrityFailure);
            if (store.Load(paths) is not Outcome<SettingsSnapshot>.Success saved || saved.Value != captured)
                throw new ManagementException(ManagementError.InvalidInput);
            await packageStore.ActivateInstallerAsync(package.Id, token);
            if (bootstrapPath is not null) await BootstrapUpdater.RefreshAsync(packageStore, package.Id, bootstrapPath, token);
        }, installer: true))
            await OfferInstallerRestartAsync(package.Version);
    }
}
