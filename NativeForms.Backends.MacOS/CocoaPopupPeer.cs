using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A borderless floating surface — the menus, drop-downs and tooltips the owner-draw engine puts up —
/// as an <c>NSWindow</c> with no chrome hosting a canvas.
/// </summary>
/// <remarks>
/// Light dismissal is not wired yet: that needs an event monitor watching for clicks outside the
/// surface, which belongs with the rest of AppKit event routing. Until then a popup opens and is
/// closed by whoever opened it, which is how every backend behaves for the surfaces that take no grab.
/// </remarks>
internal sealed class CocoaPopupPeer : IPopupPeer
{
    private readonly CocoaCanvasPeer _canvas = new();
    private readonly nint _window;
    private Rectangle _bounds;

    public CocoaPopupPeer()
    {
        var allocated = CocoaRuntime.Allocate("NSWindow");
        _window = allocated == 0
            ? 0
            : CocoaRuntime.SendRect(
                allocated,
                CocoaRuntime.sel_registerName("initWithContentRect:styleMask:backing:defer:"),
                new(0, 0, 1, 1),
                0,   // NSWindowStyleMaskBorderless
                2,   // NSBackingStoreBuffered
                false);

        if (_window != 0 && _canvas.Handle != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setContentView:"), _canvas.Handle);
    }

    public bool LightDismiss { get; set; } = true;
    public Func<Point, bool>? OutsidePress { get; set; }
    public Action<Point>? OutsidePointerMove { get; set; }

    public event EventHandler<PaintEventArgs>? Paint
    {
        add => _canvas.Paint += value;
        remove => _canvas.Paint -= value;
    }

    public event EventHandler<MouseEventArgs>? MouseDown
    {
        add => _canvas.MouseDown += value;
        remove => _canvas.MouseDown -= value;
    }

    public event EventHandler<MouseEventArgs>? MouseUp
    {
        add => _canvas.MouseUp += value;
        remove => _canvas.MouseUp -= value;
    }

    public event EventHandler<MouseEventArgs>? MouseMove
    {
        add => _canvas.MouseMove += value;
        remove => _canvas.MouseMove -= value;
    }

    public event EventHandler<MouseEventArgs>? MouseWheel
    {
        add => _canvas.MouseWheel += value;
        remove => _canvas.MouseWheel -= value;
    }

    public event EventHandler? MouseLeave
    {
        add => _canvas.MouseLeave += value;
        remove => _canvas.MouseLeave -= value;
    }

    public event EventHandler<KeyEventArgs>? KeyDown
    {
        add => _canvas.KeyDown += value;
        remove => _canvas.KeyDown -= value;
    }

    public event EventHandler<KeyEventArgs>? KeyUp
    {
        add => _canvas.KeyUp += value;
        remove => _canvas.KeyUp -= value;
    }

    public event EventHandler<KeyPressEventArgs>? KeyPress
    {
        add => _canvas.KeyPress += value;
        remove => _canvas.KeyPress -= value;
    }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<MouseEventArgs>? PointerMove;
    public event EventHandler? PointerLeave;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
    public event EventHandler? Dismissed;

    public void ShowAt(Point screenLocation, Size size)
    {
        _bounds = new(screenLocation, size);
        _canvas.SetBounds(new(0, 0, size.Width, size.Height));
        if (_window == 0)
            return;

        var height = CocoaNative.CGDisplayPixelsHigh(CocoaNative.CGMainDisplayID());
        CocoaRuntime.SendRectVoid(
            _window,
            CocoaRuntime.sel_registerName("setFrame:display:"),
            new(screenLocation.X, height - screenLocation.Y - size.Height, size.Width, size.Height),
            true);

        CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("orderFront:"), 0);
    }

    public void Resize(Size size) => this.ShowAt(_bounds.Location, size);

    public void Hide()
    {
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("orderOut:"), 0);
    }

    public void Invalidate(Rectangle bounds) => _canvas.Invalidate(bounds);

    public void InvalidateAll() => _canvas.InvalidateAll();

    public void SetBounds(Rectangle bounds) => this.ShowAt(bounds.Location, bounds.Size);

    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    public void AddChild(IControlPeer child) => _canvas.AddChild(child);

    public void RemoveChild(IControlPeer child) => _canvas.RemoveChild(child);

    public void SetVisible(bool visible)
    {
        if (visible)
            this.ShowAt(_bounds.Location, _bounds.Size);
        else
            this.Hide();
    }

    public void SetText(string text) { }

    public void SetEnabled(bool enabled) { }

    public void SetFont(Drawing.Font font) { }

    public void SetColors(Color foreColor, Color backColor) { }

    public void SetCursor(Cursor cursor) { }

    public void Focus() { }

    public void ShowToolTip(string? text) { }

    public void SetFocusable(bool focusable) { }

    public void Dispose()
    {
        this.Hide();
        _canvas.Dispose();
    }

    /// <summary>Keeps the surface events referenced until AppKit's routing feeds them.</summary>
    private void Unused()
    {
        GotFocus?.Invoke(this, EventArgs.Empty);
        LostFocus?.Invoke(this, EventArgs.Empty);
        PointerMove?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        PointerLeave?.Invoke(this, EventArgs.Empty);
        ContextMenuRequested?.Invoke(this, new(Point.Empty));
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A status-bar item. Inert until <c>NSStatusItem</c> is wired.</summary>
internal sealed class CocoaNotifyIconPeer : INotifyIconPeer
{
    public event EventHandler? Click;
    public event EventHandler? DoubleClick;

    public void SetIcon(int width, int height, ReadOnlySpan<int> argb) { }

    public void SetToolTip(string text) { }

    public void SetVisible(bool visible) { }

    public void Dispose() { }

    private void Unused()
    {
        Click?.Invoke(this, EventArgs.Empty);
        DoubleClick?.Invoke(this, EventArgs.Empty);
    }
}
