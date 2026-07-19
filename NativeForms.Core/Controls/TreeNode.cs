namespace Hawkynt.NativeForms;

/// <summary>
/// A single node in a <see cref="TreeView"/>: a label, optional icons and check state, and a
/// <see cref="Nodes"/> collection of children. Nodes are plain data until their tree is realized —
/// they can be built, nested and expanded long before (or without) a backend existing, exactly like
/// their Windows Forms namesake.
/// </summary>
public sealed class TreeNode
{
    private TreeNodeCollection? _nodes;
    private TreeView? _tree;

    /// <summary>Creates a node with an empty label.</summary>
    public TreeNode() { }

    /// <summary>Creates a node with the given label.</summary>
    public TreeNode(string text) => this.Text = text;

    /// <summary>The node's label.</summary>
    public string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _tree?.Invalidate();
        }
    } = string.Empty;

    /// <summary>Arbitrary caller data associated with the node.</summary>
    public object? Tag { get; set; }

    /// <summary>The <see cref="TreeView.ImageList"/> index of the node's icon, or -1 for none.</summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _tree?.Invalidate();
        }
    } = -1;

    /// <summary>The icon index used while the node is selected, or -1 to reuse <see cref="ImageIndex"/>.</summary>
    public int SelectedImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _tree?.Invalidate();
        }
    } = -1;

    /// <summary>
    /// Whether the node's check box is ticked. Changing it raises <see cref="TreeView.AfterCheck"/>
    /// on an attached tree.
    /// </summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _tree?.OnNodeChecked(this);
        }
    }

    /// <summary>Whether the node's children are currently shown.</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>The child nodes, created on first access.</summary>
    public TreeNodeCollection Nodes => _nodes ??= new(this);

    /// <summary>The parent node, or <see langword="null"/> for a root node.</summary>
    public TreeNode? Parent { get; internal set; }

    /// <summary>The tree this node is attached to, or <see langword="null"/> while detached.</summary>
    public TreeView? TreeView => _tree;

    /// <summary>The zero-based depth of the node: 0 for roots, parent level + 1 below.</summary>
    public int Level
    {
        get
        {
            var level = 0;
            for (var node = this.Parent; node is not null; node = node.Parent)
                ++level;

            return level;
        }
    }

    /// <summary>The collection this node currently lives in, or <see langword="null"/> while detached.</summary>
    internal TreeNodeCollection? OwnerCollection { get; set; }

    /// <summary>The node's index among its siblings, maintained by the owning collection.</summary>
    internal int SiblingIndex { get; set; } = -1;

    /// <summary>Whether the node has at least one child (without forcing <see cref="Nodes"/> into existence).</summary>
    internal bool HasChildren => _nodes is { Count: > 0 };

    /// <summary>Whether a sibling precedes this node.</summary>
    internal bool HasPreviousSibling => this.SiblingIndex > 0;

    /// <summary>Whether a sibling follows this node.</summary>
    internal bool HasNextSibling => this.OwnerCollection is not null && this.SiblingIndex < this.OwnerCollection.Count - 1;

    /// <summary>
    /// Shows the node's children. On an attached tree this raises <see cref="TreeView.BeforeExpand"/>
    /// (cancelable) and <see cref="TreeView.AfterExpand"/>; detached nodes just flip the state.
    /// </summary>
    public void Expand()
    {
        if (_tree is null)
            this.IsExpanded = true;
        else
            _tree.RequestExpand(this);
    }

    /// <summary>Shows the node and its entire subtree, expanding every descendant.</summary>
    public void ExpandAll()
    {
        this.Expand();
        if (!this.IsExpanded || _nodes is null)
            return;

        for (var i = 0; i < _nodes.Count; ++i)
            _nodes[i].ExpandAll();
    }

    /// <summary>
    /// Hides the node's children. On an attached tree this raises <see cref="TreeView.BeforeCollapse"/>
    /// (cancelable) and <see cref="TreeView.AfterCollapse"/>; detached nodes just flip the state.
    /// </summary>
    public void Collapse()
    {
        if (_tree is null)
            this.IsExpanded = false;
        else
            _tree.RequestCollapse(this);
    }

    /// <summary>Expands a collapsed node and collapses an expanded one.</summary>
    public void Toggle()
    {
        if (this.IsExpanded)
            this.Collapse();
        else
            this.Expand();
    }

    /// <summary>Expands every ancestor and scrolls the attached tree until this node is on screen.</summary>
    public void EnsureVisible()
    {
        for (var node = this.Parent; node is not null; node = node.Parent)
            node.Expand();

        _tree?.ScrollNodeIntoView(this);
    }

    /// <summary>Sets the expansion state directly, bypassing the tree's Before/After event pipeline.</summary>
    internal void SetExpandedCore(bool expanded) => this.IsExpanded = expanded;

    /// <summary>Attaches or detaches this node and its whole subtree to a tree.</summary>
    internal void SetTree(TreeView? tree)
    {
        _tree = tree;
        if (_nodes is null)
            return;

        for (var i = 0; i < _nodes.Count; ++i)
            _nodes[i].SetTree(tree);
    }
}
