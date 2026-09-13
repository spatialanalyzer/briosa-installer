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
        SdkNextStepText.Text = "";
        try
        {
            sdkReport = await Task.Run(sdkDiscovery.Inspect);
            var products = sdkReport.Observations.Where(o => o.Kind == "Installed SA product").Select(o => o.Version).Distinct().ToArray();
            var registered = sdkReport.Observations.Where(o => o.Kind == "Registered SDK candidate").ToArray();
            var incomplete = sdkReport.Observations.Any(o => o.Kind == "Discovery incomplete");
            var unknown = registered.Any(o => o.Version == "Unknown");
            SdkSummaryTitle.Text = products.Length == 0 ? "No installed SA releases identified" :
                $"{products.Length} SpatialAnalyzer release{(products.Length == 1 ? "" : "s")} found";
            SdkSummaryText.Text = registered.Length == 0 ? "No SDK registration was found in the inspected locations." :
                unknown ? "The registered SDK version could not be determined." :
                "Registered SDK file version evidence: " + string.Join(", ", registered.Select(o => o.Version).Distinct()) + ".";
            SdkSummaryText.Text += " Runtime identity and readiness have not been validated." + (incomplete ? " Some locations could not be inspected." : "");
            SdkNextStepText.Text = registered.Length == 0 || unknown || incomplete ?
                "Review the evidence below. If registration needs maintenance, export a handoff report and coordinate the vendor-supported repair procedure with IT / Hexagon." :
                "Review registration details if you are investigating a mismatch. Briosa validates the actual SDK and SA identities when it connects.";
            SdkObservations.ItemsSource = sdkReport.Observations.OrderBy(o => o.Kind == "Installed SA product" ? 0 : 1).Select(o => new SdkEvidence(o)).ToArray();
            SdkObservations.Visibility = Show(sdkReport.Observations.Count > 0);
            SdkDetailsButton.Visibility = Show(sdkReport.Observations.Count > 0);
            RecordActivity("Sdk.Inspect", "Succeeded");
        }
        catch (Exception exception) when (exception is ManagementException or IOException or UnauthorizedAccessException)
        {
            sdkReport = null; SdkObservations.ItemsSource = null; SdkObservations.Visibility = SdkDetailsButton.Visibility = Visibility.Collapsed;
            SdkSummaryTitle.Text = "Inspection could not complete";
            SdkSummaryText.Text = "Check access to Windows installation information, then inspect again.";
            RecordActivity("Sdk.Inspect", "Failed");
        }
        finally { SetBusy(false); }
    }
    private void SdkSelectionChanged(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateInterface(); }
    private void SdkDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (SdkObservations.SelectedItem is not SdkEvidence { Observation: var item }) return;
        new DetailsDialog("SDK observation", $"{item.Kind}\nVersion: {item.Version}\nStatus: {item.Status}\n\nRegistry context: {item.Context}\nLocation: {item.Location}\n\nThis observation does not prove which SDK or SA instance is active.") { Owner = this }.ShowDialog();
    }
    private void SdkGuidanceClicked(object sender, RoutedEventArgs e) =>
        new DetailsDialog("About SDK registration", sdkReport?.Guidance ??
            "Installed products and Windows registry entries provide setup evidence. They do not prove which SDK or SA instance is active.\n\n" +
            "Briosa validates runtime identities before admitting MP work. Multiple installed SA releases can share one COM registration.\n\n" +
            "For stale, missing, or shadowed registration, coordinate a maintenance window and use the vendor-supported installer repair procedure with IT / Hexagon. This app does not activate the SDK or change registration.")
        { Owner = this }.ShowDialog();
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
        if (Navigation.SelectedItem == SdkNavigation) SdkNextStepText.Text = message;
    }
}
