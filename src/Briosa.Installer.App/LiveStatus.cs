using System.ComponentModel;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Briosa.Installer.App;

// WPF live regions need an explicit event when text changes; the property alone
// describes politeness but does not announce operation results to assistive tools.
internal sealed class LiveStatus : IDisposable
{
    private readonly TextBlock text;
    private readonly DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    public LiveStatus(TextBlock text)
    {
        this.text = text;
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        descriptor.AddValueChanged(text, TextChanged);
    }

    private void TextChanged(object? sender, EventArgs e)
    {
        if (!text.IsVisible || string.IsNullOrWhiteSpace(text.Text) || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        var peer = UIElementAutomationPeer.FromElement(text) ?? UIElementAutomationPeer.CreatePeerForElement(text);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public void Dispose() => descriptor.RemoveValueChanged(text, TextChanged);
}
