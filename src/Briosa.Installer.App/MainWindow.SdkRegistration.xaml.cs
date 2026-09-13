using System.IO;
using System.Windows;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private readonly ISdkRegistrationService sdkRegistration;
    private bool sdkChanging;
    private async void ChangeSdkClicked(object sender, RoutedEventArgs e)
    {
        if (busy || reviewing || sdkReading || sdkChanging || sdkReport is null) return;
        var choices = sdkReport.Observations.Where(o => o.Kind == "Installed SA product").ToArray();
        var selection = new SdkChoiceDialog(choices, SdkSummaryTitle.Text) { Owner = this };
        if (selection.ShowDialog() != true || selection.SelectedInstallation is not { } selected) return;
        sdkChanging = true; SetBusy(true);
        SdkMaintenanceStatusText.Visibility = Visibility.Visible;
        SdkMaintenanceStatusText.ToolTip = null;
        SdkMaintenanceStatusText.Text = "Checking the selected SDK and maintenance requirements…";
        try
        {
            var plan = await sdkRegistration.PrepareAsync(selected.Location);
            if (!ConfirmAction("Change configured SDK",
                "Register this SDK for new SDK sessions on this machine. This affects SDK consumers beyond Briosa. Keep SA, its SDK, and Briosa servers closed until the change finishes.",
                "Change SDK", [new("Current SDK", plan.CurrentVersion), new("Selected SDK", plan.Target.Version),
                    new("Installed executable", plan.Target.Executable)],
                "Uses the installed Hexagon SDK's /Regserver procedure. Windows will request administrator approval. A local pre-change record is saved; registration is verified afterward. This does not start SA or establish runtime readiness."))
            { SdkMaintenanceStatusText.Text = "SDK change cancelled."; return; }
            SdkMaintenanceStatusText.Text = "Waiting for administrator approval and SDK registration…";
            var result = await sdkRegistration.ApplyAsync(plan);
            RecordActivity("Sdk.Register", result.Code.ToString());
            SdkMaintenanceStatusText.Text = result.Message;
            SdkMaintenanceStatusText.ToolTip = "Local maintenance record: " + result.CheckpointPath;
        }
        catch (SdkRegistrationException failure)
        { SdkMaintenanceStatusText.Text = failure.Message; RecordActivity("Sdk.Register", failure.Code.ToString()); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ManagementException)
        { SdkMaintenanceStatusText.Text = "SDK maintenance could not complete. Check file access and refresh setup before trying again."; RecordActivity("Sdk.Register", "Failed"); }
        finally
        {
            sdkChanging = false; SetBusy(false);
            await RefreshSdkSetupAsync();
        }
    }
}
