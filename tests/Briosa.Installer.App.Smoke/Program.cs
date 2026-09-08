using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Briosa.Installer.App;
using Briosa.Installer.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Briosa.Installer.App.Smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Briosa.Installer;component/Styles.xaml", UriKind.Absolute),
            });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var path = Path.Combine(directory, "settings.json");
            var window = new MainWindow(new(path));
            // Exercise our own WPF tree without displaying a native window or
            // accessing any existing user settings, desktop application, or SDK.
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var save = Find<Button>(window, "SaveButton");
            PumpUntil(() => save.IsEnabled);
            Find<TextBox>(window, "ServerCatalog").Text = "https://artifacts.example.com/artifactory/briosa-servers/catalog.json";
            Find<CheckBox>(window, "SameSource").IsChecked = false;
            var updater = Find<TextBox>(window, "InstallerCatalog");
            Require(updater.IsEnabled, "Separate update catalog field did not enable.");
            updater.Text = "https://artifacts.example.com/artifactory/briosa-installer/catalog.json";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            var result = new SettingsStore().Load(new(path));
            Require(result is Outcome<SettingsSnapshot>.Success { Value.Settings.InstallerCatalog: not null }, "WPF save did not persist both catalogs.");
            Find<Button>(window, "ReloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            Require(updater.Text.EndsWith("briosa-installer/catalog.json", StringComparison.Ordinal), "WPF reload lost the update source.");
            var navigation = Find<ListBox>(window, "Navigation");
            for (var index = 0; index < 4; index++)
            {
                navigation.SelectedIndex = index;
                Require(Find<TextBlock>(window, "PageTitle").Text.Length > 0, "Navigation has no page title.");
            }
            navigation.SelectedIndex = 0;
            if (args.Length == 1)
            {
                // The preview uses a symbolic settings path instead of the temporary
                // test directory. Every other element is the actual WPF control tree.
                Find<TextBox>(window, "SettingsPath").Text = @"%LOCALAPPDATA%\Briosa\Installer\settings.json";
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1120, 820));
                root.Arrange(new Rect(0, 0, 1120, 820));
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1120, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.GetFullPath(args[0]));
                encoder.Save(stream);
            }
            window.Close();
            app.Shutdown();
            Console.WriteLine("WPF smoke passed: XAML/resources, separate update field, save/reload through the shared engine, and navigation. No native window was displayed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static T Find<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing control: {name}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF settings operation did not complete.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
}
