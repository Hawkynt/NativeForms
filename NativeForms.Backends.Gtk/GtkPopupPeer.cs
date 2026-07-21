using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a light-dismiss popup surface. It derives from <see cref="GtkCanvasPeer"/> so the
/// whole owner-drawn pipeline — the app-paintable <c>GtkFixed</c> and every paint/input signal — is
/// reused untouched; the canvas is simply created eagerly and dropped into an undecorated
/// <c>GTK_WINDOW_POPUP</c> top-level, which floats at screen coordinates without taking the window
/// manager's focus. That top-level is made transient for the window that owns it and carries a
/// menu/tooltip type hint, so the display server can anchor it to its opener and keeps that opener
/// looking active. Light dismiss rides on two grabs taken in <see cref="ShowAt"/>: a GDK seat grab
/// (<c>GDK_SEAT_CAPABILITY_ALL_POINTING</c>, owner events on) that routes clicks outside the
/// application to the popup, and a GTK grab that redirects the application's own events to it. A
/// button press outside the allocation dismisses, Escape dismisses, and both grabs are released on
/// hide. The keyboard is deliberately not grabbed, so Escape only arrives while the application holds
/// keyboard focus; richer keyboard forwarding is left to the owning control.
/// </summary>
internal sealed class GtkPopupPeer : GtkCanvasPeer, IPopupPeer
{
    private readonly nint _window;
    private bool _shown;

    /// <summary>Whether the grabs taken by the last <see cref="ShowAt"/> are still standing, so
    /// <see cref="Hide"/> releases exactly what it took even if the flag changed in between.</summary>
    private bool _grabbed;

    /// <inheritdoc />
    public event EventHandler? Dismissed;

    /// <inheritdoc />
    public bool LightDismiss { get; set; } = true;

