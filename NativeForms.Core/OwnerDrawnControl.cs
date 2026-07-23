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
    private IPlatformBackend? _themeSource;

    /// <summary>The native theme to paint with; the fallback until the control is realized.</summary>
    protected ITheme Theme { get; private set; } = DefaultTheme.Instance;

    /// <summary>Owner-drawn surfaces take no focus by default; interactive controls override.</summary>
    protected override bool Focusable => false;

    /// <summary>Whether a mouse-button press on this control is in flight. Set by the input pipeline
    /// before click-to-focus transfers focus and cleared on release, so focus-arrival handlers can
    /// distinguish a click from keyboard navigation.</summary>
    private protected bool IsMousePressInFlight { get; private set; }

    /// <summary>Requests a full repaint of the canvas surface.</summary>
    public override void Invalidate() => _canvas?.InvalidateAll();

    /// <summary>Requests a repaint of a sub-region of the canvas surface.</summary>
    public override void Invalidate(Rectangle region) => _canvas?.Invalidate(region);

    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        this.Theme = backend.Theme;
        var canvas = backend.CreateCanvas();
        _canvas = canvas;
        _themeSource = backend;
        backend.ThemeChanged += this.OnBackendThemeChanged;
        return canvas;
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        if (_themeSource is { } backend)
        {
            backend.ThemeChanged -= this.OnBackendThemeChanged;
            _themeSource = null;
        }

        _canvas = null;
    }

    /// <summary>
    /// The single image a subclass wants animated, or <see langword="null"/>. A control that carries one
    /// image property overrides this so the base keeps the shared <see cref="AnimationClock"/>
    /// subscription in step with both the image and the control's realized lifetime — the wiring that
    /// lets an <see cref="AnimatedImage"/> assigned to a plain <see cref="IImage"/> property animate.
    /// </summary>
    private protected virtual IImage? AnimatedImageSlot => null;

    /// <summary>
    /// (Re)subscribes <see cref="AnimatedImageSlot"/> to the shared animation clock when it is an
    /// animated image and the control is realized, and unsubscribes otherwise. Call it from the image
    /// property's setter; the base also calls it on realization so a subscription survives an image set
    /// before the control had a backend.
    /// </summary>
    private protected void UpdateImageAnimation() => this.TrackImageAnimation(this.AnimatedImageSlot, this.Invalidate);

    /// <summary>The desktop theme changed: adopt the backend's fresh snapshot and repaint.</summary>
    private void OnBackendThemeChanged(object? sender, EventArgs e)
    {
        if (_themeSource is not { } backend)
            return;

        this.Theme = backend.Theme;
        this.Invalidate();
    }

    /// <summary>Repaints when the effective font, colors or padding change — set directly or inherited.</summary>
    private protected override void OnAppearanceChanged() => this.Invalidate();

    /// <summary>Mirrors the canvas's pointer moves for components (tool tips) that observe a control
    /// without subclassing it. Raised after the control's own <see cref="OnMouseMove"/>.</summary>
    internal event EventHandler<MouseEventArgs>? CanvasMouseMove;

    /// <summary>Mirrors the canvas's mouse-leave for observing components.</summary>
    internal event EventHandler? CanvasMouseLeave;

    /// <summary>Mirrors the canvas's button presses for observing components.</summary>
    internal event EventHandler<MouseEventArgs>? CanvasMouseDown;

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not ICanvasPeer canvas)
            return;

        // Input is gated once, here, on the effective Enabled — a disabled control (or one inside a
        // disabled ancestor) receives no mouse or key input at all, the Windows Forms contract, so
        // no subclass needs its own Enabled guard.
        canvas.SetFocusable(this.Focusable);

        // Painting is clipped to the client rectangle before the subclass gets a say, so no
        // OnPaint can bleed past Width/Height onto a sibling — the Windows Forms contract, and the
        // one guarantee that holds on every backend rather than depending on whether the native
        // surface happens to clip. Pushing the clip here rather than in each OnPaint keeps it
        // impossible to forget, and costs no per-frame allocation (both rectangles are structs).
        canvas.Paint += (_, e) =>
        {
            var graphics = e.Graphics;
            graphics.PushClip(new Rectangle(0, 0, this.Width, this.Height));
            try
            {
                this.OnPaint(e);
            }
            finally
            {
                graphics.PopClip();
            }
        };
        canvas.MouseDown += (_, e) =>
        {
            if (!this.Enabled)
                return;

            // The press is recorded before click-to-focus transfers focus, so focus-arrival logic
            // (the radio auto-check) can tell a click apart from keyboard arrival — the same signal
            // Windows Forms reads from the static MouseButtons state during WM_SETFOCUS.
            this.IsMousePressInFlight = true;

            // Clicking a focusable control focuses it before the handler runs, like WM_MOUSEACTIVATE.
            if (this.Focusable)
                this.Focus();

            this.OnMouseDown(e);
            this.CanvasMouseDown?.Invoke(this, e);
            this.RaiseMouseDown(e); // also detects and raises DoubleClick/MouseDoubleClick
            if (e.Button == MouseButtons.Right && this.ContextMenuStrip is { } menu)
                menu.Show(this, e.Location);
        };
        canvas.MouseUp += (_, e) =>
        {
            // The physical press ended no matter how the event routes below.
            this.IsMousePressInFlight = false;

            // A drag in flight consumes the source's mouse stream — matching how an OS drag steals
            // the pointer from the source window.
            if (DragDropSession.RouteMouseUp(this, e))
                return;

            if (!this.Enabled)
                return;

            this.OnMouseUp(e);
            this.RaiseMouseUp(e);
        };
        canvas.MouseMove += (_, e) =>
        {
            if (DragDropSession.RouteMouseMove(this, e))
                return;

            if (!this.Enabled)
                return;

            this.OnMouseMove(e);
            this.CanvasMouseMove?.Invoke(this, e);
        };
        canvas.MouseWheel += (_, e) =>
        {
            if (!this.Enabled)
                return;

            this.OnMouseWheel(e);
            this.RaiseMouseWheel(e);
        };
        canvas.MouseLeave += (_, _) =>
        {
            // Deliberately not gated: a control disabled mid-hover must still clear its hot state.
            this.OnMouseLeave(EventArgs.Empty);
            this.CanvasMouseLeave?.Invoke(this, EventArgs.Empty);
        };
        canvas.KeyDown += (_, e) =>
        {
            if (!this.Enabled)
                return;

            // The form's dialog-key chain previews every key ahead of the control — Tab navigation,
            // Enter/Escape routing, menu shortcuts and Alt+mnemonics — unless IsInputKey claims it.
            if (this.FindForm() is { } form && form.ProcessDialogKey(this, e))
            {
                e.Handled = true;
                return;
            }

            this.OnKeyDown(e);
        };
        canvas.KeyUp += (_, e) =>
        {
            if (this.Enabled)
                this.OnKeyUp(e);
        };
        canvas.KeyPress += (_, e) =>
        {
            if (this.Enabled)
                this.OnKeyPress(e);
        };

        // A backend now exists, so an animated image assigned before realization can finally subscribe.
        this.UpdateImageAnimation();
    }

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
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
}
