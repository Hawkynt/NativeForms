using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for controls the toolkit paints itself (everything the platform has no native widget
/// for, plus anything we want pixel-identical across platforms). It realizes onto a single
/// <see cref="ICanvasPeer"/> and turns that surface's paint and input events into the familiar
/// <see cref="OnPaint"/>/<see cref="OnMouseDown"/>/… overrides. A subclass draws through
/// <see cref="PaintEventArgs.Graphics"/> using <see cref="Theme"/> so it matches the host desktop.
/// </summary>
public abstract class OwnerDrawnControl : Control
{
    private ICanvasPeer? _canvas;

    /// <summary>The native theme to paint with; the fallback until the control is realized.</summary>
    protected ITheme Theme { get; private set; } = DefaultTheme.Instance;

    /// <summary>Whether the control can take keyboard focus. Overridden by interactive controls.</summary>
    protected virtual bool Focusable => false;

    /// <summary>Requests a full repaint.</summary>
    public void Invalidate() => _canvas?.InvalidateAll();

    /// <summary>Requests a repaint of a sub-region.</summary>
    public void Invalidate(Rectangle region) => _canvas?.Invalidate(region);

    /// <summary>Moves keyboard focus to this control.</summary>
    public void Focus() => _canvas?.Focus();

    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        this.Theme = backend.Theme;
        var canvas = backend.CreateCanvas();
        _canvas = canvas;
        return canvas;
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized() => _canvas = null;

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not ICanvasPeer canvas)
            return;

        canvas.SetFocusable(this.Focusable);
        canvas.Paint += (_, e) => this.OnPaint(e);
        canvas.MouseDown += (_, e) => this.OnMouseDown(e);
        canvas.MouseUp += (_, e) => this.OnMouseUp(e);
        canvas.MouseMove += (_, e) => this.OnMouseMove(e);
        canvas.MouseWheel += (_, e) => this.OnMouseWheel(e);
        canvas.MouseLeave += (_, _) => this.OnMouseLeave(EventArgs.Empty);
        canvas.KeyDown += (_, e) => this.OnKeyDown(e);
        canvas.KeyUp += (_, e) => this.OnKeyUp(e);
        canvas.KeyPress += (_, e) => this.OnKeyPress(e);
        canvas.GotFocus += (_, _) => this.OnGotFocus(EventArgs.Empty);
        canvas.LostFocus += (_, _) => this.OnLostFocus(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.Invalidate();
    }

    /// <summary>Paints the control. Override to draw through <see cref="PaintEventArgs.Graphics"/>.</summary>
    protected virtual void OnPaint(PaintEventArgs e) { }

    /// <summary>Handles a mouse-button press.</summary>
    protected virtual void OnMouseDown(MouseEventArgs e) { }

    /// <summary>Handles a mouse-button release.</summary>
    protected virtual void OnMouseUp(MouseEventArgs e) { }

    /// <summary>Handles pointer movement.</summary>
    protected virtual void OnMouseMove(MouseEventArgs e) { }

    /// <summary>Handles a mouse-wheel turn.</summary>
    protected virtual void OnMouseWheel(MouseEventArgs e) { }

    /// <summary>Handles the pointer leaving the control.</summary>
    protected virtual void OnMouseLeave(EventArgs e) { }

    /// <summary>Handles a key press (down).</summary>
    protected virtual void OnKeyDown(KeyEventArgs e) { }

    /// <summary>Handles a key release.</summary>
    protected virtual void OnKeyUp(KeyEventArgs e) { }

    /// <summary>Handles a typed character.</summary>
    protected virtual void OnKeyPress(KeyPressEventArgs e) { }

    /// <summary>Handles gaining focus.</summary>
    protected virtual void OnGotFocus(EventArgs e) { }

    /// <summary>Handles losing focus.</summary>
    protected virtual void OnLostFocus(EventArgs e) { }
}
