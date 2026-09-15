using System.Windows;
using System.Windows.Threading;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow
{
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Task? settingsWrite;
    private bool writingSettings, closingAfterSave;
    private string? settingsError;
    private int settingsEditVersion;
    public bool IsSavingSettings => writingSettings || settingsTimer.IsEnabled;
    private bool HasSource => snapshot?.Settings is { ServerCatalog.Length: > 0 };

    private void ScheduleSettings(bool debounce = false)
    {
        settingsEditVersion++;
        settingsTimer.Stop();
        dirty = CurrentSettings() != editorBaseline;
        if (!externalChange) settingsError = null;
        if (!dirty && snapshot?.Settings is not null) { StatusText.Text = "Settings are saved automatically."; UpdateInterface(); return; }
        StatusText.Text = externalChange ? settingsError ?? "Settings changed in another window. Reload from disk to continue." : "Saving changes…";
        if (debounce) settingsTimer.Start();
        else _ = PersistSettingsAsync();
        UpdateInterface();
    }

    // One writer owns each snapshot revision. Edits remain enabled during local
    // persistence; the loop saves the latest values if another edit arrives.
    private Task PersistSettingsAsync()
    {
        settingsTimer.Stop();
        return settingsWrite is { IsCompleted: false } ? settingsWrite : settingsWrite = WriteSettingsAsync();
    }

    private async Task WriteSettingsAsync()
    {
        if (snapshot is null || externalChange || catalogClosed) return;
        writingSettings = true;
        try
        {
            while (!catalogClosed)
            {
                var version = settingsEditVersion;
                var editor = CurrentSettings();
                var invalid = (SettingsCodec.ValidateDocument(editor) as Outcome<InstallerSettings>.Failure)?.Error;
                // A partially typed source must not block an independent appearance
                // change or replace the last valid source with incomplete input.
                var settings = invalid is null ? editor : (snapshot.Settings ?? new()) with { Theme = editor.Theme };
                if (settings != snapshot.Settings)
                {
                    var captured = snapshot;
                    var changedSources = !SameSources(captured.Settings, settings);
                    var tested = SameSources(testedServerSettings, settings) ? testedServerCatalog : null;
                    var saved = await Task.Run(() => store.Save(captured, settings));
                    if (saved is Outcome<SettingsSnapshot>.Failure failure)
                    {
                        settingsError = failure.Error.Message;
                        externalChange = failure.Error.Code == ConfigurationError.SaveConflict;
                        StatusText.Text = "Changes could not be saved. " + settingsError;
                        RecordActivity("Settings.Save", failure.Error.Code.ToString());
                        return;
                    }
                    snapshot = ((Outcome<SettingsSnapshot>.Success)saved).Value;
                    editorBaseline = settings;
                    if (changedSources)
                    {
                        InvalidateCatalog();
                        if (tested is not null && SameSources(settings, CurrentSettings()))
                        { catalogSnapshot = tested; CatalogStatusText.Text = CatalogSummary(tested); }
                    }
                    RecordActivity("Settings.Save", "Succeeded");
                }
                dirty = CurrentSettings() != editorBaseline;
                if (version != settingsEditVersion) continue;
                settingsError = invalid?.Message;
                StatusText.Text = invalid is null ? "Settings are saved automatically." :
                    "This source change has not been applied. " + invalid.Message;
                break;
            }
        }
        finally
        {
            writingSettings = false;
            if (!catalogClosed) { UpdateInterface(); RebuildInventory(); }
        }
        _ = EnsureServerCatalogAsync();
    }

    private async void RetrySettingsClicked(object sender, RoutedEventArgs e) => await PersistSettingsAsync();
    private async void SettingsFocusLost(object sender, RoutedEventArgs e)
    {
        if (initialized && !populating && settingsTimer.IsEnabled) await PersistSettingsAsync();
    }
}
