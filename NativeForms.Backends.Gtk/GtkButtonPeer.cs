using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>The GTK peer for a push button, wrapping a <c>GtkButton</c>.</summary>
internal sealed class GtkButtonPeer : GtkControlPeer, IButtonPeer
{
    /// <inheritdoc />
    public event EventHandler? Clicked;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_button_new_with_label(_text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        _selfHandle = GCHandle.Alloc(this);
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClicked;
            NativeMethods.g_signal_connect_data(
                _widget, "clicked", callback, GCHandle.ToIntPtr(_selfHandle), 0, 0);
        }
    }

    /// <summary>Raises <see cref="Clicked"/>; invoked from the native "clicked" callback.</summary>
    private void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Native handler for the button's "clicked" signal, shaped as
    /// <c>void (GtkWidget *widget, gpointer user_data)</c>; recovers the peer from
    /// <paramref name="userData"/>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnClicked(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkButtonPeer peer)
            peer.RaiseClicked();
    }
}
