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

/// <summary>
/// A menu-bar item: a real <c>NSStatusItem</c> in the system status bar, which is what macOS has where
/// Windows has a notification-area icon.
/// </summary>
/// <remarks>
/// <para>
/// The item is taken from the shared status bar at construction rather than when it is first shown,
/// because the button behind it is what carries the icon, the tooltip and the target, and there is
/// nothing to buffer state into until it exists. Visibility is then the item's own flag — which is
/// also the only honest way to hide one: an item removed from the bar cannot be put back in the place
/// it had.
/// </para>
/// <para>
/// One press produces one action, so the two events are told apart by the click count on the event
/// that caused it. That is the same thing the shell does on Windows: a double click arrives as a
/// click and then a double click, and an application that listens to both hears both.
/// </para>
/// </remarks>
internal sealed class CocoaNotifyIconPeer : INotifyIconPeer
{
    /// <summary>NSVariableStatusItemLength: the item is as wide as what it shows.</summary>
    private const double _Variable = -1;

    private readonly nint _item;
    private readonly nint _target;
    private nint _image;

    public CocoaNotifyIconPeer()
    {
        // A status item needs the application to own a slice of the menu bar, and a process launched
        // from a terminal is NSApplicationActivationPolicyProhibited until something says otherwise.
        // The loop says so when it starts, which is after the interface has been built — and a tray
        // icon is one of the things an application builds first. Asked for here, the item has a menu
        // bar to go in; left until Run, it is created into a process that has none and quietly has no
        // window at all.
        var app = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        if (app != 0 && CocoaRuntime.SendInteger(app, CocoaRuntime.sel_registerName("activationPolicy")) != 0)
            CocoaRuntime.SendBool(app, CocoaRuntime.sel_registerName("setActivationPolicy:"), 0);

        var bar = CocoaRuntime.SendToClass("NSStatusBar", "systemStatusBar");
        _item = bar == 0
            ? 0
            : CocoaRuntime.SendLength(bar, CocoaRuntime.sel_registerName("statusItemWithLength:"), _Variable);

        if (_item == 0)
            return;

        // Retained: the status bar hands back an autoreleased item, and this one has to survive the
        // pool that is drained at the end of whatever created it.
        CocoaRuntime.SendPointer(_item, CocoaRuntime.sel_registerName("retain"));

        // Hidden until the core says otherwise, so a component that is built and never shown does not
        // put an icon in the user's menu bar.
        CocoaRuntime.SendVoid(_item, CocoaRuntime.sel_registerName("setVisible:"), false);

        if (this.Button() is not { } button)
            return;

        _target = CocoaAction.Create(this.OnPressed);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    /// <inheritdoc/>
    public event EventHandler? Click;

    /// <inheritdoc/>
    public event EventHandler? DoubleClick;

    /// <summary>The item's button, which is where everything visible about it lives.</summary>
    private nint? Button()
    {
        if (_item == 0)
            return null;

        var button = CocoaRuntime.SendPointer(_item, CocoaRuntime.sel_registerName("button"));
        return button == 0 ? null : button;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not marked as a template image. A template is drawn as a monochrome stencil so it follows the
    /// menu bar's appearance, which is what a system icon wants — but the core hands over an
    /// application's own colours, and reducing them to a silhouette would throw away what the caller
    /// chose without being asked.
    /// </remarks>
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        if (this.Button() is not { } button)
            return;

        var image = CocoaImage.CreateNSImage(width, height, argb);
        if (image == 0)
            return;

        CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setImage:"), image);

        // The button retains what it is given, so the previous one is released only once it is no
        // longer the one on screen.
        if (_image != 0)
            CocoaRuntime.SendVoid(_image, CocoaRuntime.sel_registerName("release"));

        _image = image;
    }

    /// <inheritdoc/>
    public void SetToolTip(string text)
    {
        if (this.Button() is not { } button)
            return;

        var value = CocoaRuntime.NSString(text);
        if (value == 0)
            return;

        CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setToolTip:"), value);
        CocoaNative.CFRelease(value);
    }

    /// <inheritdoc/>
    public void SetVisible(bool visible)
    {
        if (_item != 0)
            CocoaRuntime.SendVoid(_item, CocoaRuntime.sel_registerName("setVisible:"), visible);
    }

    /// <summary>The button was pressed; which event that is depends on how many clicks it took.</summary>
    private void OnPressed()
    {
        var app = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        var current = app == 0 ? 0 : CocoaRuntime.SendPointer(app, CocoaRuntime.sel_registerName("currentEvent"));
        var clicks = current == 0 ? 1 : (int)CocoaRuntime.SendInteger(current, CocoaRuntime.sel_registerName("clickCount"));

        if (clicks >= 2)
            DoubleClick?.Invoke(this, EventArgs.Empty);
        else
            Click?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CocoaAction.Forget(_target);
        if (_image != 0)
            CocoaRuntime.SendVoid(_image, CocoaRuntime.sel_registerName("release"));

        if (_item == 0)
            return;

        var bar = CocoaRuntime.SendToClass("NSStatusBar", "systemStatusBar");
        if (bar != 0)
            CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("removeStatusItem:"), _item);

        CocoaRuntime.SendVoid(_item, CocoaRuntime.sel_registerName("release"));
    }
}
