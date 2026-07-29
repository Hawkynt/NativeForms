using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The owner-draw surface: an <c>NSView</c> subclass built at run time whose <c>drawRect:</c> calls
/// back into managed code.
/// </summary>
/// <remarks>
/// <para>
/// AppKit has no way to be told "call this function when you need painting" — it calls a method on a
/// class. So the class is created with <c>objc_allocateClassPair</c> and given a <c>drawRect:</c>
/// whose implementation is an <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/>
/// static passed as a function pointer, which is the same shape the Win32 window procedure and the GTK
/// draw signal already use, and keeps §2's rule against marshalled delegates intact.
/// </para>
/// <para>
/// The view is registered once per process, not once per canvas: registering a class pair twice under
/// the same name fails, and a gallery has dozens of canvases.
/// </para>
/// </remarks>
internal sealed unsafe class CocoaCanvasPeer : ICanvasPeer
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _viewClass;

    /// <summary>Live canvases by view pointer, so the static callback can find the one being painted.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, CocoaCanvasPeer> _canvases = new();

    private readonly nint _view;
    private Rectangle _bounds;

    public CocoaCanvasPeer()
    {
        EnsureViewClass();
        var allocated = _viewClass == 0 ? 0 : CocoaRuntime.SendPointer(_viewClass, CocoaRuntime.sel_registerName("alloc"));
        _view = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (_view == 0)
            return;

        _canvases[_view] = this;
        InstallTrackingArea(_view);
    }

    /// <summary>
    /// Asks AppKit to route the pointer's movement over this view to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hover has to be asked for twice on AppKit and neither half works alone, which is why nothing
    /// highlighted under the pointer while presses, drags and the wheel all arrived: a window drops
    /// mouse-moved events unless <c>setAcceptsMouseMovedEvents:</c> turns them on (the window peers do
    /// that), and even then it sends <c>mouseMoved:</c> to its first responder rather than to the view
    /// under the pointer. A tracking area is what makes the view under the pointer the one that hears
    /// it — and it is also what produces <c>mouseEntered:</c> and <c>mouseExited:</c>, without which a
    /// highlight would light up and never go out.
    /// </para>
    /// <para>
    /// <c>NSTrackingInVisibleRect</c> because the rectangle would otherwise be a snapshot: the view is
    /// created at 1×1 and given its real frame later, and every layout after that moves it again.
    /// AppKit keeps an in-visible-rect area glued to the view's visible bounds itself, so the hover
    /// region cannot drift away from the control the way a rectangle passed once would.
    /// <c>NSTrackingActiveAlways</c> because a menu surface is never the key window, and hover is the
    /// whole of how a menu is read.
    /// </para>
    /// </remarks>
    private static void InstallTrackingArea(nint view)
    {
        // NSTrackingMouseEnteredAndExited | NSTrackingMouseMoved | NSTrackingActiveAlways |
        // NSTrackingInVisibleRect.
        const nint options = 0x01 | 0x02 | 0x80 | 0x200;

        var allocated = CocoaRuntime.Allocate("NSTrackingArea");
        if (allocated == 0)
            return;

        var area = CocoaRuntime.SendTrackingArea(
            allocated,
            CocoaRuntime.sel_registerName("initWithRect:options:owner:userInfo:"),
            new(0, 0, 1, 1), // ignored: NSTrackingInVisibleRect substitutes the view's own visible rect
            options,
            view,
            0);

        if (area != 0)
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("addTrackingArea:"), area);
    }

    /// <summary>
    /// A bare view of the canvas class, for use where something needs the flipped coordinate system
    /// without a canvas behind it — a window's content view, which is otherwise a plain
    /// <c>NSView</c> and therefore bottom-up.
    /// </summary>
    internal static nint CreateFlippedView()
    {
        EnsureViewClass();
        if (_viewClass == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_viewClass, CocoaRuntime.sel_registerName("alloc"));
        return allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));
    }

    /// <summary>The view handle, so a container can add it to its own.</summary>
    internal nint Handle => _view;

    /// <summary>
    /// Whether a view is one this backend built, and therefore one that answers
    /// <see cref="ResetCursorRects"/> for itself.
    /// </summary>
    /// <remarks>
    /// What keeps the two cursor routes disjoint. A group box's host is one of these while its peer is
    /// a control peer, so without the question it would be given a cursor rectangle and a tracking
    /// area both saying the same thing.
    /// </remarks>
    internal static bool IsOwnView(nint view)
        => view != 0 && _viewClass != 0 && CocoaRuntime.object_getClass(view) == _viewClass;

    public event EventHandler<PaintEventArgs>? Paint;
    public event EventHandler<MouseEventArgs>? MouseDown;
    public event EventHandler<MouseEventArgs>? MouseUp;
    public event EventHandler<MouseEventArgs>? MouseMove;
    public event EventHandler<MouseEventArgs>? MouseWheel;
    public event EventHandler? MouseLeave;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;
    public event EventHandler<KeyPressEventArgs>? KeyPress;
    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<MouseEventArgs>? PointerMove;
    public event EventHandler? PointerLeave;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>Creates the <c>NSView</c> subclass once per process.</summary>
    private static void EnsureViewClass()
    {
        if (_viewClass != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSView");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsCanvas", 0);
        if (created == 0)
            return;

        // "v@:{CGRect={CGPoint=dd}{CGSize=dd}}": returns void, takes self, _cmd and a rectangle.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("drawRect:"),
            (nint)(delegate* unmanaged<nint, nint, CocoaRuntime.CGRect, void>)&DrawRect,
            "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");

        // A flipped view puts its origin at the top left with y growing down, which is the toolkit's
        // convention and Win32's and GTK's. Without it every child sits mirrored inside its parent —
        // laid out correctly and drawn in the wrong half, which reads as a layout bug rather than a
        // coordinate one.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("isFlipped"),
            (nint)(delegate* unmanaged<nint, nint, byte>)&IsFlipped,
            "c@:");

        // The input selectors. AppKit delivers each as a method on the view, so each is a function
        // pointer on the runtime class exactly as drawRect: is — the pattern, repeated, rather than a
        // second mechanism.
        Add(created, "mouseDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseDown);
        Add(created, "mouseUp:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseUp);
        Add(created, "mouseDragged:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseMoved);
        Add(created, "mouseMoved:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseMoved);

        // The tracking area's own pair. Entering is a move like any other, so it goes to the same
        // place; leaving is what puts a highlight out again, and without it the last cell the pointer
        // touched stays lit after the pointer has gone somewhere else entirely.
        Add(created, "mouseEntered:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseMoved);
        Add(created, "mouseExited:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMouseExited);
        Add(created, "rightMouseDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnRightMouseDown);
        Add(created, "scrollWheel:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScrollWheel);
        Add(created, "keyDown:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&KeyDownEvent);
        Add(created, "keyUp:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&KeyUpEvent);

        // The pointer's shape. AppKit has no "set the cursor on this view" message: a view declares
        // the rectangles it wants a shape over when asked to, and this is the asking.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("resetCursorRects"),
            (nint)(delegate* unmanaged<nint, nint, void>)&ResetCursorRects,
            "v@:");

        // Without this the view never becomes first responder and no key ever arrives.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("acceptsFirstResponder"),
            (nint)(delegate* unmanaged<nint, nint, byte>)&AcceptsFirstResponder,
            "c@:");

        CocoaRuntime.objc_registerClassPair(created);
        _viewClass = created;
    }

    /// <summary>Attaches one event method, whose encoded signature is "void, self, _cmd, object".</summary>
    private static void Add(nint cls, string selector, nint implementation)
        => CocoaRuntime.class_addMethod(cls, CocoaRuntime.sel_registerName(selector), implementation, "v@:@");

    /// <summary>Answers YES, so the view can take the keyboard.</summary>
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static byte AcceptsFirstResponder(nint self, nint selector) => 1;

    /// <summary>Where an event landed, in the view's own (flipped) coordinates.</summary>
    internal static Point LocationOf(nint view, nint theEvent)
    {
        var inWindow = CocoaRuntime.SendPoint(theEvent, CocoaRuntime.sel_registerName("locationInWindow"));
        var local = CocoaRuntime.SendConvert(view, CocoaRuntime.sel_registerName("convertPoint:fromView:"), inWindow, 0);
        return new((int)local.X, (int)local.Y);
    }

    /// <summary>The toolkit's modifier set for an event's AppKit flags.</summary>
    private static KeyModifiers ModifiersOf(nint theEvent)
    {
        var flags = CocoaRuntime.SendInteger(theEvent, CocoaRuntime.sel_registerName("modifierFlags"));
        var modifiers = KeyModifiers.None;
        if ((flags & (1 << 17)) != 0)
            modifiers |= KeyModifiers.Shift;

        // Command is mapped to Control, because it is the platform's own accelerator key and every
        // shortcut the toolkit defines reads as Control — mapping it to anything else would leave a
        // Mac user pressing the key that says Control on a keyboard that never uses it.
        if ((flags & ((1 << 18) | (1 << 20))) != 0)
            modifiers |= KeyModifiers.Control;
        if ((flags & (1 << 19)) != 0)
            modifiers |= KeyModifiers.Alt;

        return modifiers;
    }

    private static CocoaCanvasPeer? CanvasOf(nint self) => _canvases.TryGetValue(self, out var canvas) ? canvas : null;

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnMouseDown(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var at = LocationOf(self, theEvent);
        canvas.MouseDown?.Invoke(canvas, new(MouseButtons.Left, at.X, at.Y, 0, ModifiersOf(theEvent)));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnMouseUp(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var at = LocationOf(self, theEvent);
        canvas.MouseUp?.Invoke(canvas, new(MouseButtons.Left, at.X, at.Y, 0, ModifiersOf(theEvent)));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnMouseMoved(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var at = LocationOf(self, theEvent);
        canvas.MouseMove?.Invoke(canvas, new(MouseButtons.None, at.X, at.Y, 0, ModifiersOf(theEvent)));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnMouseExited(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is { } canvas)
            canvas.MouseLeave?.Invoke(canvas, EventArgs.Empty);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnRightMouseDown(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var at = LocationOf(self, theEvent);
        canvas.MouseDown?.Invoke(canvas, new(MouseButtons.Right, at.X, at.Y, 0, ModifiersOf(theEvent)));
        canvas.ContextMenuRequested?.Invoke(canvas, new(at));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnScrollWheel(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var at = LocationOf(self, theEvent);
        // AppKit reports the wheel in points; the toolkit counts notches of 120 like every other backend.
        var delta = (int)(CocoaRuntime.SendDouble(theEvent, CocoaRuntime.sel_registerName("scrollingDeltaY")) * 120);
        canvas.MouseWheel?.Invoke(canvas, new(MouseButtons.None, at.X, at.Y, delta, ModifiersOf(theEvent)));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void KeyDownEvent(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is not { } canvas)
            return;

        var modifiers = ModifiersOf(theEvent);
        canvas.KeyDown?.Invoke(canvas, new(KeyOf(theEvent), modifiers));

        // The typed characters come separately from the key, because a keystroke and the text it
        // produces are different things — dead keys and IME make one keystroke none or several.
        var characters = CocoaRuntime.SendPointer(theEvent, CocoaRuntime.sel_registerName("characters"));
        if (characters == 0)
            return;

        foreach (var c in CocoaNative.ReadString(characters))
            if (!char.IsControl(c))
                canvas.KeyPress?.Invoke(canvas, new(c));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void KeyUpEvent(nint self, nint selector, nint theEvent)
    {
        if (CanvasOf(self) is { } canvas)
            canvas.KeyUp?.Invoke(canvas, new(KeyOf(theEvent), ModifiersOf(theEvent)));
    }

    /// <summary>The toolkit key for an event's virtual key code.</summary>
    private static Keys KeyOf(nint theEvent)
        => CocoaRuntime.SendUShort(theEvent, CocoaRuntime.sel_registerName("keyCode")) switch
        {
            0x24 => Keys.Enter,
            0x30 => Keys.Tab,
            0x31 => Keys.Space,
            0x33 => Keys.Back,
            0x35 => Keys.Escape,
            0x75 => Keys.Delete,
            0x73 => Keys.Home,
            0x77 => Keys.End,
            0x74 => Keys.PageUp,
            0x79 => Keys.PageDown,
            0x7B => Keys.Left,
            0x7C => Keys.Right,
            0x7D => Keys.Down,
            0x7E => Keys.Up,
            _ => Keys.None,
        };

    /// <summary>
    /// AppKit is rebuilding this view's cursor rectangles: claim the whole of it for the shape the
    /// control asked for, or claim nothing and let the window's own arrow stand.
    /// </summary>
    /// <remarks>
    /// The rectangle is read back off the view rather than taken from the peer's buffered bounds,
    /// because a cursor rectangle is in the view's own coordinates and a window's content view is one
    /// of these too without having a peer behind it. <c>NSView</c>'s own implementation claims nothing,
    /// so overriding it without calling up loses no platform behaviour.
    /// </remarks>
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void ResetCursorRects(nint self, nint selector)
    {
        var cursor = CocoaCursor.For(self);
        if (cursor == 0)
            return;

        var bounds = CocoaRuntime.SendRect(self, CocoaRuntime.sel_registerName("bounds"));
        CocoaRuntime.SendCursorRect(self, CocoaRuntime.sel_registerName("addCursorRect:cursor:"), bounds, cursor);
    }

    /// <summary>Answers YES, so this view's coordinates run top-left down like the toolkit's.</summary>
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static byte IsFlipped(nint self, nint selector) => 1;

    /// <summary>AppKit's paint callback, dispatched to the canvas that owns the view.</summary>
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void DrawRect(nint self, nint selector, CocoaRuntime.CGRect dirty)
    {
        if (!_canvases.TryGetValue(self, out var canvas))
            return;

        var context = CocoaNative.CurrentContext();
        if (context == 0)
            return;

        var area = new Rectangle(0, 0, canvas._bounds.Width, canvas._bounds.Height);
        using var graphics = new CocoaGraphics(context);
        canvas.Paint?.Invoke(canvas, new(graphics, area));
    }

    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (_view != 0)
            CocoaRuntime.SendRectVoidOnly(_view, CocoaRuntime.sel_registerName("setFrame:"), new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    public void InvalidateAll()
    {
        if (_view != 0)
            CocoaRuntime.SendVoid(_view, CocoaRuntime.sel_registerName("setNeedsDisplay:"), true);
    }

    public void Invalidate(Rectangle bounds) => this.InvalidateAll();

    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    public void AddChild(IControlPeer child)
    {
        if (_view == 0)
            return;

        var view = child switch
        {
            CocoaCanvasPeer canvas when canvas.Handle != 0 => canvas.Handle,
            CocoaControlPeer control when control.Handle != 0 => control.Handle,
            _ => 0,
        };

        if (view != 0)
            CocoaRuntime.SendVoid(_view, CocoaRuntime.sel_registerName("addSubview:"), view);
    }

    /// <inheritdoc/>
    public void RemoveChild(IControlPeer child)
    {
        var view = child switch
        {
            CocoaCanvasPeer canvas => canvas.Handle,
            CocoaControlPeer control => control.Handle,
            _ => 0,
        };

        if (view != 0)
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("removeFromSuperview"));
    }

    public void SetVisible(bool visible)
    {
        if (_view != 0)
            CocoaRuntime.SendVoid(_view, CocoaRuntime.sel_registerName("setHidden:"), !visible);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The case the seam exists for. Nothing an owner-drawn control paints is legible to an
    /// accessibility client — the view is one rectangle of pixels — so whatever the core says here is
    /// the only description there will ever be.
    /// </remarks>
    public void SetAccessibleInfo(string? name, string? description, AccessibleRole role)
        => CocoaAccessibility.Describe(_view, name, description, role);

    /// <inheritdoc/>
    /// <remarks>Parked for the view's own <see cref="ResetCursorRects"/> to claim, since AppKit asks
    /// rather than being told.</remarks>
    public void SetCursor(Cursor cursor) => CocoaCursor.Apply(_view, cursor);

    /// <inheritdoc/>
    /// <remarks>
    /// Served the same way a native widget's is, though the core does not come here for it: an
    /// owner-drawn control is watched through its canvas mouse pipeline and floats the toolkit's own
    /// popup. The seam is <c>IControlPeer</c>'s, so a caller that reaches it gets the platform's tip
    /// rather than silence.
    /// </remarks>
    public void ShowToolTip(string? text) => CocoaToolTip.Apply(_view, text);

    // --- Not yet, and deliberately not fatal (docs/PRD.md §2) ------------------------------------

    public void SetText(string text) { }

    public void SetEnabled(bool enabled) { }

    public void SetFont(Font font) { }

    public void SetColors(Color foreColor, Color backColor) { }

    public void Focus() { }

    public void SetFocusable(bool focusable) { }

    public void Dispose()
    {
        if (_view == 0)
            return;

        _canvases.TryRemove(_view, out _);
        CocoaCursor.Forget(_view);
    }

    /// <summary>Keeps the input events referenced until AppKit's event routing feeds them.</summary>
    private void Unused()
    {
        MouseDown?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        MouseUp?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        MouseMove?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        MouseWheel?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        MouseLeave?.Invoke(this, EventArgs.Empty);
        KeyDown?.Invoke(this, new(Keys.None, KeyModifiers.None));
        KeyUp?.Invoke(this, new(Keys.None, KeyModifiers.None));
        KeyPress?.Invoke(this, new(' '));
        GotFocus?.Invoke(this, EventArgs.Empty);
        LostFocus?.Invoke(this, EventArgs.Empty);
        PointerMove?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        PointerLeave?.Invoke(this, EventArgs.Empty);
        ContextMenuRequested?.Invoke(this, new(Point.Empty));
    }
}
