using System.IO;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private SourceSettings? serverSecurity, installerSecurity;
    private CancellationTokenSource? sourceTest;
    private int sourceTestGeneration;
    private InstallerSettings? testedServerSettings;
    private CatalogSnapshot? testedServerCatalog;

    private void UpdateSecurityLabels()
    {
        static string Label(SourceSettings source) => $"Access: {SourceSecurityDialog.AuthenticationLabel(source.Authentication)} · " +
            (source.PublisherKey is null ? "Publisher not configured" : "Approved publisher configured");
        ServerSecurityText.Text = Label(CurrentSettings().Source(CatalogComponent.Server));
        InstallerSecurityText.Text = Label(CurrentSettings().Source(CatalogComponent.Installer));
    }
    private void ServerSecurityClicked(object sender, RoutedEventArgs e) => EditSecurity(CatalogComponent.Server);
    private void InstallerSecurityClicked(object sender, RoutedEventArgs e) => EditSecurity(CatalogComponent.Installer);
    private void EditSecurity(CatalogComponent component)
    {
        if (busy || sourceTest is not null) return;
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure invalid)
        { StatusText.Text = invalid.Error.Message; return; }
        var dialog = new SourceSecurityDialog(settings.Source(component), credentials) { Owner = this };
        var applied = dialog.ShowDialog() == true;
        if (applied)
        {
            if (component == CatalogComponent.Server || SameSource.IsChecked == true) serverSecurity = dialog.Result;
            else installerSecurity = dialog.Result;
            dirty = CurrentSettings() != editorBaseline;
        }
        if (applied || dialog.CredentialChanged)
        {
            InvalidateCatalog(); InvalidateSourceTests(); UpdateSecurityLabels(); UpdateInterface();
            StatusText.Text = (dialog.CredentialChanged ? "Credential changes were saved separately. " : "") +
                (dirty ? "Save source settings to apply the access method and publisher selection." : "Source settings are unchanged.");
            if (dialog.CredentialChanged) RecordActivity(dialog.CredentialRemoved ? "Credentials.Remove" : "Credentials.Save", "Succeeded");
        }
    }
    private void BrowseSourceClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Title = "Choose a package catalog", Filter = "Package catalog (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
            (((Button)sender).Tag?.ToString() == "Installer" ? InstallerCatalog : ServerCatalog).Text = dialog.FileName;
    }
    private void InvalidateSourceTests()
    {
        sourceTestGeneration++; sourceTest?.Cancel(); testedServerSettings = null; testedServerCatalog = null;
        ServerTestText.Text = InstallerTestText.Text = "Not tested. Testing reads metadata only.";
    }
    private static string TestSummary(CatalogSnapshot catalog) => $"Catalog accessible · checked {DateTime.Now:t}. " +
        (catalog.Publisher is null ? "Publisher not configured; browsing is available, installation is blocked." :
        "Publisher signature verified. Payload integrity is checked when installing.");
    private async void TestSourceClicked(object sender, RoutedEventArgs e)
    {
        if (busy || sourceTest is not null) return;
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure invalid)
        { StatusText.Text = invalid.Error.Message; return; }
        var component = ((Button)sender).Tag?.ToString() == "Installer" ? CatalogComponent.Installer : CatalogComponent.Server;
        var target = component == CatalogComponent.Server ? ServerTestText : InstallerTestText;
        var code = component == CatalogComponent.Server ? "Settings.Test" : "Settings.TestUpdater";
        var generation = ++sourceTestGeneration;
        using var cancellation = new CancellationTokenSource(); sourceTest = cancellation;
        target.Text = "Testing this catalog and its publisher…"; UpdateInterface();
        try
        {
            var result = await catalogClient.ReadAsync(settings, component, cancellation.Token);
            if (generation != sourceTestGeneration || catalogClosed || settings != CurrentSettings()) return;
            if (cancellation.IsCancellationRequested)
            { target.Text = "Connection test cancelled. Settings can still be saved."; RecordActivity(code, "Cancelled"); return; }
            if (result is CatalogResult<CatalogSnapshot>.Failure failure)
            { target.Text = failure.Error.Message; RecordActivity(code, failure.Error.Code.ToString()); return; }
            var catalog = ((CatalogResult<CatalogSnapshot>.Success)result).Value;
            target.Text = TestSummary(catalog);
            if (component == CatalogComponent.Server) { testedServerSettings = settings; testedServerCatalog = catalog; }
            RecordActivity(code, "Succeeded");
        }
        finally
        {
            if (ReferenceEquals(sourceTest, cancellation)) sourceTest = null;
            if (!catalogClosed) UpdateInterface();
        }
    }
    private void CancelSourceTestClicked(object sender, RoutedEventArgs e) => sourceTest?.Cancel();

    private void JsonClicked(object sender, RoutedEventArgs e)
    {
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure failure)
        { StatusText.Text = failure.Error.Message; return; }
        new DetailsDialog("Settings JSON — editor values", SettingsCodec.Serialize(settings)) { Owner = this }.ShowDialog();
    }
    private void ImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Installer settings (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var imported = store.Load(new(dialog.FileName, ExplicitFile: dialog.FileName));
        if (imported is not Outcome<SettingsSnapshot>.Success { Value.Settings: { } settings })
        { StatusText.Text = "This file could not be imported. Check its format and access."; RecordActivity("Settings.Import", "Failed"); return; }
        var baseline = editorBaseline;
        PopulateEditor(settings); editorBaseline = baseline; dirty = CurrentSettings() != baseline;
        InvalidateCatalog(); InvalidateSourceTests(); UpdateInterface();
        SettingsSections.SelectedItem = SourcesSection;
        StatusText.Text = "Settings imported into the editor. Review and save to apply them. Credentials were not imported.";
    }
    private void ExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure failure)
        { StatusText.Text = failure.Error.Message; return; }
        var dialog = new SaveFileDialog { Filter = "Installer settings (*.json)|*.json", FileName = "briosa-settings.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, SettingsCodec.Serialize(settings)); StatusText.Text = "Editor settings exported. No token or password was included."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = "Settings could not be exported. Check file access."; }
    }
}

