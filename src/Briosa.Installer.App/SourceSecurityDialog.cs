using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.App;

public sealed class SourceSecurityDialog : Window
{
    public SourceSettings Result { get; private set; }
    public bool CredentialChanged { get; private set; }
    public bool CredentialRemoved { get; private set; }
    public static string AuthenticationLabel(string mode) => mode switch
    { "bearer" => "Access token", "basic" => "Username and password", "windows" => "Windows sign-in", _ => "No authentication" };

    public SourceSecurityDialog(SourceSettings source, ICredentialStore credentials)
    {
        Result = source; Title = "Source access and publisher"; Width = 680; Height = 700; MinWidth = 540; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Apply to settings" }; apply.SetResourceReference(StyleProperty, "PrimaryButton");
        actions.Children.Add(cancel); actions.Children.Add(apply);
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock { Text = "Source access", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(new TextBlock { Text = source.Catalog, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var modes = new[] { "anonymous", "bearer", "basic", "windows" };
        var mode = new ComboBox { Name = "AuthenticationMode", ItemsSource = modes.Select(m => new ComboBoxItem { Content = AuthenticationLabel(m), Tag = m }).ToArray(), SelectedIndex = Array.IndexOf(modes, source.Authentication) };
        AutomationProperties.SetName(mode, "Authentication method");
        content.Children.Add(new Label { Content = "Authentication method", Target = mode }); content.Children.Add(mode);
        var credentialPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 16) }; content.Children.Add(credentialPanel);
        var user = new TextBox { Name = "CredentialUser" };
        var userLabel = new Label { Content = "Username", Target = user };
        credentialPanel.Children.Add(userLabel); credentialPanel.Children.Add(user);
        var secret = new PasswordBox { Name = "CredentialSecret", Padding = new Thickness(10), MinHeight = 36 };
        var secretLabel = new Label { Content = "Access token", Target = secret, Margin = new Thickness(0, 10, 0, 0) };
        credentialPanel.Children.Add(secretLabel); credentialPanel.Children.Add(secret);
        var credentialStatus = new TextBlock { Name = "CredentialStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        AutomationProperties.SetLiveSetting(credentialStatus, System.Windows.Automation.AutomationLiveSetting.Polite);
        credentialPanel.Children.Add(credentialStatus);
        void ReadCredentialStatus()
        {
            try
            {
                var stored = credentials.Read(source.Catalog);
                user.Text = stored?.UserName ?? "";
                credentialStatus.Text = stored is null ? "No credential stored for this exact catalog." : "A credential is stored for this exact catalog. Its secret is never displayed.";
            }
            catch (ManagementException e) { credentialStatus.Text = e.Message; }
        }
        ReadCredentialStatus();
        var credentialActions = new WrapPanel();
        var saveCredential = new Button { Name = "SaveCredentialButton", Content = "Save credential now", Margin = new Thickness(0, 0, 8, 8) };
        var removeCredential = new Button { Name = "RemoveCredentialButton", Content = "Remove stored credential…", Margin = new Thickness(0, 0, 0, 8) };
        credentialActions.Children.Add(saveCredential); credentialActions.Children.Add(removeCredential); credentialPanel.Children.Add(credentialActions);
        credentialPanel.Children.Add(new TextBlock { Text = "Credential actions take effect immediately in Windows Credential Manager. Cancel and Discard changes do not undo them. The access method and publisher below apply only when you save the main Settings page.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var windowsText = new TextBlock { Text = "Uses your current Windows identity and enterprise proxy credentials only when this method is selected.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) }; content.Children.Add(windowsText);
        void UpdateMode()
        {
            var selected = modes[mode.SelectedIndex];
            credentialPanel.Visibility = selected is "basic" or "bearer" ? Visibility.Visible : Visibility.Collapsed;
            user.Visibility = userLabel.Visibility = selected == "basic" ? Visibility.Visible : Visibility.Collapsed;
            secretLabel.Content = selected == "basic" ? "Password" : "Access token";
            windowsText.Visibility = selected == "windows" ? Visibility.Visible : Visibility.Collapsed;
            secret.Clear();
        }
        mode.SelectionChanged += (_, _) => UpdateMode(); UpdateMode();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        saveCredential.Click += (_, _) =>
        {
            try
            {
                if (!source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || modes[mode.SelectedIndex] is not ("bearer" or "basic"))
                    throw new ManagementException(ManagementError.InvalidInput);
                credentials.Save(source.Catalog, new(modes[mode.SelectedIndex] == "basic" ? user.Text : "", secret.Password));
                secret.Clear(); CredentialChanged = true; CredentialRemoved = false;
                credentialStatus.Text = "Credential saved now. Page Discard will not undo this change.";
            }
            catch (ManagementException e) { credentialStatus.Text = e.Message; }
        };
        removeCredential.Click += (_, _) =>
        {
            if (new ReviewDialog("Remove credential", "Remove the credential stored for this exact catalog? Source access may fail until another credential is saved.", "Remove credential") { Owner = this }.ShowDialog() != true) return;
            try { credentials.Delete(source.Catalog); secret.Clear(); CredentialChanged = true; CredentialRemoved = true; ReadCredentialStatus(); }
            catch (ManagementException e) { credentialStatus.Text = e.Message; }
        };

        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 18) });
        content.Children.Add(new TextBlock { Text = "Approved publisher", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var publisherStatus = new TextBlock { TextWrapping = TextWrapping.Wrap }; content.Children.Add(publisherStatus);
        var fingerprint = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(fingerprint, "Approved publisher fingerprint");
        var evidence = new Expander { Header = "Publisher fingerprint", Content = fingerprint, Margin = new Thickness(0, 10, 0, 10) }; content.Children.Add(evidence);
        void UpdatePublisher()
        {
            publisherStatus.Text = Result.PublisherKey is null ? "No approved publisher configured. Catalog browsing is available; installation is blocked." : "Approved publisher key configured. Catalog signatures and package integrity are still checked.";
            fingerprint.Text = Result.PublisherKey is null ? "No key configured" : PublisherTrust.Fingerprint(Result.PublisherKey);
        }
        UpdatePublisher();
        var publisherActions = new WrapPanel();
        var import = new Button { Content = "Import public key…", Margin = new Thickness(0, 0, 8, 8) };
        var clear = new Button { Content = "Clear key", Margin = new Thickness(0, 0, 0, 8) };
        publisherActions.Children.Add(import); publisherActions.Children.Add(clear); content.Children.Add(publisherActions);
        import.Click += (_, _) =>
        {
            var file = new OpenFileDialog { Filter = "PEM public key (*.pem)|*.pem|All files (*.*)|*.*" };
            if (file.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(file.FileName).Length > 4096) throw new ManagementException(ManagementError.UntrustedPublisher);
                var pem = File.ReadAllText(file.FileName); var hash = PublisherTrust.Fingerprint(pem);
                if (new ReviewDialog("Approve publisher", "Compare this fingerprint with the publisher or your IT team's approved value before trusting it.", "Use this key",
                    [new("SHA-256 fingerprint", hash)])
                { Owner = this }.ShowDialog() != true) return;
                Result = Result with { PublisherKey = pem }; UpdatePublisher();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ManagementException or System.Security.Cryptography.CryptographicException or ArgumentException)
            { status.Text = "The file is not a supported RSA public key (3072–8192 bits)."; }
        };
        clear.Click += (_, _) => { Result = Result with { PublisherKey = null }; UpdatePublisher(); };
        content.Children.Add(status);
        apply.Click += (_, _) =>
        {
            var selected = modes[mode.SelectedIndex];
            if (selected != "anonymous" && !source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { status.Text = "Authentication requires an HTTPS catalog."; return; }
            if (secret.Password.Length > 0)
            { status.Text = "Save the entered credential now, or clear the secret field before applying source settings."; return; }
            Result = Result with { Authentication = selected }; DialogResult = true;
        };
        Closed += (_, _) => secret.Clear();
    }
}
