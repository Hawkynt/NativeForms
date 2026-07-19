using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A push button. Backed by the platform's native button widget, so it looks and behaves exactly
/// like every other button on the user's desktop.
/// </summary>
public class Button : Control
{
    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateButton();

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is IButtonPeer button)
            button.Clicked += (_, _) => this.OnClick(EventArgs.Empty);
    }
}
