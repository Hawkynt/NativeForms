using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>Carries the drawing surface and clip region to a paint handler. Valid only for the
/// duration of that paint: canvas peers reuse one instance across frames (via <see cref="Reset"/>)
/// so a steady-state repaint allocates nothing.</summary>
public sealed class PaintEventArgs(IGraphics graphics, Rectangle clipRectangle) : EventArgs
{
    /// <summary>The surface to draw on.</summary>
    public IGraphics Graphics { get; private set; } = graphics;

    /// <summary>The region that needs repainting; drawing may be clipped to it.</summary>
    public Rectangle ClipRectangle { get; private set; } = clipRectangle;

    /// <summary>Rebinds the instance to the next paint's surface and clip — the peer-side reuse hook.</summary>
    internal void Reset(IGraphics graphics, Rectangle clipRectangle)
    {
        this.Graphics = graphics;
        this.ClipRectangle = clipRectangle;
    }
}

/// <summary>Describes a mouse event, matching <c>System.Windows.Forms.MouseEventArgs</c> plus the
/// modifier keys held at the time — multi-selection gestures (Ctrl/Shift+click) and
/// Shift+wheel scrolling need them.</summary>
public sealed class MouseEventArgs(MouseButtons button, int x, int y, int delta, KeyModifiers modifiers = KeyModifiers.None) : EventArgs
{
    /// <summary>The button that changed state (or <see cref="MouseButtons.None"/> for moves).</summary>
    public MouseButtons Button { get; } = button;

    /// <summary>The keyboard modifier flags held while the event happened.</summary>
    public KeyModifiers Modifiers { get; } = modifiers;

    /// <summary>The x-coordinate in the control's client space.</summary>
    public int X { get; } = x;

    /// <summary>The y-coordinate in the control's client space.</summary>
    public int Y { get; } = y;

    /// <summary>The signed wheel delta (0 for non-wheel events).</summary>
    public int Delta { get; } = delta;

    /// <summary>Whether Shift was held.</summary>
    public bool Shift => (this.Modifiers & KeyModifiers.Shift) != 0;

    /// <summary>Whether Control was held.</summary>
    public bool Control => (this.Modifiers & KeyModifiers.Control) != 0;

    /// <summary>Whether Alt was held.</summary>
    public bool Alt => (this.Modifiers & KeyModifiers.Alt) != 0;

    /// <summary>The pointer location.</summary>
    public Point Location => new(this.X, this.Y);
}

/// <summary>Describes a key-down/up event.</summary>
public sealed class KeyEventArgs(Keys keyCode, KeyModifiers modifiers) : EventArgs
{
    /// <summary>The virtual key.</summary>
    public Keys KeyCode { get; } = keyCode;

    /// <summary>The active modifier flags.</summary>
    public KeyModifiers Modifiers { get; } = modifiers;

    /// <summary>Whether Shift was held.</summary>
    public bool Shift => (this.Modifiers & KeyModifiers.Shift) != 0;

    /// <summary>Whether Control was held.</summary>
    public bool Control => (this.Modifiers & KeyModifiers.Control) != 0;

    /// <summary>Whether Alt was held.</summary>
    public bool Alt => (this.Modifiers & KeyModifiers.Alt) != 0;

    /// <summary>The key code combined with the active modifier bits — the shape shortcut chords
    /// (<c>Keys.Control | Keys.S</c>) are declared and compared in.</summary>
    public Keys KeyData
        => this.KeyCode
           | (this.Shift ? Keys.Shift : Keys.None)
           | (this.Control ? Keys.Control : Keys.None)
           | (this.Alt ? Keys.Alt : Keys.None);

    /// <summary>Set by a handler to indicate the key was consumed.</summary>
    public bool Handled { get; set; }
}

/// <summary>Carries the text of an activated inline link — an auto-detected URL in a <see cref="RichTextBox"/>.</summary>
public sealed class LinkClickedEventArgs(string linkText) : EventArgs
{
    /// <summary>The link's text as it appears in the document (for URLs, the URL itself).</summary>
    public string LinkText { get; } = linkText;
}

/// <summary>Describes a character typed, matching <c>System.Windows.Forms.KeyPressEventArgs</c>.</summary>
public sealed class KeyPressEventArgs(char keyChar) : EventArgs
{
    /// <summary>The character produced.</summary>
    public char KeyChar { get; } = keyChar;

    /// <summary>Set by a handler to indicate the character was consumed.</summary>
    public bool Handled { get; set; }
}
