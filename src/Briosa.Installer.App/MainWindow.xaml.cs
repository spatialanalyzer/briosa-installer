using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class MainWindow : Window
{
    private readonly ConfigurationPaths paths;
    private readonly SettingsStore store = new();
    private SettingsSnapshot? snapshot;
    private bool populating;
    private bool dirty;
    private bool busy;

    public MainWindow(ConfigurationPaths paths, ReleaseCatalogClient? catalogClient = null)
    {
        this.paths = paths;
        this.catalogClient = catalogClient ?? new ReleaseCatalogClient();
        ownsCatalogClient = catalogClient is null;
        InitializeComponent();
        SettingsPath.Text = paths.ExplicitFile ?? paths.UserFile;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        InvalidateCatalog();
        SetBusy(true);
        try
        {
            var loaded = await Task.Run(() => store.Load(paths));
            snapshot = loaded is Outcome<SettingsSnapshot>.Success success ? success.Value : null;
            if (loaded is Outcome<SettingsSnapshot>.Failure failure)
            {
                StatusText.Text = $"{failure.Error.Code}: {failure.Error.Message}";
                AddActivity("Settings could not be loaded.");
                return;
            }
            populating = true;
            ServerCatalog.Text = snapshot!.Settings?.ServerCatalog ?? "";
            InstallerCatalog.Text = snapshot.Settings?.InstallerCatalog ?? "";
            SameSource.IsChecked = snapshot.Settings?.InstallerCatalog is null;
            populating = false;
            dirty = false;
            UpdateEffectiveSource();
            StatusText.Text = snapshot.Origin switch
            {
                SettingsOrigin.SetupRequired => "Choose your catalog and save to get started.",
                SettingsOrigin.MachineDefaults => "Machine defaults loaded. Saving creates your personal settings file.",
                _ => "Settings loaded. Catalog access has not been tested.",
            };
            AddActivity("Settings loaded.");
        }
        finally { populating = false; SetBusy(false); }
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (snapshot is null || busy) return;
        var settings = CurrentSettings();
        SetBusy(true);
        try
        {
            var saved = await Task.Run(() => store.Save(snapshot, settings));
            if (saved is Outcome<SettingsSnapshot>.Failure failure)
            {
                StatusText.Text = $"{failure.Error.Code}: {failure.Error.Message}";
                AddActivity("Settings were not saved.");
                return;
            }
            snapshot = ((Outcome<SettingsSnapshot>.Success)saved).Value;
            dirty = false;
            InvalidateCatalog();
            StatusText.Text = "Settings saved. Catalog access has not been tested.";
            AddActivity("Source settings saved.");
        }
        finally { SetBusy(false); }
    }

    private async void ReloadClicked(object sender, RoutedEventArgs e)
    {
        if (!busy && ConfirmDiscard()) await ReloadAsync();
    }

    private void SourceChanged(object sender, RoutedEventArgs e)
    {
        if (EffectiveSource is null || SameSource is null || InstallerCatalog is null) return;
        UpdateEffectiveSource();
        if (!populating)
        {
            dirty = true;
            InvalidateCatalog();
        }
    }

    private void UpdateEffectiveSource()
    {
        InstallerCatalog.IsEnabled = SameSource.IsChecked != true && !busy;
        var effective = CurrentSettings().EffectiveInstallerCatalog;
        EffectiveSource.Text = string.IsNullOrWhiteSpace(effective) ? "Choose a catalog for installer updates." : "Effective update catalog: " + effective;
    }

    private InstallerSettings CurrentSettings() => new(ServerCatalog.Text, SameSource.IsChecked == true ? null : InstallerCatalog.Text);

    private void JsonClicked(object sender, RoutedEventArgs e)
    {
        var settings = CurrentSettings();
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure failure)
        {
            StatusText.Text = failure.Error.Message;
            return;
        }
        var text = new TextBox
        {
            Text = SettingsCodec.Serialize(settings), IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14,
            Margin = new Thickness(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        System.Windows.Automation.AutomationProperties.SetName(text, "Settings JSON preview");
        new Window { Owner = this, Title = "Settings JSON — current editor values", Width = 740, Height = 420,
            Content = text, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcesPage is null) return;
        var pages = new[] { SourcesPage, InstallationsPage, SdkPage, ActivityPage };
        for (var index = 0; index < pages.Length; index++)
            pages[index].Visibility = index == Navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = ((ListBoxItem)Navigation.SelectedItem).Content.ToString();
        PageDescription.Text = Navigation.SelectedIndex switch
        {
            0 => "Choose where Briosa gets server packages and installer updates.",
            1 => "Manage independent server versions for exact SpatialAnalyzer releases.",
            2 => "Understand SDK setup before planning maintenance.",
            _ => "Review settings operations from this app session.",
        };
    }

    private void SetBusy(bool value)
    {
        busy = value;
        SaveButton.IsEnabled = !value && snapshot is not null;
        ReloadButton.IsEnabled = !value;
        JsonButton.IsEnabled = !value;
        ServerCatalog.IsEnabled = !value;
        SameSource.IsEnabled = !value;
        UpdateEffectiveSource();
        UpdateCatalogControls();
    }

    private void AddActivity(string message)
    {
        ActivityList.Items.Insert(0, message);
        if (ActivityList.Items.Count > 50) ActivityList.Items.RemoveAt(50);
    }

    private bool ConfirmDiscard() => !dirty || MessageBox.Show(this,
        "Discard your unsaved source settings?", "Briosa Installer", MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = busy || !ConfirmDiscard();
        if (!e.Cancel) catalogRead?.Cancel();
    }
}
