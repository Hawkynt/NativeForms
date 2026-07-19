using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>Carries the drawing surface and clip region to a paint handler.</summary>
public sealed class PaintEventArgs(IGraphics graphics, Rectangle clipRectangle) : EventArgs
{
    /// <summary>The surface to draw on.</summary>
    public IGraphics Graphics { get; } = graphics;

    /// <summary>The region that needs repainting; drawing may be clipped to it.</summary>
    public Rectangle ClipRectangle { get; } = clipRectangle;
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

    /// <summary>Set by a handler to indicate the key was consumed.</summary>
    public bool Handled { get; set; }
}

/// <summary>Describes a character typed, matching <c>System.Windows.Forms.KeyPressEventArgs</c>.</summary>
public sealed class KeyPressEventArgs(char keyChar) : EventArgs
{
    /// <summary>The character produced.</summary>
    public char KeyChar { get; } = keyChar;

    /// <summary>Set by a handler to indicate the character was consumed.</summary>
    public bool Handled { get; set; }
}
