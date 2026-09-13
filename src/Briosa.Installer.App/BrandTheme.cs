using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Briosa.Installer.App;

/// <summary>Maps Briosa v1 colors to native Fluent controls without replacing their templates.</summary>
public sealed class BrandTheme : IDisposable
{
    private readonly Application app;
    private static readonly HashSet<string> nativeOverrides = [];
    private static readonly Dictionary<string, BitmapSource> images = [];
    private static readonly Color Blue = Color.FromRgb(0, 56, 117);
    private static readonly Color Cyan = Color.FromRgb(0, 186, 241);
    private static readonly Color Graphite = Color.FromRgb(88, 91, 98);
    private static readonly Color Silver = Color.FromRgb(242, 242, 242);
    private bool disposed;

    public BrandTheme(Application app)
    {
        this.app = app;
        ApplySystem(app);
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameterChanged;
    }

    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void SystemParameterChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        if (!disposed && !app.Dispatcher.HasShutdownStarted)
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { if (!disposed) ApplySystem(app); }));
    }

    public static void ApplySystem(Application app)
    {
        var dark = false;
#pragma warning disable WPF0001 // WPF's selected theme; no OS setting is changed.
        if (app.ThemeMode == ThemeMode.Dark) dark = true;
        else if (app.ThemeMode == ThemeMode.System)
#pragma warning restore WPF0001
        {
            // Matches WPF's system-theme detection; reading is optional and fails to light.
            try
            {
                using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = preferences?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
        }
        Apply(app, dark, SystemParameters.HighContrast);
    }

    public static void ApplyPreference(Application app, string theme)
    {
#pragma warning disable WPF0001 // App-wide Fluent appearance; never writes a Windows preference.
        app.ThemeMode = theme switch
        {
            "system" => ThemeMode.System,
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(theme)),
        };
#pragma warning restore WPF0001
        ApplySystem(app);
    }

    // Explicit appearance parameters also let the control-tree harness validate both
    // palettes and high-contrast delegation without changing the user's Windows settings.
    public static void Apply(Application app, bool dark, bool highContrast)
    {
        var resources = app.Resources;
        foreach (var key in nativeOverrides) resources.Remove(key);
        nativeOverrides.Clear();
        var white = Colors.White;
        // A deeper graphite shade for long desktop sessions; the brand color
        // remains the light-mode text color and the source for neutral tints.
        var darkBase = Mix(Graphite, Colors.Black, .66); // #1E1F21
        var text = dark ? white : Graphite;
        var background = dark ? darkBase : white;
        var surface = dark ? Mix(darkBase, white, .045) : Silver;
        var control = dark ? Mix(darkBase, white, .07) : white;
        var border = dark ? Mix(darkBase, Silver, .43) : Mix(Graphite, white, .25);
        var accent = dark ? Cyan : Blue;
        var onAccent = dark ? Blue : white;
        var selected = dark ? Mix(darkBase, Cyan, .12) : Silver;
        var navBackground = highContrast ? SystemColors.WindowColor : dark ? Mix(darkBase, Colors.Black, .14) : Silver;
        var navText = highContrast ? SystemColors.WindowTextColor : text;
        var navSelected = highContrast ? SystemColors.HighlightColor : Cyan;
        var navSelectedText = highContrast ? SystemColors.HighlightTextColor : Blue;
        resources["BriosaNavigationBackgroundColor"] = navBackground;
        resources["BriosaNavigationTextColor"] = navText;
        resources["BriosaNavigationSelectedColor"] = navSelected;
        resources["BriosaNavigationSelectedTextColor"] = navSelectedText;
        resources["BriosaNavigationHoverColor"] = highContrast ? SystemColors.ControlColor : dark ? Mix(darkBase, white, .10) : Mix(Silver, Graphite, .06);
        resources["BriosaNavigationBrush"] = Brush(navBackground);
        resources["BriosaNavigationTextBrush"] = Brush(navText);
        resources["BriosaHeadingBrush"] = Brush(highContrast ? SystemColors.WindowTextColor : dark ? white : Blue);
        resources["BriosaSelectionBorderBrush"] = Brush(highContrast ? SystemColors.HighlightColor : accent);
        var logo = highContrast ? (Luminance(navBackground) < .5 ? "white" : "black") : dark ? "white" : "color";
        resources["BriosaLogo"] = Bitmap($"Brand/png/logos/briosa-horizontal-{logo}.png");
        // Native vector geometry is non-interactive and completely removed in high contrast.
        resources["BriosaWorkspaceBrush"] = highContrast ? Brush(SystemColors.WindowColor) : LayeredPlanes.Create(dark, darkBase, Silver, Cyan);
        // Let WPF's high-contrast resource dictionary own all native control colors.
        if (highContrast) return;

        void Paint(Color color, params string[] keys)
        {
            var brush = Brush(color);
            foreach (var key in keys) { resources[key] = brush; nativeOverrides.Add(key); }
        }
        Paint(background, "ApplicationBackgroundBrush", "WindowBackground");
        Paint(surface, "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush", "LayerFillColorDefaultBrush",
            "DataGridHeaderBackground", "DataGridColumnHeaderBackground", "ExpanderHeaderBackground", "ExpanderContentBackground", "TabViewItemHeaderBackgroundSelected");
        Paint(text, "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "ListBoxItemForeground", "ButtonForeground",
            "ButtonForegroundPointerOver", "ButtonForegroundPressed", "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed", "ComboBoxForegroundFocused",
            "ComboBoxDropDownForeground", "ComboBoxItemForeground", "ComboBoxItemForegroundSelected", "CheckBoxForegroundUnchecked",
            "CheckBoxForegroundChecked", "DataGridColumnHeaderForeground", "ExpanderHeaderForeground", "TabViewForeground", "TabViewItemForegroundSelected");
        Paint(control, "ControlFillColorDefaultBrush", "ControlFillColorInputActiveBrush", "ButtonBackground", "TextControlBackground",
            "TextControlBackgroundFocused", "ComboBoxBackground", "ComboBoxBackgroundFocused", "ComboBoxDropDownBackground");
        Paint(dark ? Mix(darkBase, white, .12) : Silver, "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "TextControlBackgroundPointerOver",
            "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxDropDownBackgroundPointerOver",
            "ListBoxItemUnselectedBackgroundPointerOverThemeBrush");
        Paint(dark ? Mix(darkBase, white, .18) : Mix(Graphite, white, .65), "ControlElevationBorderBrush");
        Paint(border, "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ComboBoxBorderBrush", "ComboBoxDropDownBorderBrush");
        Paint(accent, "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentButtonBackground",
            "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
            "TextControlBorderBrushFocused", "TextControlFocusedBorderBrush", "ComboBoxBorderBrushFocused", "ComboBoxItemPillFillBrush",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "ProgressBarForeground", "KeyboardFocusBorderColorBrush");
        Paint(Mix(accent, white, .08), "AccentButtonBackgroundPointerOver");
        Paint(Mix(accent, white, .16), "AccentButtonBackgroundPressed");
        Paint(onAccent, "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "AccentButtonForeground",
            "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed", "CheckBoxCheckGlyphForeground", "CheckBoxCheckGlyphForegroundPressed");
        Paint(selected, "ListBoxItemSelectedBackgroundThemeBrush", "ListBoxItemSelectedBackgroundPointerOverThemeBrush",
            "ListBoxItemSelectedBackgroundPressedThemeBrush", "DataGridRowSelectedBackgroundThemeBrush");
        Paint(dark ? white : Blue, "ListBoxItemSelectedForegroundThemeBrush", "DataGridRowSelectedForegroundThemeBrush");
        Paint(accent, "AccentFillColorSelectedTextBackgroundBrush", "TextControlSelectionHighlightColor");
    }

    private static BitmapSource Bitmap(string path)
    {
        if (!images.TryGetValue(path, out var image))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri($"pack://application:,,,/Briosa.Installer;component/Assets/{path}");
            bitmap.EndInit(); bitmap.Freeze();
            images[path] = image = bitmap;
        }
        return image;
    }

    private static SolidColorBrush Brush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Color Mix(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
        (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
        (byte)Math.Round(background.B + (foreground.B - background.B) * amount));
    private static double Luminance(Color color) => (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255;
    public void Dispose()
    {
        disposed = true;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameterChanged;
    }
}
