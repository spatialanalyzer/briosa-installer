using System.IO;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private SdkReport? sdkReport;
    private IReadOnlyList<ActivityView> activityRows = [];
    private void RecordActivity(string operationCode, string outcome, CatalogPackage? package = null, long? duration = null)
    {
        var saved = activity.Record(operationCode, outcome, package, duration);
        RefreshActivity();
        if (!saved) ActivityStatusText.Text = "The operation result could not be added to saved history. Check access to the settings directory.";
    }
    private void RefreshActivity()
    {
        activityRows = activity.Read().Select(e => new ActivityView(e)).ToArray();
        ActivityStatusText.Text = activity.ReadFailed ? "Saved history could not be read. The history file has not been overwritten." : "";
        ApplyActivityFilter();
    }
    private void ApplyActivityFilter()
    {
        var rows = activityRows.Where(r => ActivityFilter.SelectedIndex != 1 || r.Entry.Outcome != "Succeeded").ToArray();
        ActivityList.ItemsSource = rows; ActivityEmptyText.Visibility = Show(rows.Length == 0);
        ActivityEmptyText.Text = ActivityFilter.SelectedIndex == 1 ? "No activity needs attention." : "No activity yet.";
        ActivityDetailsButton.IsEnabled = false;
    }
    private void RefreshActivityClicked(object sender, RoutedEventArgs e) => RefreshActivity();
    private void ActivityFilterChanged(object sender, SelectionChangedEventArgs e) { if (initialized) ApplyActivityFilter(); }
    private void ActivitySelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (initialized) ActivityDetailsButton.IsEnabled = ActivityList.SelectedItem is ActivityView; }
    private void ActivityDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (ActivityList.SelectedItem is not ActivityView row) return;
        var entry = row.Entry;
        var guidance = entry.Outcome switch
        {
            "Succeeded" => "The operation completed.",
            "Cancelled" => "The operation was cancelled. Review the current package inventory before choosing another action.",
            _ when Enum.TryParse<ManagementError>(entry.Outcome, out var management) => ManagementException.MessageFor(management),
            _ when Enum.TryParse<CatalogError>(entry.Outcome, out var catalog) => new CatalogFailure(catalog).Message,
            _ when Enum.TryParse<ConfigurationError>(entry.Outcome, out var configuration) => new ConfigurationFailure(configuration).Message,
            _ => "Review the relevant package or source in Settings. Export a support report if the issue continues.",
        };
        new DetailsDialog("Activity details", $"{row.Title}\n{row.Context}\n{row.Outcome}\n\n{guidance}\n\nLocal time: {row.Time}\nUTC: {entry.Time:u}\n" +
            $"Duration: {(entry.DurationMs is null ? "Not recorded" : entry.DurationMs + " ms")}\nOperation code: {entry.Operation}\nResult code: {entry.Outcome}")
        { Owner = this }.ShowDialog();
    }
    private async void InspectSdkClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true); SdkSummaryText.Text = "Inspecting installed products and SDK registration…";
        SdkNextStepText.Text = ""; SdkNextStepText.Visibility = Visibility.Collapsed;
        try
        {
            sdkReport = await Task.Run(sdkDiscovery.Inspect);
            var overview = SdkSetupPresentation.FromReport(sdkReport);
            SdkSummaryTitle.Text = overview.Title;
            SdkSummaryText.Text = overview.Summary;
            SdkNextStepText.Text = overview.Findings;
            SdkNextStepText.Visibility = Show(overview.Findings.Length > 0);
            SdkObservations.ItemsSource = overview.Installations;
            SdkObservations.SelectedItem = overview.Installations.FirstOrDefault(i => i.IsRegistered);
            SdkObservations.Visibility = Show(overview.Installations.Count > 0);
            RecordActivity("Sdk.Inspect", "Succeeded");
        }
        catch (Exception exception) when (exception is ManagementException or IOException or UnauthorizedAccessException)
        {
            sdkReport = null; SdkObservations.ItemsSource = null; SdkObservations.Visibility = Visibility.Collapsed;
            SdkSummaryTitle.Text = "Inspection could not complete";
            SdkSummaryText.Text = "Check access to Windows installation information, then inspect again.";
            RecordActivity("Sdk.Inspect", "Failed");
        }
        finally { SetBusy(false); }
    }
    private void SdkDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: SaInstallationRow row } || sdkReport is null) return;
        var registration = row.IsRegistered ? "This installation's SDK path is registered." :
            "This installation's SDK path is not referenced by the inspected registration.";
        var evidence = sdkReport.Observations.Where(o => o.Kind is SdkReport.ConfiguredRegistration or "Other SDK registration" or "Discovery incomplete")
            .Select(o => $"{o.Kind}\nVersion: {o.Version}\nRegistry context: {o.Context}\nPath: {o.Location}\n{o.Status}").ToArray();
        var workstation = evidence.Length > 0 ? string.Join("\n\n", evidence) : "No SDK registration was found for the inspecting user.";
        new DetailsDialog($"SpatialAnalyzer {row.Version}",
            $"Installation path: {row.Location}\nInstallation evidence: {row.Installation.Context}\n\n{registration}\n\n" +
            $"SDK registration on this workstation\n\n{workstation}\n\n{sdkReport.Guidance}")
        { Owner = this }.ShowDialog();
    }
    private void ExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Filter = "Support report (*.json)|*.json", FileName = "briosa-support.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { activity.Export(dialog.FileName, sdkReport); RecordActivity("Support.Export", "Succeeded"); ShowExportResult("Sanitized support report exported."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { RecordActivity("Support.Export", "Failed"); ShowExportResult("Support report could not be saved. Check file access."); }
    }
    private void ShowExportResult(string message)
    {
        ActivityStatusText.Text = message;
    }
}
