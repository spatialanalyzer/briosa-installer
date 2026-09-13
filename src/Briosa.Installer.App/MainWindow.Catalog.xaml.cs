using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private readonly ReleaseCatalogClient catalogClient;
    private readonly bool ownsCatalogClient;
    private CancellationTokenSource? catalogRead;
    private CatalogSnapshot? catalogSnapshot;
    private int catalogGeneration;
    private bool catalogClosed;

    private void InvalidateCatalog()
    {
        InvalidateServerCatalog();
        InvalidateInstallerCatalog();
    }

    private void InvalidateServerCatalog()
    {
        if (CatalogPackages is null) return;
        catalogGeneration++;
        catalogRead?.Cancel();
        catalogSnapshot = null;
        CatalogPackages.ItemsSource = null;
        PackagePreviewText.Text = "";
        CatalogStatusText.Text = dirty ? "Save or reload source settings before refreshing." : "Refresh to read the selected catalog.";
        UpdateCatalogControls();
    }

    private void CancelCatalogClicked(object sender, RoutedEventArgs e) => catalogRead?.Cancel();
    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackagePreviewText is null) return;
        PackagePreviewText.Text = "";
        UpdateCatalogControls();
    }

    private void UpdateCatalogControls()
    {
        if (RefreshCatalogButton is null) return;
        RefreshCatalogButton.IsEnabled = !busy && !dirty && catalogRead is null && snapshot?.Settings is not null;
        CancelCatalogButton.IsEnabled = catalogRead is not null;
        PreviewPackageButton.IsEnabled = !busy && !dirty && catalogRead is null && catalogSnapshot is not null && CatalogPackages.SelectedItem is CatalogPackage;
        UpdateManagementControls();
    }

    private async Task<bool> SavedSettingsMatchAsync(SettingsSnapshot captured)
    {
        var loaded = await Task.Run(() => store.Load(paths));
        return loaded is Outcome<SettingsSnapshot>.Success success && success.Value == captured;
    }

    private async void RefreshCatalogClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || catalogRead is not null || snapshot?.Settings is null) return;
        var captured = snapshot;
        InvalidateServerCatalog();
        var generation = catalogGeneration;
        using var cancellation = new CancellationTokenSource();
        catalogRead = cancellation;
        UpdateCatalogControls();
        CatalogStatusText.Text = "Reading the selected catalog…";
        try
        {
            if (!await SavedSettingsMatchAsync(captured))
            {
                if (generation == catalogGeneration) CatalogStatusText.Text = "Source settings changed or cannot be read. Reload Settings before refreshing.";
                return;
            }
            if (generation != catalogGeneration || catalogClosed) return;
            var result = await catalogClient.ReadAsync(captured.Settings!, CatalogComponent.Server, cancellation.Token);
            if (generation != catalogGeneration || catalogClosed) return;
            // A script may change settings while the catalog request is in flight.
            if (!await SavedSettingsMatchAsync(captured))
            {
                if (generation == catalogGeneration) CatalogStatusText.Text = "Source settings changed while reading. Reload Settings and refresh again.";
                return;
            }
            if (generation != catalogGeneration || catalogClosed) return;
            if (cancellation.IsCancellationRequested)
            {
                CatalogStatusText.Text = "The catalog read was cancelled.";
                return;
            }
            if (result is CatalogResult<CatalogSnapshot>.Failure failure)
            {
                CatalogStatusText.Text = failure.Error.Message;
                AddActivity($"Catalog refresh: {failure.Error.Code}.");
                return;
            }
            catalogSnapshot = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            CatalogPackages.ItemsSource = catalogSnapshot.Packages;
            var count = catalogSnapshot.Packages.Count;
            CatalogStatusText.Text = count == 0 ? "No gRPC server packages are listed in the selected catalog." :
                $"{count} package{(count == 1 ? "" : "s")} listed. " + (catalogSnapshot.Publisher is null ? "Import an approved publisher key in Settings to enable installation." : "Publisher signature verified.");
            AddActivity("Catalog refreshed.");
        }
        finally
        {
            if (ReferenceEquals(catalogRead, cancellation)) catalogRead = null;
            if (!catalogClosed) UpdateCatalogControls();
        }
    }

    private async void PreviewPackageClicked(object sender, RoutedEventArgs e)
    {
        if (busy || dirty || catalogRead is not null || catalogSnapshot is null || snapshot is null || CatalogPackages.SelectedItem is not CatalogPackage package) return;
        var generation = catalogGeneration;
        var captured = snapshot;
        var catalog = catalogSnapshot;
        if (!await SavedSettingsMatchAsync(captured))
        {
            if (generation == catalogGeneration)
            {
                InvalidateCatalog();
                CatalogStatusText.Text = "Source settings changed or cannot be read. Reload Settings and refresh again.";
            }
            return;
        }
        if (generation != catalogGeneration || catalogClosed || !ReferenceEquals(CatalogPackages.SelectedItem, package)) return;
        var result = ReleaseCatalogCodec.Preview(catalog, package.Id);
        if (result is CatalogResult<PackagePreview>.Failure failure)
        {
            CatalogStatusText.Text = failure.Error.Message;
            return;
        }
        var preview = ((CatalogResult<PackagePreview>.Success)result).Value;
        PackagePreviewText.Text = $"""
            {package.ComponentName} {package.Version}
            SpatialAnalyzer: {package.TargetDisplay}
            Platform: {package.RuntimeIdentifier}

            Catalog: {preview.CatalogSource}
            Catalog SHA-256: {preview.CatalogSha256}
            Package: {preview.ArtifactLocation}
            Declared bytes: {package.Artifact.Size}
            Declared package SHA-256: {package.Artifact.Sha256}
            Provenance: {preview.ProvenanceLocation ?? "Not declared"}

            Publisher verification: {(preview.Publisher is null ? "Not verified" : "Verified")}
            Publisher fingerprint: {preview.Publisher?.Fingerprint ?? "Import an approved publisher key in Settings before installation."}
            Payload hashes will be checked during installation. Previewing does not change this machine.
            """;
        AddActivity("Package preview opened.");
    }

    protected override void OnClosed(EventArgs e)
    {
        catalogClosed = true;
        catalogGeneration++;
        catalogRead?.Cancel();
        updaterGeneration++;
        updaterRead?.Cancel();
        if (ownsCatalogClient) catalogClient.Dispose();
        base.OnClosed(e);
    }
}
