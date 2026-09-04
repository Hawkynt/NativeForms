namespace Hawkynt.NativeForms;

/// <summary>Carries the affected node to a <see cref="TreeView"/>/<see cref="TreeListView"/> After* event handler.</summary>
public sealed class TreeViewEventArgs(TreeNode node) : EventArgs {
  /// <summary>The node the event is about.</summary>
  public TreeNode Node { get; } = node;
}

/// <summary>
/// Carries the affected node to a cancelable <see cref="TreeView"/>/<see cref="TreeListView"/>
/// Before* event handler; setting <see cref="Cancel"/> vetoes the pending state change.
/// </summary>
public sealed class TreeViewCancelEventArgs(TreeNode node) : EventArgs {
  /// <summary>The node the event is about.</summary>
  public TreeNode Node { get; } = node;

  /// <summary>Set by a handler to abort the pending expand/collapse.</summary>
  public bool Cancel { get; set; }
}

/// <summary>Carries a node label edit to a <see cref="TreeView"/> Before/After label-edit handler; a
/// <see cref="CancelEdit"/> handler vetoes the edit (before) or the commit (after). <see cref="Label"/>
/// is the proposed text, or <see langword="null"/> when the edit was cancelled.</summary>
public sealed class NodeLabelEditEventArgs(TreeNode node, string? label) : EventArgs {
  /// <summary>The node being renamed.</summary>
  public TreeNode Node { get; } = node;

  /// <summary>The proposed new label, or <see langword="null"/> when the edit is cancelled.</summary>
  public string? Label { get; } = label;

  /// <summary>Set by a handler to veto the edit (before) or discard the entered text (after).</summary>
  public bool CancelEdit { get; set; }
}
