namespace Hawkynt.NativeForms;

/// <summary>
/// What a tree-shaped control (<see cref="TreeView"/>, <see cref="TreeListView"/>) offers the node
/// model: repaint and re-flatten notifications, scrolling, selection and the raisers of the
/// cancelable expand/collapse events. <see cref="TreeNode"/> and <see cref="TreeNodeCollection"/>
/// talk exclusively to this contract, which is what lets both controls share the model unchanged.
/// </summary>
internal interface ITreeNodeHost
{
    /// <summary>The selected node, or <see langword="null"/>.</summary>
    TreeNode? SelectedNode { get; set; }

    /// <summary>Requests a full repaint.</summary>
    void Invalidate();

    /// <summary>Called after any structural change so the visible rows re-flatten and repaint.</summary>
    void OnStructureChanged();

    /// <summary>Called after a node's check state changed.</summary>
    void OnNodeChecked(TreeNode node);

    /// <summary>Scrolls so the given (visible) node's row is inside the client area.</summary>
    void ScrollNodeIntoView(TreeNode node);

    /// <summary>Raises the control's cancelable BeforeExpand event.</summary>
    void OnBeforeExpand(TreeViewCancelEventArgs e);

    /// <summary>Raises the control's AfterExpand event.</summary>
    void OnAfterExpand(TreeViewEventArgs e);

    /// <summary>Raises the control's cancelable BeforeCollapse event.</summary>
    void OnBeforeCollapse(TreeViewCancelEventArgs e);

    /// <summary>Raises the control's AfterCollapse event.</summary>
    void OnAfterCollapse(TreeViewEventArgs e);
}
