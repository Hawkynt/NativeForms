using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A borderless floating surface — the menus, drop-downs and tooltips the owner-draw engine puts up —
/// as an <c>NSWindow</c> with no chrome hosting a canvas.
/// </summary>
/// <remarks>
/// <para>
/// Light dismissal is armed from the application's own event loop rather than from a grab. AppKit's
/// answer to "see this press before anything else does" is
/// <c>addLocalMonitorForEventsMatchingMask:handler:</c>, which takes an Objective-C block — an object
/// with a calling convention, and precisely what the interop rules exist to keep out of this
/// assembly. <see cref="CocoaBackend.Run"/> already pulls every event before dispatching it, so a
/// monitor would only be a second way of standing where the loop already stands.
/// </para>
/// <para>
/// There is no pointer grab, so the surface never sees a press aimed at another window; the loop
/// decides instead, by asking which window the event was dispatched to. The deepest surface is the
/// one that answers, which is the same rule the capture-holding Win32 peer follows and what lets a
/// menu cascade route a click on a shallower level through <see cref="OutsidePress"/> instead of
/// tearing itself down.
/// </para>
/// </remarks>
internal sealed class CocoaPopupPeer : IPopupPeer
{
    /// <summary>
    /// Every surface currently up that wants light dismiss, deepest last. A passive surface — a
    /// tooltip — never joins, because it must not consume the click the user aimed past it.
    /// </summary>
    private static readonly List<CocoaPopupPeer> _open = [];

    private readonly CocoaCanvasPeer _canvas = new();
    private readonly nint _window;
    private Rectangle _bounds;
    private bool _shown;

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

        if (_window == 0)
            return;

        // A menu is read by hovering it, and a window drops mouse-moved events until it is told not
        // to. This one is never the key window either, which is why the canvas asks for its tracking
        // area with NSTrackingActiveAlways.
        CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setAcceptsMouseMovedEvents:"), true);

        if (_canvas.Handle != 0)
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
        _shown = true;
        if (this.LightDismiss && !_open.Contains(this))
            _open.Add(this);
    }

    public void Resize(Size size) => this.ShowAt(_bounds.Location, size);

    public void Hide()
    {
        // Dropped first: the loop must not offer a press to a surface that is already on its way out.
        _shown = false;
        _open.Remove(this);
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("orderOut:"), 0);
    }

    /// <summary>
    /// Offers an event to the open surfaces before AppKit dispatches it, answering whether it was
    /// consumed.
    /// </summary>
    /// <remarks>
    /// A press that dismisses is swallowed, exactly as the Win32 capture swallows one: the click that
    /// closes a menu belongs to the menu, and letting it through as well would close the menu and press
    /// whatever happened to be underneath it in the same gesture.
    /// </remarks>
    internal static bool Intercept(nint theEvent)
    {
        if (theEvent == 0 || _open.Count == 0)
            return false;

        var deepest = _open[^1];
        var type = (int)CocoaRuntime.SendInteger(theEvent, CocoaRuntime.sel_registerName("type"));

        // NSEventTypeKeyDown, and 0x35 is Escape — which closes the deepest surface wherever it is
        // typed, because a popup here is never the key window and would otherwise never hear it.
        if (type == 10)
            return CocoaRuntime.SendUShort(theEvent, CocoaRuntime.sel_registerName("keyCode")) == 0x35 && deepest.Dismiss();

        // NSEventTypeLeftMouseDown, RightMouseDown, OtherMouseDown.
        if (type is not (1 or 3 or 25))
            return false;

        var window = CocoaRuntime.SendPointer(theEvent, CocoaRuntime.sel_registerName("window"));
        if (window != 0 && window == deepest._window)
            return false;

        // Offered to the owner in screen space first: a menu recognizes a click on a shallower level of
        // its own cascade there and routes it, rather than the whole menu coming down on a click that
        // was aimed at part of it.
        return deepest.OutsidePress?.Invoke(ScreenPointOf(theEvent, window)) == true || deepest.Dismiss();
    }

    /// <summary>Where an event landed, in the toolkit's screen coordinates.</summary>
    private static Point ScreenPointOf(nint theEvent, nint window)
    {
        var local = CocoaRuntime.SendPoint(theEvent, CocoaRuntime.sel_registerName("locationInWindow"));

        // An event with no window carries screen coordinates already.
        var screen = window == 0
            ? local
            : CocoaRuntime.SendPoint(window, CocoaRuntime.sel_registerName("convertPointToScreen:"), local);

        var height = CocoaNative.CGDisplayPixelsHigh(CocoaNative.CGMainDisplayID());
        return new((int)screen.X, (int)(height - screen.Y));
    }

    /// <summary>Hides the surface and raises <see cref="Dismissed"/>; answers whether it did.</summary>
    private bool Dismiss()
    {
        if (!_shown)
            return false;

        this.Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Invalidate(Rectangle bounds) => _canvas.Invalidate(bounds);

    public void InvalidateAll() => _canvas.InvalidateAll();

    public void SetBounds(Rectangle bounds) => this.ShowAt(bounds.Location, bounds.Size);

    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    /// <inheritdoc/>
    /// <remarks>The surface is the canvas; the window around it is chrome with nothing in it to read.</remarks>
    public void SetAccessibleInfo(string? name, string? description, AccessibleRole role)
        => _canvas.SetAccessibleInfo(name, description, role);

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