    /// <summary>Creates the popup top-level, realizes the canvas into it and wires the dismissal signals.</summary>
    /// <param name="owner">The <c>GtkWindow</c> this surface belongs to, or zero when none is known.</param>
    internal GtkPopupPeer(nint owner)
    {
        _window = NativeMethods.gtk_window_new(NativeMethods.GTK_WINDOW_POPUP);

        // Without a transient parent a GTK_WINDOW_POPUP is an unrelated, override-redirect top-level:
        // GDK says so out loud ("temporary window without parent, application will not be able to
        // position it on screen") and cannot anchor it to the window that opened it, and GTK has no
        // reason to keep that window looking focused — so pulling down the application's own menu put
        // the whole window into its :backdrop state and greyed out every widget behind the menu.
        // Naming the owner fixes both: the surface is positioned relative to a real parent, and the
        // parent stays active for as long as its transient child is up.
        if (owner != 0)
            NativeMethods.gtk_window_set_transient_for(_window, owner);

        // Realize the canvas eagerly: no container ever parents a top-level surface, so the lazy
        // child path never runs for a popup.
        _widget = CreateWidget();
        OnWidgetRealized();
        ConnectFocusSignals();
        ConnectAllocationClamp();
        NativeMethods.gtk_container_add(_window, _widget);

        // Dismissal watches the top-level. Presses that land on the popup's own canvas stop there —
        // they are menu interactions, never dismissals — while presses elsewhere in the application
        // are redirected here by the GTK grab, and keys the content left unhandled still bubble up,
        // which is what carries Escape.
        NativeMethods.gtk_widget_add_events(_window, NativeMethods.GDK_BUTTON_PRESS_MASK | NativeMethods.GDK_KEY_PRESS_MASK);
        var data = GCHandle.ToIntPtr(_selfHandle);
        unsafe
        {
            NativeMethods.g_signal_connect_data(
                _window, "button-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnWindowButtonPress, data, 0, 0);
            NativeMethods.g_signal_connect_data(
                _window, "key-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnWindowKeyPress, data, 0, 0);
        }
    }

    /// <inheritdoc />
    public void ShowAt(Point screenLocation, Size size)
    {
        // Say what kind of surface this is before it is mapped. A window manager reads the hint to
        // decide whether the surface is an ordinary window that should take the focus and push its
        // opener into the background, or a transient of it that should not; both kinds here are the
        // latter. Set on every show because LightDismiss is written after construction.
        NativeMethods.gtk_window_set_type_hint(
            _window,
            this.LightDismiss ? NativeMethods.GDK_WINDOW_TYPE_HINT_DROPDOWN_MENU : NativeMethods.GDK_WINDOW_TYPE_HINT_TOOLTIP);
        NativeMethods.gtk_window_move(_window, screenLocation.X, screenLocation.Y);

        // A popup is sized here rather than through SetBounds, so record the size as this peer's
        // bounds too: that is the rectangle the canvas clamps its allocation and clips its painting
        // to, and leaving it stale would shrink the popup to whatever it last measured.
        _bounds = new Rectangle(_bounds.Location, size);
        NativeMethods.gtk_widget_set_size_request(_widget, size.Width, size.Height);
        NativeMethods.gtk_widget_show_all(_window);
        _shown = true;

        // A passive surface takes no grab either, so the next press keeps its normal delivery path
        // and reaches the widget the user aimed at.
        if (!this.LightDismiss)
            return;

        // The seat grab routes pointer events outside the application to the popup (owner events keep
        // in-app delivery normal); the GTK grab redirects the application's own events to it. Together
        // they make every outside click land in OnWindowButtonPress.
        var seat = NativeMethods.gdk_display_get_default_seat(NativeMethods.gdk_display_get_default());
        NativeMethods.gdk_seat_grab(
            seat,
            NativeMethods.gtk_widget_get_window(_window),
            NativeMethods.GDK_SEAT_CAPABILITY_ALL_POINTING,
            Bool(true),
            0,
            0,
            0,
            0);
        NativeMethods.gtk_grab_add(_window);
        _grabbed = true;
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_grabbed)
        {
            NativeMethods.gtk_grab_remove(_window);
            NativeMethods.gdk_seat_ungrab(NativeMethods.gdk_display_get_default_seat(NativeMethods.gdk_display_get_default()));
            _grabbed = false;
        }

        _shown = false;
        NativeMethods.gtk_widget_hide(_window);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Hide();

        // The base disposes the canvas (removing it from the window); the empty top-level follows.
        base.Dispose();
        NativeMethods.gtk_widget_destroy(_window);
    }

    /// <summary>Hides the surface and releases the grabs, then raises <see cref="Dismissed"/>. A no-op while hidden.</summary>
    private void Dismiss()
    {
        if (!_shown)
            return;

        Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether a press at the given root (screen) coordinates lies outside the popup's own rectangle.
    /// The grab redirects presses aimed at any other window here without rewriting their coordinates,
    /// so <c>GdkEventButton.X/Y</c> still measures from whichever window the pointer was actually
    /// over. Testing those against this popup's allocation reads a press near the main window's origin
    /// as a press near the popup's origin and refuses to dismiss; only the root coordinates, mapped
    /// through the popup's own origin, describe both windows in the same space.
    /// </summary>
    private bool IsOutside(int rootX, int rootY)
    {
        var window = NativeMethods.gtk_widget_get_window(_window);
        if (window == 0)
            return true;

        NativeMethods.gdk_window_get_origin(window, out var originX, out var originY);
        var x = rootX - originX;
        var y = rootY - originY;
        var width = NativeMethods.gtk_widget_get_allocated_width(_window);
        var height = NativeMethods.gtk_widget_get_allocated_height(_window);
        return x < 0 || y < 0 || x >= width || y >= height;
    }

    /// <summary>Recovers the popup bound to a native callback's <c>user_data</c>.</summary>
    private static GtkPopupPeer? PopupFromData(nint userData)
        => userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkPopupPeer peer ? peer : null;

    /// <summary>Native "button-press-event" handler on the popup top-level: presses outside dismiss.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnWindowButtonPress(nint widget, nint eventPtr, nint userData)
    {
        var peer = PopupFromData(userData);
        if (peer is null)
            return 0;

        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventButton>((void*)eventPtr);
            if (!peer.IsOutside((int)e.XRoot, (int)e.YRoot))
                return 0;
        }

        peer.Dismiss();
        return 1;
    }

    /// <summary>Native "key-press-event" handler on the popup top-level: Escape dismisses.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnWindowKeyPress(nint widget, nint eventPtr, nint userData)
    {
        var peer = PopupFromData(userData);
        if (peer is null)
            return 0;

        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventKey>((void*)eventPtr);
            if (e.KeyVal != NativeMethods.GDK_KEY_Escape)
                return 0;
        }

        peer.Dismiss();
        return 1;
    }
}
