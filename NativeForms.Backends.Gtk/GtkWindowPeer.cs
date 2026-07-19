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

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <summary>Creates the window and its <c>GtkFixed</c> content area and wires the close signals.</summary>
    internal GtkWindowPeer()
    {
        _widget = NativeMethods.gtk_window_new(NativeMethods.GTK_WINDOW_TOPLEVEL);
        _fixed = NativeMethods.gtk_fixed_new();
        NativeMethods.gtk_container_add(_widget, _fixed);

        _selfHandle = GCHandle.Alloc(this);
        unsafe
        {
            var destroyCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDestroy;
            NativeMethods.g_signal_connect_data(
                _widget, "destroy", destroyCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            var deleteCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnDeleteEvent;
            NativeMethods.g_signal_connect_data(
                _widget, "delete-event", deleteCallback, GCHandle.ToIntPtr(_selfHandle), 0, 0);
        }
    }

    /// <summary>The window's widget is created eagerly in the constructor; never lazily created.</summary>
    protected override nint CreateWidget() => _widget;

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_window_set_title(_widget, text);

    /// <inheritdoc />
    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;

        // Window position is left to the window manager; we only size the window.
        NativeMethods.gtk_window_set_default_size(_widget, bounds.Width, bounds.Height);
        NativeMethods.gtk_window_resize(_widget, bounds.Width, bounds.Height);
    }

    /// <inheritdoc />
    public void AddChild(IControlPeer child)
    {
        if (child is GtkControlPeer peer)
            peer.Realize(_fixed);
    }

    /// <inheritdoc />
    public void Show() => NativeMethods.gtk_widget_show_all(_widget);

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
        if (_widget == 0)
            return;

        if (_modal)
            this.CloseModal();
        else
            NativeMethods.gtk_widget_destroy(_widget);
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
    /// Native handler for the window's "delete-event" signal (the window-manager close button). A
    /// modal window intercepts it — hide instead of destroy, so the peer outlives its nested loop —
    /// by returning 1; a modeless window returns 0 and lets GTK proceed to "destroy".
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnDeleteEvent(nint widget, nint evt, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkWindowPeer { _modal: true } peer)
            return 0;

        peer.CloseModal();
        return 1;
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
