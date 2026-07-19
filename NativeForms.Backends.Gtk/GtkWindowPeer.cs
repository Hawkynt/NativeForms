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

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <summary>Creates the window and its <c>GtkFixed</c> content area and wires the close signal.</summary>
    internal GtkWindowPeer()
    {
        _widget = NativeMethods.gtk_window_new(NativeMethods.GTK_WINDOW_TOPLEVEL);
        _fixed = NativeMethods.gtk_fixed_new();
        NativeMethods.gtk_container_add(_widget, _fixed);

        _selfHandle = GCHandle.Alloc(this);
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDestroy;
            NativeMethods.g_signal_connect_data(
                _widget, "destroy", callback, GCHandle.ToIntPtr(_selfHandle), 0, 0);
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

    /// <summary>Raises <see cref="Closed"/>; invoked from the native "destroy" callback.</summary>
    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Native handler for the window's "destroy" signal. GTK invokes it as
    /// <c>void (GtkWidget *object, gpointer user_data)</c>; we recover the peer from
    /// <paramref name="userData"/> and end the main loop.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnDestroy(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkWindowPeer peer)
            peer.RaiseClosed();

        NativeMethods.gtk_main_quit();
    }
}
