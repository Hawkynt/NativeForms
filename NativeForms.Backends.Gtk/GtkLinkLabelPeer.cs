using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="LinkLabel"/>, wrapping a real <c>GtkLinkButton</c> (PRD §12) so
/// the desktop supplies the link colour, the underline, the visited shade and the hand cursor.
/// </summary>
/// <remarks>
/// The URI is deliberately empty and "activate-link" always returns <c>TRUE</c>: the application's hook is
/// <see cref="LinkLabel.LinkClicked"/>, so GTK must be told the activation was handled and must not hand
/// the caption to <c>xdg-open</c>. The widget still marks itself visited on activation, which is why the
/// core's flag is pushed back in rather than left to drift.
/// </remarks>
internal sealed class GtkLinkLabelPeer : GtkControlPeer, ILinkLabelPeer
{
    private bool _visited;

    /// <inheritdoc />
    public event EventHandler? LinkActivated;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_link_button_new_with_label(string.Empty, _text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

    /// <inheritdoc />
    public void SetVisited(bool visited)
    {
        _visited = visited;
        if (_widget != 0)
            NativeMethods.gtk_link_button_set_visited(_widget, visited ? 1 : 0);
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        NativeMethods.gtk_link_button_set_visited(_widget, _visited ? 1 : 0);

        var data = this.PinSelf();
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnActivateLink;
            NativeMethods.g_signal_connect_data(_widget, "activate-link", callback, data, 0, 0);
        }
    }

    /// <summary>
    /// Native handler for "activate-link", shaped as <c>gboolean (GtkLinkButton *, gpointer)</c>. Always
    /// claims the activation so GTK does not try to launch the caption as a URI.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnActivateLink(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkLinkLabelPeer peer)
            peer.LinkActivated?.Invoke(peer, EventArgs.Empty);

        return 1;
    }
}
