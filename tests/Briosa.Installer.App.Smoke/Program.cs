using System.IO;
using System.Net;
using System.Net.Http;
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
            using var handler = new CatalogHandler();
            using var catalogs = new ReleaseCatalogClient(handler);
            var window = new MainWindow(new(path), catalogs);
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
            Require(handler.Requests.Count == 0, "Settings editing made an unexpected network request.");
            var navigation = Find<ListBox>(window, "Navigation");
            for (var index = 0; index < 4; index++)
            {
                navigation.SelectedIndex = index;
                Require(Find<TextBlock>(window, "PageTitle").Text.Length > 0, "Navigation has no page title.");
            }
            navigation.SelectedIndex = 1;
            var refresh = Find<Button>(window, "RefreshCatalogButton");
            var rows = Find<DataGrid>(window, "CatalogPackages");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refresh.IsEnabled);
            Require(rows.Items.Count == 3, "Server catalog did not list its three fixture packages.");
            rows.SelectedIndex = 0;
            var preview = Find<TextBox>(window, "PackagePreviewText");
            Find<Button>(window, "PreviewPackageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => preview.Text.Length > 0);
            Require(preview.Text.Contains("No package will be installed.", StringComparison.Ordinal), "Preview implied an actionable installation.");
            var component = Find<ComboBox>(window, "CatalogComponentSelector");
            component.SelectedIndex = 1;
            Require(rows.Items.Count == 0 && preview.Text.Length == 0, "Component selection left stale results.");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refresh.IsEnabled);
            Require(rows.Items.Count == 1 && handler.Requests.Last().Contains("briosa-installer/catalog.json", StringComparison.Ordinal), "Installer check did not use its independent source.");
            component.SelectedIndex = 0;
            handler.HoldNext = true;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => handler.Pending is not null);
            Find<TextBox>(window, "ServerCatalog").Text = "https://new-mirror.example.com/briosa/catalog.json";
            handler.Pending!.SetResult(handler.Response());
            PumpUntil(() => !Find<Button>(window, "CancelCatalogButton").IsEnabled);
            Require(rows.Items.Count == 0 && preview.Text.Length == 0, "Late results restored a catalog after its source changed.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => save.IsEnabled);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refresh.IsEnabled);
            rows.SelectedIndex = 0;
            Find<Button>(window, "PreviewPackageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => preview.Text.Length > 0);
            Require(handler.Requests.All(uri => uri.EndsWith("catalog.json", StringComparison.Ordinal)), "Browsing fetched a referenced payload.");
            if (args.Length == 1)
            {
                // The preview uses a symbolic settings path instead of the temporary
                // test directory. Every other element is the actual WPF control tree.
                Find<TextBox>(window, "SettingsPath").Text = @"%LOCALAPPDATA%\Briosa\Installer\settings.json";
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1120, 820));
                root.Arrange(new Rect(0, 0, 1120, 820));
                root.UpdateLayout();
                // DataGrid completes column sizing through deferred dispatcher work.
                for (var pass = 0; pass < 3; pass++)
                {
                    PumpDispatcher();
                    root.UpdateLayout();
                }
                var bitmap = new RenderTargetBitmap(1120, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.GetFullPath(args[0]));
                encoder.Save(stream);
            }
            window.Close();
            app.Shutdown();
            Console.WriteLine("WPF smoke passed: settings, server and installer catalogs, package preview, independent updater routing, stale-result cancellation, and navigation. No native window or real network was used.");
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

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool HoldNext { get; set; }
        public TaskCompletionSource<HttpResponseMessage>? Pending { get; private set; }
        public HttpResponseMessage Response() => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog.json"))),
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (!HoldNext) return Task.FromResult(Response());
            HoldNext = false;
            Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Pending.Task;
        }
    }

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
            PumpDispatcher();
            Thread.Sleep(10);
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
