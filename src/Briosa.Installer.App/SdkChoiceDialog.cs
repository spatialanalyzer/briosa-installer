using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public sealed class SdkChoiceDialog : Window
{
    public SdkObservation? SelectedInstallation { get; private set; }
    public SdkChoiceDialog(SdkReport report, string current)
    {
        Title = "Change configured SDK"; Width = 660; MinWidth = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        var content = new StackPanel { Margin = new Thickness(24) }; Content = content;
        content.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        content.Children.Add(new TextBlock { Text = current, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        content.Children.Add(new TextBlock { Text = "Use the SDK included with:", Margin = new Thickness(0, 0, 0, 6) });
        var choices = new ComboBox { Name = "SdkInstallationChoice", ItemsSource = SdkSetupPresentation.RegistrationChoices(report), DisplayMemberPath = "DisplayName", MinWidth = 240 };
        AutomationProperties.SetName(choices, "SpatialAnalyzer installation for SDK registration");
        AutomationProperties.SetHelpText(choices, "Recommended identifies the latest SA release installed on this machine.");
        content.Children.Add(choices);
        var path = new TextBlock { Text = "Choose an installed SA release.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) };
        content.Children.Add(path);
        content.Children.Add(new TextBlock { Text = "This changes shared SDK registration for new sessions. Close SA, its SDK, and Briosa servers before continuing. Windows will request administrator approval.", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var review = new Button { Content = "Review change…", IsEnabled = false };
        choices.SelectionChanged += (_, _) => { SelectedInstallation = (choices.SelectedItem as SdkInstallationChoice)?.Installation; path.Text = SelectedInstallation?.Location ?? "Choose an installed SA release."; review.IsEnabled = SelectedInstallation is not null; };
        review.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(review); content.Children.Add(actions);
        Loaded += (_, _) => choices.Focus();
    }
}
