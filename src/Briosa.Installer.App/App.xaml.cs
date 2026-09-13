using System.Windows;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? config = null, store = null, bootstrap = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < e.Args.Length; index += 2)
            {
                if (index + 1 >= e.Args.Length || e.Args[index] is not ("--config" or "--store" or "--bootstrap") || !seen.Add(e.Args[index])) throw new ArgumentException();
                if (e.Args[index] == "--config") config = e.Args[index + 1];
                else if (e.Args[index] == "--store") store = e.Args[index + 1]; else bootstrap = e.Args[index + 1];
            }
            var paths = ConfigurationPaths.ForCurrentUser(config);
            new MainWindow(paths, packageStore: new PackageStore(store), bootstrapPath: bootstrap).Show();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("The settings location is invalid or inaccessible.", "Briosa Installer");
            Shutdown(2);
        }
    }
}
