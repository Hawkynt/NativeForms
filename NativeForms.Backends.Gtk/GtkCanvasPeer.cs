using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for an owner-drawn control, wrapping a focusable <c>GtkFixed</c> that is given its
/// own GDK window and marked app-paintable — it emits the same "draw" signal a <c>GtkDrawingArea</c>
/// would, but can additionally host native children at absolute coordinates, which is what makes it
/// an <see cref="IContainerPeer"/>. The widget's "draw" and input signals become the toolkit's
/// paint/mouse/key/focus events, so every custom control renders through <see cref="GtkGraphics"/>
/// and drives the same event surface; our draw handler runs before GTK's default container draw, so
/// the owner-drawn background always ends up underneath the children. Like the other child peers it
/// is realized lazily: the widget, its event mask and its signal handlers are created the first time
/// the owning container drops it into its <c>GtkFixed</c>, and children added before that moment are
/// buffered and placed as soon as the widget exists. <see cref="GtkPopupPeer"/> derives from it,
/// hosting the same canvas widget inside a popup top-level for the light-dismiss surface.
/// </summary>
internal class GtkCanvasPeer : GtkControlPeer, ICanvasPeer
{
    private const int GdkEventMask =
        NativeMethods.GDK_BUTTON_PRESS_MASK
        | NativeMethods.GDK_BUTTON_RELEASE_MASK
        | NativeMethods.GDK_POINTER_MOTION_MASK
        | NativeMethods.GDK_SCROLL_MASK
        | NativeMethods.GDK_KEY_PRESS_MASK
        | NativeMethods.GDK_KEY_RELEASE_MASK
        | NativeMethods.GDK_LEAVE_NOTIFY_MASK
        | NativeMethods.GDK_FOCUS_CHANGE_MASK;

    private bool _focusable = true;

    /// <summary>Child peers hosted by this surface. Created on first use so leaf canvases (the
    /// overwhelming majority) pay nothing for the container role.</summary>
    private List<GtkControlPeer>? _children;

    /// <inheritdoc />
    public event EventHandler<PaintEventArgs>? Paint;

    /// <inheritdoc />
    public event EventHandler<MouseEventArgs>? MouseDown;

    /// <inheritdoc />
    public event EventHandler<MouseEventArgs>? MouseUp;

    /// <inheritdoc />
    public event EventHandler<MouseEventArgs>? MouseMove;

    /// <inheritdoc />
    public event EventHandler<MouseEventArgs>? MouseWheel;

    /// <inheritdoc />
    public event EventHandler? MouseLeave;

    /// <inheritdoc />
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <inheritdoc />
    public event EventHandler<KeyEventArgs>? KeyUp;

    /// <inheritdoc />
    public event EventHandler<KeyPressEventArgs>? KeyPress;

