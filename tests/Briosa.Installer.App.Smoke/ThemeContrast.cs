using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Briosa.Installer.App;

internal static partial class Program
{
    // Check resolved resources and real control properties, including navigation's
    // local resource overrides. These thresholds protect readability as tints evolve.
    private static void CheckThemeContrast(MainWindow window, string theme, string[] args)
    {
        Require(AppContext.TryGetSwitch("Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering", out var adornerSelection) && !adornerSelection,
            "WPF must render selection behind the text and honor SelectionTextBrush; opaque adorner highlights hide glyphs.");
        var root = (FrameworkElement)window.Content;
        Layout(root, 1140, 800);
        var nav = (ListBoxItem)Find<ListBox>(window, "Navigation").SelectedItem;
        nav.ApplyTemplate();
        var navFill = ((Border)nav.Template.FindName("Border", nav)).Background;
        Check(nav.Foreground, navFill, 4.5, "selected navigation text");
        Check(navFill, Resource("BriosaNavigationBrush"), 3, "navigation selection against sidebar");
        Check(nav.FindResource("ListBoxItemSelectedForegroundThemeBrush"), nav.FindResource("ListBoxItemSelectedBackgroundPointerOverThemeBrush"), 4.5, "navigation hover text");

        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Pair("AccentButtonForeground" + state, "AccentButtonBackground" + state, 4.5);
            Pair("ButtonForeground" + state, "ButtonBackground" + state, 4.5);
            Pair("ButtonBorderBrush" + state, "ButtonBackground" + state, 3);
            Pair("ComboBoxForeground" + state, "ComboBoxBackground" + state, 4.5);
            Pair("ComboBoxBorderBrush" + state, "ComboBoxBackground" + state, 3);
            Pair("CheckBoxCheckGlyphForeground", "CheckBoxCheckBackgroundFillChecked" + state, 3);
        }
        Pair("TabViewItemForegroundSelected", "TabViewItemHeaderBackgroundSelected", 4.5);
        Pair("ListBoxItemSelectedForegroundThemeBrush", "ListBoxItemSelectedBackgroundThemeBrush", 4.5);
        Pair("ListBoxItemSelectedForegroundThemeBrush", "ListBoxItemSelectedBackgroundPointerOverThemeBrush", 4.5);
        Pair("DataGridRowSelectedForegroundThemeBrush", "DataGridRowSelectedBackgroundThemeBrush", 4.5);
        Pair("ComboBoxItemForegroundSelected", "ComboBoxItemBackgroundSelected", 4.5);
        Pair("ComboBoxItemPillFillBrush", "ComboBoxItemBackgroundSelected", 3);
        Pair("TextControlBorderBrushFocused", "TextControlBackgroundFocused", 3);
        Pair("TextControlBorderBrushPointerOver", "TextControlBackgroundPointerOver", 3);
        Pair("KeyboardFocusBorderColorBrush", "CardBackgroundFillColorDefaultBrush", 3);
        Pair("KeyboardFocusBorderColorBrush", "ListBoxItemSelectedBackgroundThemeBrush", 3);

        Page(window, "SettingsNavigation");
        Find<TabControl>(window, "SettingsSections").SelectedIndex = 0;
        Layout(root, 1140, 800);
        var input = Find<TextBox>(window, "ServerCatalog");
        Require(input.SelectionOpacity == 1, "Selected text must use the tested opaque foreground/background pair.");
        Check(input.SelectionTextBrush, input.SelectionBrush, 4.5, "selected input text");
        var secret = new PasswordBox();
        Find<Grid>(window, "PageHost").Children.Add(secret);
        secret.ApplyTemplate();
        Check(secret.SelectionTextBrush, secret.SelectionBrush, 4.5, "selected password glyphs");
        Find<Grid>(window, "PageHost").Children.Remove(secret);

        if (args.Length > 0)
        {
            Render(root, args[0] + $".{theme}-sources.png", 1140, 800);
            Find<TabControl>(window, "SettingsSections").SelectedIndex = 1;
            var recovery = Find<Expander>(window, "PreviousInstallerReleases");
            var wasExpanded = recovery.IsExpanded;
            recovery.IsExpanded = true;
            var releases = Find<DataGrid>(window, "InstallerReleases");
            var selectedRelease = releases.SelectedItem;
            releases.SelectedIndex = 0;
            // Let Fluent's expansion animation finish before accepting the image.
            var settledAt = DateTime.UtcNow.AddMilliseconds(500);
            PumpUntil(() => DateTime.UtcNow >= settledAt);
            Render(root, args[0] + $".{theme}-updates.png", 1140, 800);
            releases.SelectedItem = selectedRelease;
            recovery.IsExpanded = wasExpanded;
        }
        Page(window, "InstallationsNavigation");
        var rows = Find<ListBox>(window, "CatalogPackages");
        var previous = rows.SelectedItem;
        rows.SelectedItem = rows.Items.OfType<ServerRow>().First(r => r.Installed is null);
        if (args.Length > 0) Render(root, args[0] + $".{theme}-install-action.png", 1140, 800);
        rows.SelectedItem = previous;
        Console.WriteLine($"{theme}: navigation, action states, selections, tabs, input/password highlights and control boundaries meet contrast thresholds.");

        object Resource(string key) => window.FindResource(key);
        void Pair(string foreground, string background, double minimum) => Check(Resource(foreground), Resource(background), minimum, foreground);
        void Check(object foreground, object background, double minimum, string label)
        {
            Require(foreground is SolidColorBrush { Color.A: 255 } && background is SolidColorBrush { Color.A: 255 }, $"{theme} {label} must be an opaque color pair.");
            var a = RelativeLuminance(((SolidColorBrush)foreground).Color);
            var b = RelativeLuminance(((SolidColorBrush)background).Color);
            var contrast = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Require(contrast >= minimum, $"{theme} {label}: {contrast:F2}:1 is below {minimum}:1.");
        }
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte value)
        { var s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
