using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A node in a <see cref="DockPanel"/>'s layout tree. The tree holds only the panes that are actually
/// on screen (docked or in the document well); floating, auto-hidden and hidden panes live in the
/// manager's side lists, so they cost the tree nothing. Two node kinds: a
/// <see cref="DockSplitNode"/> divides its rectangle between two children with a draggable splitter,
/// and a <see cref="DockTabGroupNode"/> is a leaf tab well of one or more panes.
/// </summary>
internal abstract class DockNode
{
    /// <summary>The rectangle assigned to this node by the most recent layout pass, in panel-client
    /// coordinates. A value type, so recomputing it allocates nothing.</summary>
    public Rectangle Bounds;
}

/// <summary>A split of one region into two, side by side (<see cref="Orientation.Vertical"/>) or
/// stacked (<see cref="Orientation.Horizontal"/>), separated by a draggable splitter.</summary>
internal sealed class DockSplitNode : DockNode
{
    public DockSplitNode(Orientation orientation, double ratio, DockNode first, DockNode second)
    {
        this.Orientation = orientation;
        this.Ratio = ratio;
        this.First = first;
        this.Second = second;
    }

    /// <summary><see cref="Orientation.Vertical"/> places <see cref="First"/> left of
    /// <see cref="Second"/>; <see cref="Orientation.Horizontal"/> places it above.</summary>
    public Orientation Orientation;

    /// <summary>The fraction of the split axis given to <see cref="First"/> (0..1).</summary>
    public double Ratio;

    /// <summary>The leading child (left or top).</summary>
    public DockNode First;

    /// <summary>The trailing child (right or bottom).</summary>
    public DockNode Second;

    /// <summary>The splitter band from the last layout, in panel-client coordinates.</summary>
    public Rectangle Splitter;
}

/// <summary>A leaf tab well: one or more panes sharing a caption bar and (when more than one) a tab
/// strip. Exactly one pane is active and fills the content area; the rest are peer-vetoed.</summary>
internal sealed class DockTabGroupNode : DockNode
{
    /// <summary>The panes in this well, in tab order.</summary>
    public readonly List<DockContent> Contents = [];

    /// <summary>The index of the active pane within <see cref="Contents"/>.</summary>
    public int ActiveIndex;

    /// <summary>Whether this is the central document well (documents rather than tool windows).</summary>
    public bool IsDocument;

    /// <summary>The caption bar rectangle from the last layout.</summary>
    public Rectangle CaptionBounds;

    /// <summary>The tab-strip rectangle from the last layout (empty when a single pane).</summary>
    public Rectangle TabStripBounds;

    /// <summary>The content rectangle the active pane fills.</summary>
    public Rectangle ContentBounds;

    /// <summary>Per-tab widths cached from the last paint, reused so hit-testing never re-measures.</summary>
    public readonly List<int> TabWidths = [];

    /// <summary>The active pane, or <see langword="null"/> for an (transiently) empty group.</summary>
    public DockContent? Active
        => this.ActiveIndex >= 0 && this.ActiveIndex < this.Contents.Count ? this.Contents[this.ActiveIndex] : null;

    /// <summary>Clamps <see cref="ActiveIndex"/> into range after the contents change.</summary>
    public void ClampActive()
        => this.ActiveIndex = this.Contents.Count == 0 ? 0 : Math.Clamp(this.ActiveIndex, 0, this.Contents.Count - 1);
}
