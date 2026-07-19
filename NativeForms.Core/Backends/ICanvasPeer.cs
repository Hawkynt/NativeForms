using System.Drawing;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// The native side of an owner-drawn control: a single focusable, paintable surface. Every custom
/// control in the toolkit is realized onto one of these, so a backend implements drawing and input
/// exactly once (in its canvas peer) rather than once per control. The peer raises paint and input
/// events; the managed control responds by drawing through the supplied <see cref="Drawing.IGraphics"/>.
/// </summary>
public interface ICanvasPeer : IControlPeer
{
    /// <summary>Raised when a region needs repainting.</summary>
    event EventHandler<PaintEventArgs>? Paint;

    /// <summary>Raised when a mouse button goes down over the surface.</summary>
    event EventHandler<MouseEventArgs>? MouseDown;

    /// <summary>Raised when a mouse button is released.</summary>
    event EventHandler<MouseEventArgs>? MouseUp;

    /// <summary>Raised when the pointer moves over the surface.</summary>
    event EventHandler<MouseEventArgs>? MouseMove;

    /// <summary>Raised when the mouse wheel turns over the surface.</summary>
    event EventHandler<MouseEventArgs>? MouseWheel;

    /// <summary>Raised when the pointer leaves the surface.</summary>
    event EventHandler? MouseLeave;

    /// <summary>Raised when a key goes down while focused.</summary>
    event EventHandler<KeyEventArgs>? KeyDown;

    /// <summary>Raised when a key is released while focused.</summary>
    event EventHandler<KeyEventArgs>? KeyUp;

    /// <summary>Raised when a character is typed while focused.</summary>
    event EventHandler<KeyPressEventArgs>? KeyPress;

    /// <summary>Raised when the surface gains keyboard focus.</summary>
    event EventHandler? GotFocus;

    /// <summary>Raised when the surface loses keyboard focus.</summary>
    event EventHandler? LostFocus;

    /// <summary>Requests a repaint of the given client-space region.</summary>
    void Invalidate(Rectangle bounds);

    /// <summary>Requests a repaint of the whole surface.</summary>
    void InvalidateAll();

    /// <summary>Moves keyboard focus to this surface.</summary>
    void Focus();

    /// <summary>Sets whether the surface can receive keyboard focus.</summary>
    void SetFocusable(bool focusable);
}
