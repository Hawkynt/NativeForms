using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a push button, wrapping a <c>GtkButton</c>. An image becomes the button's
/// <c>GtkImage</c> child (always shown), positioned relative to the label per the requested
/// <see cref="TextImageRelation"/>; <see cref="TextImageRelation.Overlay"/> renders as image-left, and
/// the image alignment has no GTK mapping and is not rendered.
/// </summary>
internal sealed class GtkButtonPeer : GtkControlPeer, IButtonPeer
{
    private GtkImage? _image;
    private TextImageRelation _relation;

    /// <inheritdoc />
    public event EventHandler? Clicked;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_button_new_with_label(_text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

    /// <inheritdoc />
    public void SetImage(IImage? image, ContentAlignment imageAlign, TextImageRelation relation)
    {
        _image = image as GtkImage;
        _relation = relation;
        if (_widget != 0)
            this.ApplyImage();
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        // The toolkit's bounds cap the button (GtkControlPeer.ClampAllocation), so a caption wider
        // than them has to give way; elide its tail the way GTK itself does rather than let it draw
        // over the neighbouring controls.
        var caption = NativeMethods.gtk_bin_get_child(_widget);
        if (NativeMethods.IsLabel(caption))
            NativeMethods.gtk_label_set_ellipsize(caption, NativeMethods.PANGO_ELLIPSIZE_END);

        var data = this.PinSelf();
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClicked;
            NativeMethods.g_signal_connect_data(_widget, "clicked", callback, data, 0, 0);
        }

        if (_image is not null)
            this.ApplyImage();
    }

    /// <summary>Pushes the buffered image (or its removal) onto the live button.</summary>
    private void ApplyImage()
    {
        if (_image is not { Surface: not 0 } image)
        {
            NativeMethods.gtk_button_set_image(_widget, 0);
            return;
        }

        NativeMethods.gtk_button_set_image(_widget, NativeMethods.gtk_image_new_from_surface(image.Surface));
        NativeMethods.gtk_button_set_image_position(_widget, _relation switch
        {
            TextImageRelation.TextBeforeImage => NativeMethods.GTK_POS_RIGHT,
            TextImageRelation.ImageAboveText => NativeMethods.GTK_POS_TOP,
            TextImageRelation.TextAboveImage => NativeMethods.GTK_POS_BOTTOM,
            _ => NativeMethods.GTK_POS_LEFT,
        });
        NativeMethods.gtk_button_set_always_show_image(_widget, 1);
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
