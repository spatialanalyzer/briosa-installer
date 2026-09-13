using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace Briosa.Installer.App;

public sealed record ReviewFact(string Label, string Value);

public sealed class ReviewDialog : Window
{
    public ReviewDialog(string title, string message, string action, IEnumerable<ReviewFact>? facts = null, string? details = null)
    {
        Title = title; Width = 640; MaxHeight = 680; MinWidth = 480; MinHeight = 300;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var accept = new Button { Content = action, MinWidth = 100 };
        accept.SetResourceReference(StyleProperty, "PrimaryButton");
        accept.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(accept);
        var content = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 490 });
        content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        foreach (var fact in facts ?? [])
        {
            content.Children.Add(new TextBlock { Text = fact.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            content.Children.Add(new TextBlock { Text = fact.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }
        if (!string.IsNullOrEmpty(details))
            content.Children.Add(new Expander { Header = "Verification details", Content = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) } });
        Loaded += (_, _) => cancel.Focus();
    }
}

public sealed class DetailsDialog : Window
{
    public DetailsDialog(string title, string text)
    {
        Title = title; Width = 760; Height = 540; MinWidth = 480; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var copy = new Button { Content = "Copy details", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(text); copy.Content = "Copied"; } catch (System.Runtime.InteropServices.ExternalException) { copy.Content = "Clipboard busy; try again"; } };
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close();
        buttons.Children.Add(copy); buttons.Children.Add(close);
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(box, title); root.Children.Add(box);
        Loaded += (_, _) => close.Focus();
    }
}

