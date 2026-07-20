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
/// manager's focus. Light dismiss rides on two grabs taken in <see cref="ShowAt"/>: a GDK seat grab
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

    /// <inheritdoc />
    public event EventHandler? Dismissed;

    /// <summary>Creates the popup top-level, realizes the canvas into it and wires the dismissal signals.</summary>
    internal GtkPopupPeer()
    {
        _window = NativeMethods.gtk_window_new(NativeMethods.GTK_WINDOW_POPUP);

        // Realize the canvas eagerly: no container ever parents a top-level surface, so the lazy
        // child path never runs for a popup.
        _widget = CreateWidget();
        OnWidgetRealized();
        ConnectFocusSignals();
        NativeMethods.gtk_container_add(_window, _widget);

        // Dismissal watches the top-level: canvas signals return FALSE, so presses and keys bubble
        // up here after the content had its turn.
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
        NativeMethods.gtk_window_move(_window, screenLocation.X, screenLocation.Y);
        NativeMethods.gtk_widget_set_size_request(_widget, size.Width, size.Height);
        NativeMethods.gtk_widget_show_all(_window);
        _shown = true;

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
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_shown)
        {
            NativeMethods.gtk_grab_remove(_window);
            NativeMethods.gdk_seat_ungrab(NativeMethods.gdk_display_get_default_seat(NativeMethods.gdk_display_get_default()));
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

    /// <summary>Whether a point in the popup window's coordinate space lies outside its allocation.</summary>
    private bool IsOutside(int x, int y)
    {
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
            if (!peer.IsOutside((int)e.X, (int)e.Y))
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
