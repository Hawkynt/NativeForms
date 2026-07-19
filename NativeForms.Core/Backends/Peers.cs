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

/// <summary>
/// The native side of a control that hosts other controls: a window's client area or an owner-drawn
/// surface. Mirrors Windows Forms, where any control is a potential parent — realizing a control
/// tree walks it depth-first, handing each child peer to its container.
/// </summary>
public interface IContainerPeer : IControlPeer
{
    /// <summary>Re-parents a child peer into this container's client area, creating its native widget.</summary>
    void AddChild(IControlPeer child);
}

/// <summary>A top-level window peer — the native side of a <see cref="Form"/>.</summary>
public interface IWindowPeer : IContainerPeer
{
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

/// <summary>
/// A recurring UI-thread timer source — the native side of a <see cref="Timer"/>. Ticks are delivered
/// by the platform message loop, so they always arrive on the thread that pumps it. Starting a peer
/// that is already running restarts it with the new interval.
/// </summary>
public interface ITimerPeer : IDisposable
{
    /// <summary>Begins (or restarts) periodic ticking every <paramref name="intervalMs"/> milliseconds.</summary>
    void Start(int intervalMs);

    /// <summary>Stops ticking. The peer stays usable and can be started again.</summary>
    void Stop();

    /// <summary>Raised on the UI thread once per elapsed interval.</summary>
    event EventHandler? Tick;
}
