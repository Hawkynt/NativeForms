namespace Hawkynt.NativeForms;

/// <summary>Where a dragged <see cref="TreeNode"/> lands relative to the node it is dropped on.</summary>
public enum TreeViewDropLocation
{
    /// <summary>As a sibling immediately above the target node.</summary>
    Above,

    /// <summary>As a child of the target node (which is expanded to reveal it).</summary>
    Onto,

    /// <summary>As a sibling immediately below the target node.</summary>
    Below,
}

/// <summary>
/// Carries an in-flight node drag to a <see cref="TreeView"/> drag handler: the node being dragged,
/// the node the pointer is currently over, and where the drop would land. Set <see cref="Cancel"/> to
/// reject the current target — the insertion marker is hidden and the drop is refused while it stays
/// rejected — so a handler can forbid particular reparentings (read-only branches, type mismatches, …).
/// </summary>
public sealed class TreeNodeDragEventArgs(TreeNode dragged, TreeNode? target, TreeViewDropLocation location) : EventArgs
{
    /// <summary>The node being dragged.</summary>
    public TreeNode DraggedNode { get; } = dragged;

    /// <summary>The node under the pointer, or <see langword="null"/> past the last row (append to root).</summary>
    public TreeNode? TargetNode { get; } = target;

    /// <summary>Where <see cref="DraggedNode"/> would land relative to <see cref="TargetNode"/>.</summary>
    public TreeViewDropLocation Location { get; } = location;

    /// <summary>Set by a handler to reject the current target: no marker, no drop.</summary>
    public bool Cancel { get; set; }
}
