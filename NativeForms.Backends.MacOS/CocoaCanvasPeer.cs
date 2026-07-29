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

        if (_view != 0)
            _canvases[_view] = this;
    }

    /// <summary>The view handle, so a container can add it to its own.</summary>
    internal nint Handle => _view;

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

        CocoaRuntime.objc_registerClassPair(created);
        _viewClass = created;
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
        using var graphics = new CocoaGraphics(context, area.Height);
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

    // --- Not yet, and deliberately not fatal (docs/PRD.md §2) ------------------------------------

    public void SetText(string text) { }

    public void SetEnabled(bool enabled) { }

    public void SetFont(Font font) { }

    public void SetColors(Color foreColor, Color backColor) { }

    public void SetCursor(Cursor cursor) { }

    public void Focus() { }

    public void ShowToolTip(string? text) { }

    public void SetFocusable(bool focusable) { }

    public void Dispose()
    {
        if (_view != 0)
            _canvases.TryRemove(_view, out _);
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
