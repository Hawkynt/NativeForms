using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="GroupBox"/> (PRD §12): a <c>GtkFixed</c> carrying the control's
/// own coordinate system, with a real <c>GtkFrame</c> behind everything else, filling it — so the border
/// and the caption come from the desktop theme while the children keep the bounds the application gave
/// them.
/// </summary>
/// <remarks>
/// The frame is added first, and a <c>GtkFixed</c> stacks in add order, so every child lands on top of
/// it. Putting the children <em>inside</em> the frame instead would shift them all by whatever inset GTK
/// reserves for the border and label, and the same layout would sit differently on the two rendering
/// paths.
/// </remarks>
internal sealed class GtkGroupBoxPeer : GtkControlPeer, IGroupBoxPeer
{
    private nint _frame;
    private List<GtkControlPeer>? _children;
    private Size _size;

    /// <inheritdoc />
    protected override nint CreateWidget()
    {
        var fixedContainer = NativeMethods.gtk_fixed_new();
        _frame = NativeMethods.gtk_frame_new(_text);
        NativeMethods.gtk_fixed_put(fixedContainer, _frame, 0, 0);
        NativeMethods.gtk_widget_show(_frame);
        return fixedContainer;
    }

    /// <inheritdoc />
    protected override void ApplyText(string text)
    {
        if (_frame != 0)
            NativeMethods.gtk_frame_set_label(_frame, text);
    }

    /// <inheritdoc />
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _size = bounds.Size;
        this.SizeFrame();
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        this.SizeFrame();

        // Children handed over before the widget existed are placed now, in the order they arrived.
        if (_children is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
            children[i].Realize(_widget);
    }

    /// <inheritdoc />
    public void AddChild(IControlPeer child)
    {
        if (child is not GtkControlPeer peer)
            return;

        (_children ??= []).Add(peer);
        if (_widget != 0)
            peer.Realize(_widget);
    }

    /// <inheritdoc />
    public void RemoveChild(IControlPeer child)
    {
        // Drop the managed entry only; the child peer's own Dispose destroys the widget, which also
        // removes it from this GtkFixed.
        if (child is GtkControlPeer peer)
            _children?.Remove(peer);
    }

    /// <summary>Stretches the frame over the whole surface, which a <c>GtkFixed</c> will not do for it.</summary>
    private void SizeFrame()
    {
        if (_frame == 0 || _size.IsEmpty)
            return;

        NativeMethods.gtk_widget_set_size_request(_frame, _size.Width, _size.Height);
    }
}
