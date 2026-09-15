using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;
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

    public SourceSecurityDialog(SourceSettings source, ICredentialStore credentials, Func<SourceSettings, Task<string?>>? changed = null)
    {
        Result = source; Title = "Source access and publisher"; Width = 680; Height = 700; MinWidth = 540; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var close = new Button { Content = "Close", IsCancel = true };
        actions.Children.Add(close);
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
        AutomationProperties.SetLiveSetting(credentialStatus, AutomationLiveSetting.Polite); credentialPanel.Children.Add(credentialStatus);
        SourceCredential? stored = null;
        try
        {
            stored = credentials.Read(source.Catalog); user.Text = stored?.UserName ?? "";
            credentialStatus.Text = stored is null ? "No credential stored for this catalog." : "A credential is stored. Leave the secret empty to keep it.";
        }
        catch (ManagementException e) { credentialStatus.Text = e.Message; }
        var removeCredential = new Button { Name = "RemoveCredentialButton", Content = "Remove stored credential…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        credentialPanel.Children.Add(removeCredential);
        credentialPanel.Children.Add(new TextBlock { Text = "Credentials save automatically after you finish typing and stay in Windows Credential Manager for this exact catalog. Secrets are never written to settings.json.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var windowsText = new TextBlock { Text = "Uses your current Windows identity and enterprise proxy credentials only when this method is selected.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) }; content.Children.Add(windowsText);
        var status = new TextBlock { Name = "SourceSaveStatus", Text = "Changes are saved automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Task? pendingSource = null;
        string? sourceError = null, credentialError = null;
        var sourceVersion = 0;
        Task PersistSource()
        {
            if (pendingSource is { IsCompleted: false }) return pendingSource;
            return pendingSource = WriteSource();
        }
        async Task WriteSource()
        {
            while (true)
            {
                var version = sourceVersion;
                sourceError = changed is null ? null : await changed(Result);
                status.Text = sourceError ?? "Changes are saved automatically.";
                if (version == sourceVersion || sourceError is not null) return;
            }
        }
        void SourceChanged()
        {
            sourceVersion++; sourceError = null; status.Text = "Saving changes…";
            _ = PersistSource();
        }
        var credentialTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        var credentialDirty = false;
        var populating = false;
        var credentialMode = source.Authentication;
        void SaveCredential()
        {
            credentialTimer.Stop();
            if (!credentialDirty) return;
            var password = secret.Password.Length > 0 ? secret.Password : stored?.Secret;
            if (credentialMode is not ("bearer" or "basic")) { credentialDirty = false; return; }
            if (string.IsNullOrEmpty(password) || credentialMode == "basic" && string.IsNullOrEmpty(user.Text))
            { credentialError = "Complete the username and secret to apply this credential."; credentialStatus.Text = credentialError; return; }
            try
            {
                if (!source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    throw new ManagementException(ManagementError.InvalidInput);
                var value = new SourceCredential(credentialMode == "basic" ? user.Text : "", password);
                credentials.Save(source.Catalog, value); stored = value;
                credentialDirty = false; credentialError = null; CredentialChanged = true; CredentialRemoved = false;
                credentialStatus.Text = "Credential saved automatically.";
            }
            catch (ManagementException e) { credentialError = e.Message; credentialStatus.Text = e.Message; }
        }
        void CredentialEdited()
        {
            if (populating) return;
            credentialDirty = true; credentialError = null;
            credentialTimer.Stop(); credentialTimer.Start(); credentialStatus.Text = "Saving credential…";
        }
        void UpdateMode()
        {
            var selected = modes[mode.SelectedIndex];
            credentialPanel.Visibility = selected is "basic" or "bearer" ? Visibility.Visible : Visibility.Collapsed;
            user.Visibility = userLabel.Visibility = selected == "basic" ? Visibility.Visible : Visibility.Collapsed;
            secretLabel.Content = selected == "basic" ? "Password" : "Access token";
            windowsText.Visibility = selected == "windows" ? Visibility.Visible : Visibility.Collapsed;
            credentialMode = selected;
        }
        UpdateMode();
        mode.SelectionChanged += (_, _) =>
        {
            var selected = modes[mode.SelectedIndex];
            if (selected != "anonymous" && !source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                mode.SelectedIndex = 0; status.Text = "Authentication requires an HTTPS catalog."; return;
            }
            SaveCredential();
            // Changing the credential kind ends the previous field edit; it never
            // retargets a typed secret to another catalog.
            populating = true; secret.Clear(); populating = false;
            credentialDirty = false; credentialError = null;
            UpdateMode(); Result = Result with { Authentication = selected }; SourceChanged();
        };
        secret.PasswordChanged += (_, _) => CredentialEdited();
        user.TextChanged += (_, _) => CredentialEdited();
        secret.LostKeyboardFocus += (_, _) => SaveCredential();
        user.LostKeyboardFocus += (_, _) => SaveCredential();
        credentialTimer.Tick += (_, _) => SaveCredential();
        removeCredential.Click += (_, _) =>
        {
            if (new ReviewDialog("Remove credential", "Remove the credential stored for this exact catalog? Source access may fail until another credential is configured.", "Remove credential") { Owner = this }.ShowDialog() != true) return;
            try
            {
                credentialTimer.Stop(); credentials.Delete(source.Catalog); stored = null;
                populating = true; secret.Clear(); user.Clear(); populating = false;
                credentialDirty = false; credentialError = null; CredentialChanged = true; CredentialRemoved = true;
                credentialStatus.Text = "No credential stored for this catalog.";
            }
            catch (ManagementException e) { credentialStatus.Text = e.Message; }
        };

        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 18) });
        content.Children.Add(new TextBlock { Text = "Approved publisher", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var publisherStatus = new TextBlock { TextWrapping = TextWrapping.Wrap }; content.Children.Add(publisherStatus);
        var fingerprint = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(fingerprint, "Approved publisher fingerprint");
        content.Children.Add(new Expander { Header = "Publisher fingerprint", Content = fingerprint, Margin = new Thickness(0, 10, 0, 10) });
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
                    [new("SHA-256 fingerprint", hash)]) { Owner = this }.ShowDialog() != true) return;
                Result = Result with { PublisherKey = pem }; UpdatePublisher(); SourceChanged();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ManagementException or System.Security.Cryptography.CryptographicException or ArgumentException)
            { status.Text = "The file is not a supported RSA public key (3072–8192 bits)."; }
        };
        clear.Click += (_, _) => { Result = Result with { PublisherKey = null }; UpdatePublisher(); SourceChanged(); };
        content.Children.Add(status);
        var canClose = false;
        Closing += async (_, e) =>
        {
            if (canClose) return;
            SaveCredential();
            if (pendingSource is not { IsCompleted: false } && sourceError is null && credentialError is null) return;
            e.Cancel = true;
            if (pendingSource is not null) await pendingSource;
            if ((sourceError is not null || credentialError is not null) &&
                new ReviewDialog("Changes could not be applied", "Some access settings could not be saved. Close with the last saved values?", "Close") { Owner = this }.ShowDialog() != true) return;
            canClose = true; _ = Dispatcher.BeginInvoke(new Action(Close));
        };
        Closed += (_, _) => { credentialTimer.Stop(); populating = true; secret.Clear(); stored = null; };
    }
}
