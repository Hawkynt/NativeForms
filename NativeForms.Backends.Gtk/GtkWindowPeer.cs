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
    private readonly nint _layout;

    /// <summary>Whether a <see cref="RunModal"/> loop currently owns this window.</summary>
    private bool _modal;

    /// <summary>Whether the modal window was closed (hidden); the "destroy" of the eventual dispose
    /// must then neither re-raise <see cref="Closed"/> nor quit the application loop.</summary>
    private bool _modalClosed;
    private bool _quitsOnClose = true;

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
        // The children live in a GtkFixed, as they always have, but that fixed hangs inside a
        // GtkLayout rather than directly in the window.
        //
        // A GtkFixed reports the union of its children's size requests as its own minimum, and a
        // window's minimum becomes the WM_NORMAL_HINTS floor a window manager enforces. Every child
        // sized to fill the form therefore made the window's current size its smallest size — so it
        // could be grown and never shrunk, and each growth ratcheted the floor up again. It read as
        // a window that does not resize at all.
        //
        // GtkLayout is the same fixed-position container with one difference that matters here: it
        // reports a minimum of zero, because it is scrollable and expects to be smaller than its
        // content. That breaks the feedback loop without changing where a single child sits.
        _layout = NativeMethods.gtk_layout_new(0, 0);
        _fixed = NativeMethods.gtk_fixed_new();
        NativeMethods.gtk_layout_put(_layout, _fixed, 0, 0);
        NativeMethods.gtk_container_add(_widget, _layout);

        this.PinSelf();
        unsafe
        {
            var userData = GCHandle.ToIntPtr(_selfHandle);
            var destroyCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDestroy;
            NativeMethods.g_signal_connect_data(_widget, "destroy", destroyCallback, userData, 0, 0);

            var deleteCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDeleteEvent;
            NativeMethods.g_signal_connect_data(_widget, "delete-event", deleteCallback, userData, 0, 0);

            var configureCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnConfigureEvent;
            NativeMethods.g_signal_connect_data(_widget, "configure-event", configureCallback, userData, 0, 0);

            var stateCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnWindowStateEvent;
            NativeMethods.g_signal_connect_data(_widget, "window-state-event", stateCallback, userData, 0, 0);

            var dropCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int, uint, nint, int>)&OnDragDrop;
            NativeMethods.g_signal_connect_data(_widget, "drag-drop", dropCallback, userData, 0, 0);

            var dataCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int, nint, uint, uint, nint, void>)&OnDragDataReceived;
            NativeMethods.g_signal_connect_data(_widget, "drag-data-received", dataCallback, userData, 0, 0);
        }

        // GTK owns the motion/highlight defaults; final-drop data and completion stay explicit below
        // so gtk_drag_finish is issued exactly once by this peer.
        NativeMethods.gtk_drag_dest_set(
            _widget,
            NativeMethods.GTK_DEST_DEFAULT_MOTION | NativeMethods.GTK_DEST_DEFAULT_HIGHLIGHT,
            0,
            0,
            NativeMethods.GDK_ACTION_COPY);
        NativeMethods.gtk_drag_dest_add_uri_targets(_widget);
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

    /// <inheritdoc />
    public void RemoveChild(IControlPeer child) { }

    /// <inheritdoc />
    public void Show()
    {
        NativeMethods.gtk_widget_show(_fixed);
        NativeMethods.gtk_widget_show(_layout);
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
    public void SetMinimizeBox(bool visible)
    {
        _minimizeBox = visible;
        this.ApplyTypeHint();
    }

    /// <inheritdoc />
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

        NativeMethods.gtk_window_set_geometry_hints(_widget, 0, in geometry, flags);
    }

    /// <inheritdoc />
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
    public void SetQuitsOnClose(bool quits) => _quitsOnClose = quits;

    /// <inheritdoc />
    public void SetOpacity(double opacity) => NativeMethods.gtk_widget_set_opacity(_widget, opacity);

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

    private bool IsCloseVetoed()
    {
        if (CloseRequested is not { } handler)
            return false;

        var args = new System.ComponentModel.CancelEventArgs();
        handler.Invoke(this, args);
        return args.Cancel;
    }

    private void CloseModal()
    {
        _modalClosed = true;
        NativeMethods.gtk_widget_hide(_widget);
        this.RaiseClosed();
        NativeMethods.gtk_main_quit();
    }

    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnDragDrop(nint widget, nint context, int x, int y, uint time, nint userData)
    {
        var target = NativeMethods.gtk_drag_dest_find_target(widget, context, 0);
        if (target == 0)
            return 0;

        NativeMethods.gtk_drag_get_data(widget, context, target, time);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void OnDragDataReceived(
        nint widget,
        nint context,
        int x,
        int y,
        nint selectionData,
        uint info,
        uint time,
        nint userData)
    {
        var success = false;
        nint uris = 0;
        try
        {
            if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkWindowPeer peer)
                return;

            uris = NativeMethods.gtk_selection_data_get_uris(selectionData);
            if (uris == 0)
                return;

            var files = new List<string>();
            for (var current = (nint*)uris; *current != 0; ++current)
            {
                var text = Marshal.PtrToStringUTF8(*current);
                if (text is null || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsFile)
                    continue;

                files.Add(uri.LocalPath);
            }

            if (files.Count == 0)
                return;

            var screenPoint = peer.PointToScreen(new Point(x, y));
            success = ExternalDropBridge.Route(peer, files.ToArray(), DragDropEffects.Copy, screenPoint) != DragDropEffects.None;
        }
        catch
        {
            success = false;
        }
        finally
        {
            if (uris != 0)
                NativeMethods.g_strfreev(uris);

            NativeMethods.gtk_drag_finish(context, Bool(success), 0, time);
        }
    }

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

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnDestroy(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkWindowPeer peer)
        {
            if (peer._modalClosed)
                return;

            peer.RaiseClosed();
            if (!peer._quitsOnClose)
                return;
        }

        NativeMethods.gtk_main_quit();
    }
}
