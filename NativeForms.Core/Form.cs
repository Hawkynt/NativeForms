using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A top-level window. Maps to a native window on every platform; its <see cref="Control.Text"/> is
/// the title-bar caption and its <see cref="Control.Controls"/> are laid out in the client area.
/// </summary>
public class Form : Control
{
    private IWindowPeer? _window;

    /// <summary>The realized native window peer, or <see langword="null"/> before realization.</summary>
    internal IWindowPeer? WindowPeer => _window;

    /// <summary>Raised after the user closes the window.</summary>
    public event EventHandler? FormClosed;

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateWindow();

    /// <summary>Raises <see cref="FormClosed"/>.</summary>
    protected virtual void OnFormClosed(EventArgs e) => this.FormClosed?.Invoke(this, e);

    /// <summary>
    /// Realizes this form and its children against <paramref name="backend"/>, then shows it. Returns
    /// the native window peer that <see cref="Application.Run(Form)"/> hands to the message loop.
    /// </summary>
    internal IWindowPeer RealizeWindow(IPlatformBackend backend)
    {
        var window = (IWindowPeer)this.RealizeSelf(backend);
        _window = window;
        window.Closed += (_, _) => this.OnFormClosed(EventArgs.Empty);

        foreach (var child in this.Controls)
        {
            var childPeer = child.RealizeSelf(backend);
            window.AddChild(childPeer);
        }

        window.Show();
        return window;
    }
}
