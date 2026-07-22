using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A transient top surface a <see cref="DockPanel"/> raises only while a pane caption is being dragged.
/// It composites over every pane (it is the last child) and paints the docking overlay guides plus the
/// translucent landing preview by calling straight back into the owner, so all the drag visuals live in
/// one place. It is created on drag start and torn down on drop, so nothing is allocated at rest.
/// </summary>
internal sealed class DockDragOverlay : OwnerDrawnControl
{
    private readonly DockPanel _owner;

    internal DockDragOverlay(DockPanel owner) => _owner = owner;

    /// <inheritdoc/>
    protected override bool Focusable => false;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e) => _owner.PaintOverlay(e.Graphics, this.Theme);
}
