using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>The GTK peer for a static-text label, wrapping a <c>GtkLabel</c>.</summary>
internal sealed class GtkLabelPeer : GtkControlPeer, ILabelPeer
{
    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_label_new(_text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_label_set_text(_widget, text);
}
