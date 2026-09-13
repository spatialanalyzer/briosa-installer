using System.IO;
using System.Windows;
using System.Windows.Controls;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.App;

public sealed class SourceSecurityDialog : Window
{
    public SourceSettings Result { get; private set; }
    public SourceSecurityDialog(SourceSettings source, ICredentialStore credentials)
    {
        Result = source;
        Title = "Source authentication and publisher"; Width = 660; Height = 640;
        MinWidth = 520; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(new TextBlock { Text = source.Catalog, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var modes = new[] { "anonymous", "bearer", "basic", "windows" };
        var mode = new ComboBox { ItemsSource = modes, SelectedItem = source.Authentication, Padding = new Thickness(8), MinHeight = 38 };
        content.Children.Add(new Label { Content = "Authentication", Target = mode }); content.Children.Add(mode);
        var user = new TextBox();
        content.Children.Add(new Label { Content = "Username (Basic authentication only)", Target = user, Margin = new Thickness(0, 16, 0, 0) }); content.Children.Add(user);
        var secret = new PasswordBox { Padding = new Thickness(10), MinHeight = 38 };
        content.Children.Add(new Label { Content = "Token / password (leave blank to retain the stored credential)", Target = secret, Margin = new Thickness(0, 16, 0, 0) }); content.Children.Add(secret);
        content.Children.Add(new TextBlock { Text = "Bearer tokens and Basic credentials are bound to this exact catalog in Windows Credential Manager. Windows authentication uses your current Windows identity and enterprise proxy credentials only when explicitly selected.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16) });
        var fingerprint = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        void UpdateFingerprint() => fingerprint.Text = Result.PublisherKey is null ? "No publisher key configured. Browsing is available; installation is blocked." : PublisherTrust.Fingerprint(Result.PublisherKey);
        UpdateFingerprint();
        content.Children.Add(new Label { Content = "Approved publisher SHA-256 fingerprint", Target = fingerprint }); content.Children.Add(fingerprint);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 20) };
        var import = new Button { Content = "Import public key…", Margin = new Thickness(0, 0, 10, 0) };
        import.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "PEM public key (*.pem)|*.pem|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 4096) throw new ManagementException(ManagementError.UntrustedPublisher);
                var pem = File.ReadAllText(dialog.FileName);
                var hash = PublisherTrust.Fingerprint(pem);
                if (MessageBox.Show(this, "Compare this fingerprint with the publisher or your IT team's approved value before trusting it:\n\n" + hash + "\n\nTrust this publisher for this source?", "Confirm publisher", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Result = Result with { PublisherKey = pem }; UpdateFingerprint();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ManagementException or System.Security.Cryptography.CryptographicException or ArgumentException)
            { MessageBox.Show(this, "The file is not a supported RSA public key (3072–8192 bits).", "Publisher key"); }
        };
        var clear = new Button { Content = "Clear publisher key" };
        clear.Click += (_, _) => { Result = Result with { PublisherKey = null }; UpdateFingerprint(); };
        buttons.Children.Add(import); buttons.Children.Add(clear); content.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; content.Children.Add(status);
        var save = new Button { Content = "Use these settings", HorizontalAlignment = HorizontalAlignment.Left, IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                var selected = (string)mode.SelectedItem;
                if (selected != "anonymous" && !source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ManagementException(ManagementError.InvalidInput);
                if (selected is "bearer" or "basic" && secret.Password.Length > 0)
                    credentials.Save(source.Catalog, new(selected == "basic" ? user.Text : "", secret.Password));
                Result = Result with { Authentication = selected };
                secret.Clear(); DialogResult = true;
            }
            catch (ManagementException e) { status.Text = e.Message; }
        };
        content.Children.Add(save);
        Closed += (_, _) => secret.Clear();
    }
}