    /// <inheritdoc />
    protected override nint CreateWidget()
    {
        // A GtkFixed rather than a GtkDrawingArea: same "draw" signal, but it can also host native
        // children. GtkFixed is window-less by default, so give it its own GDK window (before
        // realization) to receive input, and app-paintable so the theme leaves the pixels to us.
        var widget = NativeMethods.gtk_fixed_new();
        NativeMethods.gtk_widget_set_has_window(widget, Bool(true));
        NativeMethods.gtk_widget_set_app_paintable(widget, Bool(true));
        return widget;
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        NativeMethods.gtk_widget_set_can_focus(_widget, Bool(_focusable));
        NativeMethods.gtk_widget_add_events(_widget, GdkEventMask);

        // Place children that were added while this canvas had no widget of its own yet.
        if (_children is not null)
            foreach (var child in _children)
                child.Realize(_widget);

        var data = this.PinSelf();
        unsafe
        {
            Connect("draw", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDraw, data);
            Connect("button-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnButtonPress, data);
            Connect("button-release-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnButtonRelease, data);
            Connect("motion-notify-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnMotion, data);
            Connect("scroll-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnScroll, data);
            Connect("leave-notify-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnLeave, data);
            Connect("key-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKeyPress, data);
            Connect("key-release-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKeyRelease, data);
        }
    }

    /// <summary>Connects one native signal to a Cdecl function pointer with this peer as user data.</summary>
    private void Connect(string signal, nint handler, nint data)
        => NativeMethods.g_signal_connect_data(_widget, signal, handler, data, 0, 0);

    /// <inheritdoc />
    public void AddChild(IControlPeer child)
    {
        if (child is not GtkControlPeer peer)
            return;

        (_children ??= []).Add(peer);
        if (_widget != 0)
            peer.Realize(_widget);
    }

    /// <inheritdoc />
    public void Invalidate(Rectangle bounds)
    {
        if (_widget != 0)
            NativeMethods.gtk_widget_queue_draw_area(_widget, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        if (_widget != 0)
            NativeMethods.gtk_widget_queue_draw(_widget);
    }

    /// <inheritdoc />
    public void SetFocusable(bool focusable)
    {
        _focusable = focusable;
        if (_widget != 0)
            NativeMethods.gtk_widget_set_can_focus(_widget, Bool(focusable));
    }

    // --- Event raisers (called from the native callbacks) ---------------------------------------

    // GTK 3 already double-buffers every "draw" dispatch (the cairo_t* targets an off-screen
    // surface GDK flips at the end of the frame), so no explicit buffer is needed here — only the
    // managed wrappers are cached and reused so a steady-state repaint allocates nothing.
    private GtkGraphics? _graphics;
    private PaintEventArgs? _paintArgs;

    /// <summary>Wraps the Cairo context and raises <see cref="Paint"/> for the invalidated region
    /// (the context's clip, which GDK set from the queued draw areas), falling back to the whole
    /// allocation when the clip is unbounded.</summary>
    private void RaisePaint(nint cr)
    {
        Rectangle clip;
        if (NativeMethods.gdk_cairo_get_clip_rectangle(cr, out var clipRect) != 0)
            clip = new Rectangle(clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height);
        else
        {
            var width = NativeMethods.gtk_widget_get_allocated_width(_widget);
            var height = NativeMethods.gtk_widget_get_allocated_height(_widget);
            clip = new Rectangle(0, 0, width, height);
        }

        var graphics = _graphics ??= new GtkGraphics(cr);
        graphics.Bind(cr);
        var args = _paintArgs ??= new PaintEventArgs(graphics, clip);
        args.Reset(graphics, clip);
        Paint?.Invoke(this, args);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _graphics?.Dispose();
        _graphics = null;
        base.Dispose();
    }

    private void RaiseMouseDown(MouseButtons button, int x, int y, KeyModifiers modifiers)
        => MouseDown?.Invoke(this, new MouseEventArgs(button, x, y, 0, modifiers));

    private void RaiseMouseUp(MouseButtons button, int x, int y, KeyModifiers modifiers)
        => MouseUp?.Invoke(this, new MouseEventArgs(button, x, y, 0, modifiers));

    private void RaiseMouseMove(int x, int y)
        => MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    private void RaiseMouseWheel(int delta, int x, int y, KeyModifiers modifiers)
        => MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, delta, modifiers));

    private void RaiseMouseLeave() => MouseLeave?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises KeyDown and reports whether a handler consumed the key.</summary>
    private bool RaiseKeyDown(Keys key, KeyModifiers modifiers)
    {
        if (KeyDown is not { } handler)
            return false;

        var args = new KeyEventArgs(key, modifiers);
        handler(this, args);
        return args.Handled;
    }

    /// <summary>Raises KeyUp and reports whether a handler consumed the key.</summary>
    private bool RaiseKeyUp(Keys key, KeyModifiers modifiers)
    {
        if (KeyUp is not { } handler)
            return false;

        var args = new KeyEventArgs(key, modifiers);
        handler(this, args);
        return args.Handled;
    }

    /// <summary>Raises KeyPress and reports whether a handler consumed the character.</summary>
    private bool RaiseKeyPress(char keyChar)
    {
        if (KeyPress is not { } handler)
            return false;

        var args = new KeyPressEventArgs(keyChar);
        handler(this, args);
        return args.Handled;
    }

    // --- Mapping helpers ------------------------------------------------------------------------

    /// <summary>Maps a GDK button number (1/2/3) to a <see cref="MouseButtons"/> value.</summary>
    private static MouseButtons ToButton(uint button) => button switch
    {
        1 => MouseButtons.Left,
        2 => MouseButtons.Middle,
        3 => MouseButtons.Right,
        _ => MouseButtons.None,
    };

    /// <summary>Maps a GDK modifier mask to <see cref="KeyModifiers"/>.</summary>
    private static KeyModifiers ToModifiers(uint state)
    {
        var modifiers = KeyModifiers.None;
        if ((state & NativeMethods.GDK_SHIFT_MASK) != 0)
            modifiers |= KeyModifiers.Shift;
        if ((state & NativeMethods.GDK_CONTROL_MASK) != 0)
            modifiers |= KeyModifiers.Control;
        if ((state & NativeMethods.GDK_MOD1_MASK) != 0)
            modifiers |= KeyModifiers.Alt;
        return modifiers;
    }

    /// <summary>Maps a GDK key symbol to the toolkit's <see cref="Keys"/> (or <see cref="Keys.None"/>).</summary>
    private static Keys ToKey(uint keyval) => keyval switch
    {
        NativeMethods.GDK_KEY_BackSpace => Keys.Back,
        NativeMethods.GDK_KEY_Tab => Keys.Tab,
        NativeMethods.GDK_KEY_Return or NativeMethods.GDK_KEY_KP_Enter => Keys.Enter,
        NativeMethods.GDK_KEY_Escape => Keys.Escape,
        NativeMethods.GDK_KEY_space => Keys.Space,
        NativeMethods.GDK_KEY_Page_Up => Keys.PageUp,
        NativeMethods.GDK_KEY_Page_Down => Keys.PageDown,
        NativeMethods.GDK_KEY_End => Keys.End,
        NativeMethods.GDK_KEY_Home => Keys.Home,
        NativeMethods.GDK_KEY_Left => Keys.Left,
        NativeMethods.GDK_KEY_Up => Keys.Up,
        NativeMethods.GDK_KEY_Right => Keys.Right,
        NativeMethods.GDK_KEY_Down => Keys.Down,
        NativeMethods.GDK_KEY_Insert => Keys.Insert,
        NativeMethods.GDK_KEY_Delete => Keys.Delete,
        NativeMethods.GDK_KEY_asterisk or NativeMethods.GDK_KEY_KP_Multiply => Keys.Multiply,
        NativeMethods.GDK_KEY_plus or NativeMethods.GDK_KEY_KP_Add => Keys.Add,
        NativeMethods.GDK_KEY_minus or NativeMethods.GDK_KEY_KP_Subtract => Keys.Subtract,

        // Letters and digits carry their (uppercased) ASCII code, matching the Win32 virtual-key numbering.
        >= 'a' and <= 'z' => (Keys)(keyval - 0x20),
        >= 'A' and <= 'Z' => (Keys)keyval,
        >= '0' and <= '9' => (Keys)keyval,
        _ => Keys.None,
    };

    /// <summary>Recovers the peer bound to a native callback's <c>user_data</c>.</summary>
    private static GtkCanvasPeer? FromData(nint userData)
        => userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkCanvasPeer peer ? peer : null;

    // --- Native callbacks (Cdecl, gboolean-returning) -------------------------------------------
    //
    // Event-routing policy. GTK hands an unhandled event to every ancestor widget in turn
    // (gtk_propagate_event) WITHOUT retranslating its coordinates, so an ancestor canvas would
    // re-interpret the child's client point as its own — a press at child-relative (10,5) inside a
    // TabPage would be hit-tested by the TabControl into its header strip. Pointer events therefore
    // return GDK_EVENT_STOP: the innermost canvas is the control at that pixel, exactly like a native
    // GtkButton or GtkEntry, and no ancestor has any business seeing them.
    //
    // Two deliberate exceptions keep returning GDK_EVENT_PROPAGATE:
    //   * "draw", so GtkFixed's default container handler still paints the native children on top of
    //     the owner-drawn background;
    //   * "scroll-event", so a control that does not scroll lets a scrollable ancestor take the wheel
    //     (the Windows Forms contract). This is safe precisely because every wheel handler in the
    //     toolkit reads only MouseEventArgs.Delta and never the coordinates.
    // Key events stop only once a managed handler has set Handled, which keeps unconsumed keys
    // bubbling to the form's dialog-key chain and to the popup surface's Escape dismissal.

    /// <summary>Native "draw" handler: <c>gboolean (GtkWidget*, cairo_t*, gpointer)</c>.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnDraw(nint widget, nint cr, nint userData)
    {
        FromData(userData)?.RaisePaint(cr);
        return 0;
    }

    /// <summary>Native "button-press-event" handler: <c>gboolean (GtkWidget*, GdkEventButton*, gpointer)</c>.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnButtonPress(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is not null)
        {
            unsafe
            {
                ref var e = ref Unsafe.AsRef<GdkEventButton>((void*)eventPtr);
                peer.RaiseMouseDown(ToButton(e.Button), (int)e.X, (int)e.Y, ToModifiers(e.State));
            }
        }

        return 1;
    }

    /// <summary>Native "button-release-event" handler.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnButtonRelease(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is not null)
        {
            unsafe
            {
                ref var e = ref Unsafe.AsRef<GdkEventButton>((void*)eventPtr);
                peer.RaiseMouseUp(ToButton(e.Button), (int)e.X, (int)e.Y, ToModifiers(e.State));
            }
        }

        return 1;
    }

    /// <summary>Native "motion-notify-event" handler.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnMotion(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is not null)
        {
            unsafe
            {
                ref var e = ref Unsafe.AsRef<GdkEventMotion>((void*)eventPtr);
                peer.RaiseMouseMove((int)e.X, (int)e.Y);
            }
        }

        return 1;
    }

    /// <summary>Native "scroll-event" handler: maps vertical direction to a ±120 wheel delta.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnScroll(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is not null)
        {
            unsafe
            {
                ref var e = ref Unsafe.AsRef<GdkEventScroll>((void*)eventPtr);
                var delta = e.Direction switch
                {
                    NativeMethods.GDK_SCROLL_UP => 120,
                    NativeMethods.GDK_SCROLL_DOWN => -120,
                    _ => 0,
                };
                if (delta != 0)
                    peer.RaiseMouseWheel(delta, (int)e.X, (int)e.Y, ToModifiers(e.State));
            }
        }

        return 0;
    }

    /// <summary>Native "leave-notify-event" handler.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnLeave(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is null)
            return 0;

        peer.RaiseMouseLeave();
        return 1;
    }

    /// <summary>Native "key-press-event" handler: raises KeyDown and, for printable keys, KeyPress.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnKeyPress(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is null)
            return 0;

        bool handled;
        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventKey>((void*)eventPtr);
            handled = peer.RaiseKeyDown(ToKey(e.KeyVal), ToModifiers(e.State));

            var unicode = NativeMethods.gdk_keyval_to_unicode(e.KeyVal);
            if (unicode is >= 0x20 and not 0x7F and <= 0xFFFF)
                handled |= peer.RaiseKeyPress((char)unicode);
        }

        return handled ? 1 : 0;
    }

    /// <summary>Native "key-release-event" handler.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnKeyRelease(nint widget, nint eventPtr, nint userData)
    {
        var peer = FromData(userData);
        if (peer is null)
            return 0;

        bool handled;
        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventKey>((void*)eventPtr);
            handled = peer.RaiseKeyUp(ToKey(e.KeyVal), ToModifiers(e.State));
        }

        return handled ? 1 : 0;
    }
}
