namespace Hawkynt.NativeForms;

/// <summary>Carries the affected node to a <see cref="TreeView"/> After* event handler.</summary>
public sealed class TreeViewEventArgs(TreeNode node) : EventArgs
{
    /// <summary>The node the event is about.</summary>
    public TreeNode Node { get; } = node;
}

/// <summary>
/// Carries the affected node to a cancelable <see cref="TreeView"/> Before* event handler; setting
/// <see cref="Cancel"/> vetoes the pending state change.
/// </summary>
public sealed class TreeViewCancelEventArgs(TreeNode node) : EventArgs
{
    /// <summary>The node the event is about.</summary>
    public TreeNode Node { get; } = node;

    /// <summary>Set by a handler to abort the pending expand/collapse.</summary>
    public bool Cancel { get; set; }
}
