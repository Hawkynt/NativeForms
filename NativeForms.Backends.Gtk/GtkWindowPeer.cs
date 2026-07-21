using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a top-level window. Wraps a <c>GtkWindow</c> whose sole child is a
/// <c>GtkFixed</c>; child peers are dropped into that <c>GtkFixed</c> by absolute coordinates to
/// mirror Windows Forms' absolute layout.
/// </summary>
internal sealed class GtkWindowPeer : GtkControlPeer, IWindowPeer
{
    private readonly nint _fixed;

    /// <summary>Whether a <see cref="RunModal"/> loop currently owns this window.</summary>
    private bool _modal;

    /// <summary>Whether the modal window was closed (hidden); the "destroy" of the eventual dispose
    /// must then neither re-raise <see cref="Closed"/> nor quit the application loop.</summary>
    private bool _modalClosed;

    private FormBorderStyle _borderStyle = FormBorderStyle.Sizable;
    private FormWindowState _windowState;
    private bool _minimizeBox = true;
    private bool _maximizeBox = true;

    /// <inheritdoc />
    public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested;

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <inheritdoc />
    public event EventHandler<Rectangle>? BoundsChangedByUser;

    /// <inheritdoc />
    public event EventHandler<FormWindowState>? WindowStateChanged;

    /// <summary>Creates the window and its <c>GtkFixed</c> content area and wires the close signals.</summary>
    internal GtkWindowPeer()
    {
        _widget = NativeMethods.gtk_window_new(NativeMethods.GTK_WINDOW_TOPLEVEL);
        _fixed = NativeMethods.gtk_fixed_new();
        NativeMethods.gtk_container_add(_widget, _fixed);

        this.PinSelf();
        unsafe
        {
            var destroyCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDestroy;
            NativeMethods.g_signal_connect_data(
                _widget, "destroy", destroyCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            var deleteCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDeleteEvent;
            NativeMethods.g_signal_connect_data(
                _widget, "delete-event", deleteCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            var configureCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnConfigureEvent;
            NativeMethods.g_signal_connect_data(
                _widget, "configure-event", configureCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            var stateCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnWindowStateEvent;
            NativeMethods.g_signal_connect_data(
                _widget, "window-state-event", stateCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);
        }
    }

    /// <summary>The window's widget is created eagerly in the constructor; never lazily created.</summary>
    protected override nint CreateWidget() => _widget;

    /// <summary>
    /// A top-level negotiates its size with the window manager rather than with a parent container,
    /// so its allocation is not the toolkit's to force — <see cref="SetBounds"/> resizes the window
    /// and the resulting geometry comes back through "configure-event".
    /// </summary>
    private protected override bool ClampsAllocation => false;

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_window_set_title(_widget, text);

    /// <inheritdoc />
    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;

        // Top-level bounds are screen coordinates, exactly like the Win32 backend's MoveWindow —
        // Form.StartPosition relies on the position being honored.
        NativeMethods.gtk_window_set_default_size(_widget, bounds.Width, bounds.Height);
        NativeMethods.gtk_window_resize(_widget, bounds.Width, bounds.Height);
        NativeMethods.gtk_window_move(_widget, bounds.X, bounds.Y);
    }

    /// <inheritdoc />
    public void AddChild(IControlPeer child)
    {
        if (child is GtkControlPeer peer)
            peer.Realize(_fixed);
    }

    /// <summary>
    /// Maps the window and its content area, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>gtk_widget_show_all</c>: that walks the whole descendant tree and shows
    /// every widget in it, overriding the ones the toolkit hid on purpose — the four unselected
    /// <c>TabPage</c>s, a collapsed <c>Expander</c>'s children, anything with
    /// <c>Control.Visible = false</c>. The children do not need the recursion anyway, because every
    /// child peer applies its own buffered visibility as it is realized into its parent.
    /// </remarks>
    public void Show()
    {
        NativeMethods.gtk_widget_show(_fixed);
        NativeMethods.gtk_widget_show(_widget);
    }

    /// <inheritdoc />
    public void SetBorderStyle(FormBorderStyle borderStyle)
    {
        _borderStyle = borderStyle;
        NativeMethods.gtk_window_set_resizable(_widget, Bool(borderStyle == FormBorderStyle.Sizable));
        NativeMethods.gtk_window_set_decorated(_widget, Bool(borderStyle != FormBorderStyle.None));
        this.ApplyTypeHint();
    }

    /// <inheritdoc />
    public void SetWindowState(FormWindowState state)
    {
        _windowState = state;
        switch (state)
        {
            case FormWindowState.Minimized:
                NativeMethods.gtk_window_iconify(_widget);
                break;

            case FormWindowState.Maximized:
                NativeMethods.gtk_window_deiconify(_widget);
                NativeMethods.gtk_window_maximize(_widget);
                break;

            default:
                NativeMethods.gtk_window_deiconify(_widget);
                NativeMethods.gtk_window_unmaximize(_widget);
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>Advisory on GTK: the window manager owns the caption buttons and no GTK call toggles
    /// them individually. The wish is folded into the type hint (<see cref="ApplyTypeHint"/>).</remarks>
    public void SetMinimizeBox(bool visible)
    {
        _minimizeBox = visible;
        this.ApplyTypeHint();
    }

    /// <inheritdoc />
    /// <remarks>Advisory on GTK, exactly like <see cref="SetMinimizeBox"/>.</remarks>
    public void SetMaximizeBox(bool visible)
    {
        _maximizeBox = visible;
        this.ApplyTypeHint();
    }

    /// <inheritdoc />
    public void SetSizeLimits(Size minimum, Size maximum)
    {
        var flags = 0;
        var geometry = new NativeMethods.GdkGeometry();
        if (minimum.Width > 0 || minimum.Height > 0)
        {
            flags |= NativeMethods.GDK_HINT_MIN_SIZE;
            geometry.MinWidth = Math.Max(0, minimum.Width);
            geometry.MinHeight = Math.Max(0, minimum.Height);
        }

        if (maximum.Width > 0 || maximum.Height > 0)
        {
            flags |= NativeMethods.GDK_HINT_MAX_SIZE;
            geometry.MaxWidth = maximum.Width > 0 ? maximum.Width : int.MaxValue;
            geometry.MaxHeight = maximum.Height > 0 ? maximum.Height : int.MaxValue;
        }

        // A zero mask clears previously set hints, so lifting the limits works too.
        NativeMethods.gtk_window_set_geometry_hints(_widget, 0, in geometry, flags);
    }

    /// <inheritdoc />
    /// <remarks>Fills a pixbuf-owned RGBA buffer (straight alpha, matching the source pixels) and
    /// hands it to the window, which keeps its own reference.</remarks>
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        var pixbuf = NativeMethods.gdk_pixbuf_new(NativeMethods.GDK_COLORSPACE_RGB, 1, 8, width, height);
        if (pixbuf == 0)
            return;

        unsafe
        {
            var stride = NativeMethods.gdk_pixbuf_get_rowstride(pixbuf);
            var pixels = (byte*)NativeMethods.gdk_pixbuf_get_pixels(pixbuf);
            for (var y = 0; y < height; ++y)
            {
                var row = pixels + y * stride;
                for (var x = 0; x < width; ++x)
                {
                    var source = unchecked((uint)argb[y * width + x]);
                    row[x * 4] = (byte)((source >> 16) & 0xFF);
                    row[x * 4 + 1] = (byte)((source >> 8) & 0xFF);
                    row[x * 4 + 2] = (byte)(source & 0xFF);
                    row[x * 4 + 3] = (byte)(source >> 24);
                }
            }
        }

        NativeMethods.gtk_window_set_icon(_widget, pixbuf);
        NativeMethods.g_object_unref(pixbuf);
    }

    /// <inheritdoc />
    public void SetTopMost(bool topMost) => NativeMethods.gtk_window_set_keep_above(_widget, Bool(topMost));

    /// <inheritdoc />
    public void SetOpacity(double opacity) => NativeMethods.gtk_widget_set_opacity(_widget, opacity);

    /// <summary>
    /// Advises the window manager through the type hint — the honest GTK approximation for caption
    /// buttons and dialog frames: fixed-dialog (or "both boxes off") windows read as dialogs, tool
    /// windows as utility windows. The window manager makes the final call.
    /// </summary>
    private void ApplyTypeHint()
    {
        var hint = _borderStyle switch
        {
            FormBorderStyle.FixedDialog => NativeMethods.GDK_WINDOW_TYPE_HINT_DIALOG,
            FormBorderStyle.FixedToolWindow => NativeMethods.GDK_WINDOW_TYPE_HINT_UTILITY,
            _ when !_minimizeBox && !_maximizeBox => NativeMethods.GDK_WINDOW_TYPE_HINT_DIALOG,
            _ => NativeMethods.GDK_WINDOW_TYPE_HINT_NORMAL,
        };

        NativeMethods.gtk_window_set_type_hint(_widget, hint);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The GTK modal recipe: mark the window modal, make it transient for its owner (GTK then blocks
    /// the owner's input and stacks the dialog above it), show it, and nest a <c>gtk_main</c> —
    /// closing quits the innermost loop only, so the application's own <c>gtk_main</c> keeps running.
    /// Closing hides the window (see the "delete-event" handler) rather than destroying it, so the
    /// peer survives the loop and the core disposes it normally afterwards.
    /// </remarks>
    public void RunModal(IWindowPeer? owner)
    {
        NativeMethods.gtk_window_set_modal(_widget, 1);
        if (owner is GtkWindowPeer ownerPeer)
            NativeMethods.gtk_window_set_transient_for(_widget, ownerPeer._widget);

        _modal = true;
        _modalClosed = false;
        try
        {
            this.Show();
            if (!_modalClosed)
                NativeMethods.gtk_main();
        }
        finally
        {
            _modal = false;
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_widget == 0 || this.IsCloseVetoed())
            return;

        if (_modal)
            this.CloseModal();
        else
            NativeMethods.gtk_widget_destroy(_widget);
    }

    /// <summary>Raises <see cref="CloseRequested"/> and reports whether a subscriber vetoed the close.</summary>
    private bool IsCloseVetoed()
    {
        if (CloseRequested is not { } handler)
            return false;

        var args = new System.ComponentModel.CancelEventArgs();
        handler.Invoke(this, args);
        return args.Cancel;
    }

    /// <summary>Ends a modal run: hides the window, announces the close, and quits the nested loop.</summary>
    private void CloseModal()
    {
        _modalClosed = true;
        NativeMethods.gtk_widget_hide(_widget);
        this.RaiseClosed();
        NativeMethods.gtk_main_quit();
    }

    /// <summary>Raises <see cref="Closed"/>; invoked from the native close paths.</summary>
    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Native handler for the window's "delete-event" signal (the window-manager close button). The
    /// core may veto the close (returning 1 keeps the window open). Past the veto, a modal window
    /// intercepts the event — hide instead of destroy, so the peer outlives its nested loop — by
    /// returning 1; a modeless window returns 0 and lets GTK proceed to "destroy".
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnDeleteEvent(nint widget, nint evt, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkWindowPeer peer)
            return 0;

        if (peer.IsCloseVetoed())
            return 1;

        if (!peer._modal)
            return 0;

        peer.CloseModal();
        return 1;
    }

    /// <summary>
    /// Native handler for the window's "configure-event" signal: the window was moved or resized.
    /// Updates the buffered bounds and reports them so the core can adopt the new rectangle without
    /// echoing a resize back. Returns 0 so GTK's own configure handling proceeds.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnConfigureEvent(nint widget, nint evt, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkWindowPeer peer)
            return 0;

        Rectangle bounds;
        unsafe
        {
            var configure = (GdkEventConfigure*)evt;
            bounds = new(configure->X, configure->Y, configure->Width, configure->Height);
        }

        peer._bounds = bounds;
        peer.BoundsChangedByUser?.Invoke(peer, bounds);
        return 0;
    }

    /// <summary>
    /// Native handler for the window's "window-state-event" signal: syncs minimize/maximize/restore
    /// transitions back to the core, raising <see cref="WindowStateChanged"/> only on a real change.
    /// Returns 0 so GTK's own state handling proceeds.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnWindowStateEvent(nint widget, nint evt, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkWindowPeer peer)
            return 0;

        int newState;
        unsafe
        {
            newState = ((GdkEventWindowState*)evt)->NewWindowState;
        }

        var state = (newState & NativeMethods.GDK_WINDOW_STATE_ICONIFIED) != 0
            ? FormWindowState.Minimized
            : (newState & NativeMethods.GDK_WINDOW_STATE_MAXIMIZED) != 0
                ? FormWindowState.Maximized
                : FormWindowState.Normal;

        if (state == peer._windowState)
            return 0;

        peer._windowState = state;
        peer.WindowStateChanged?.Invoke(peer, state);
        return 0;
    }

    /// <summary>
    /// Native handler for the window's "destroy" signal. GTK invokes it as
    /// <c>void (GtkWidget *object, gpointer user_data)</c>; we recover the peer from
    /// <paramref name="userData"/> and end the main loop. A window that already announced a modal
    /// close is merely being disposed — no notification, no loop exit.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnDestroy(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkWindowPeer peer)
        {
            if (peer._modalClosed)
                return;

            peer.RaiseClosed();
        }

        NativeMethods.gtk_main_quit();
    }
}
