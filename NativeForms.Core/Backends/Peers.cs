using System.Drawing;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// The native side of a single <see cref="Control"/>. A peer owns one platform widget (an HWND, a
/// GtkWidget*, an NSView …) and exposes only the operations the core needs to keep that widget in
/// sync with the managed control. All coordinates are in the parent's client space, top-left origin,
/// pixels — exactly like Windows Forms.
/// </summary>
public interface IControlPeer : IDisposable
{
    /// <summary>Positions and sizes the widget within its parent.</summary>
    void SetBounds(Rectangle bounds);

    /// <summary>Sets the caption/label text of the widget.</summary>
    void SetText(string text);

    /// <summary>Shows or hides the widget without removing it from the tree.</summary>
    void SetVisible(bool visible);

    /// <summary>Enables or greys out the widget for user interaction.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>A top-level window peer — the native side of a <see cref="Form"/>.</summary>
public interface IWindowPeer : IControlPeer
{
    /// <summary>Re-parents a child peer into this window's client area, creating its native widget.</summary>
    void AddChild(IControlPeer child);

    /// <summary>Makes the window visible and ready to receive input.</summary>
    void Show();

    /// <summary>Raised when the user closes the window (native close button, Alt+F4, ⌘Q …).</summary>
    event EventHandler? Closed;
}

/// <summary>A push-button peer — the native side of a <see cref="Button"/>.</summary>
public interface IButtonPeer : IControlPeer
{
    /// <summary>Raised when the button is activated (click, Space, Enter).</summary>
    event EventHandler? Clicked;
}

/// <summary>A static text peer — the native side of a <see cref="Label"/>.</summary>
public interface ILabelPeer : IControlPeer
{
}
