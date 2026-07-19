using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>
/// The children of a <see cref="TreeNode"/> (or the roots of a <see cref="TreeView"/> or
/// <see cref="TreeListView"/>). Every structural change re-parents the affected subtree and tells the
/// owning control — if one is attached — to re-flatten its visible rows and repaint. Fully usable
/// before any control or backend exists.
/// </summary>
public sealed class TreeNodeCollection : IReadOnlyList<TreeNode>
{
    private readonly List<TreeNode> _items = [];
    private readonly TreeNode? _ownerNode;
    private readonly ITreeNodeHost? _ownerHost;

    /// <summary>Creates the child collection of a node.</summary>
    internal TreeNodeCollection(TreeNode ownerNode) => _ownerNode = ownerNode;

    /// <summary>Creates the root collection of a hosting control.</summary>
    internal TreeNodeCollection(ITreeNodeHost ownerHost) => _ownerHost = ownerHost;

    /// <summary>The number of direct children.</summary>
    public int Count => _items.Count;

    /// <summary>The child at the given index.</summary>
    public TreeNode this[int index] => _items[index];

    /// <summary>The control this collection is (indirectly) attached to, or <see langword="null"/>.</summary>
    internal ITreeNodeHost? Host => _ownerNode is null ? _ownerHost : _ownerNode.Host;

    /// <summary>Appends a new node with the given label and returns it.</summary>
    public TreeNode Add(string text) => this.Add(new TreeNode(text));

    /// <summary>Appends a node and returns it.</summary>
    /// <exception cref="ArgumentException">The node already lives in a collection.</exception>
    public TreeNode Add(TreeNode node)
    {
        this.Insert(_items.Count, node);
        return node;
    }

    /// <summary>Appends several nodes.</summary>
    public void AddRange(IEnumerable<TreeNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes)
            this.Add(node);
    }

    /// <summary>Inserts a node at the given index.</summary>
    /// <exception cref="ArgumentException">The node already lives in a collection.</exception>
    public void Insert(int index, TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.OwnerCollection is not null)
            throw new ArgumentException("The node is already part of a collection; remove it first.", nameof(node));

        _items.Insert(index, node);
        node.OwnerCollection = this;
        node.Parent = _ownerNode;
        node.SetHost(this.Host);
        this.ReindexFrom(index);
        this.Host?.OnStructureChanged();
    }

    /// <summary>Removes a node (and its subtree) if present.</summary>
    public bool Remove(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.OwnerCollection, this))
            return false;

        this.RemoveAt(node.SiblingIndex);
        return true;
    }

    /// <summary>Removes the node at the given index (and its subtree).</summary>
    public void RemoveAt(int index)
    {
        var host = this.Host;
        Detach(_items[index]);
        _items.RemoveAt(index);
        this.ReindexFrom(index);
        host?.OnStructureChanged();
    }

    /// <summary>Removes all children (and their subtrees).</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;

        var host = this.Host;
        for (var i = 0; i < _items.Count; ++i)
            Detach(_items[i]);

        _items.Clear();
        host?.OnStructureChanged();
    }

    /// <summary>The index of the node among its siblings, or -1 if it is not a direct child.</summary>
    public int IndexOf(TreeNode node) => ReferenceEquals(node.OwnerCollection, this) ? node.SiblingIndex : -1;

    /// <inheritdoc/>
    public IEnumerator<TreeNode> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>Appends this collection's nodes and, below every expanded one, its subtree — the paint order.</summary>
    internal void FlattenVisibleInto(List<TreeNode> rows)
    {
        for (var i = 0; i < _items.Count; ++i)
        {
            var node = _items[i];
            rows.Add(node);
            if (node.IsExpanded && node.HasChildren)
                node.Nodes.FlattenVisibleInto(rows);
        }
    }

    private static void Detach(TreeNode node)
    {
        node.OwnerCollection = null;
        node.SiblingIndex = -1;
        node.Parent = null;
        node.SetHost(null);
    }

    private void ReindexFrom(int index)
    {
        for (var i = index; i < _items.Count; ++i)
            _items[i].SiblingIndex = i;
    }
}
