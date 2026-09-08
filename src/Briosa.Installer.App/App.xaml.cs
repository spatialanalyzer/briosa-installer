using System.Windows;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length != 0 && (e.Args.Length != 2 || e.Args[0] != "--config"))
        {
            MessageBox.Show("Use Briosa.Installer.exe [--config <settings-file>].", "Briosa Installer");
            Shutdown(2);
            return;
        }
        try
        {
            var paths = ConfigurationPaths.ForCurrentUser(e.Args.Length == 2 ? e.Args[1] : null);
            new MainWindow(paths).Show();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("The settings location is invalid or inaccessible.", "Briosa Installer");
            Shutdown(2);
        }
    }
}
