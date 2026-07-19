namespace Hawkynt.NativeForms;

/// <summary>
/// The virtualization engine <see cref="TreeView"/> and <see cref="TreeListView"/> share: the
/// expanded part of a tree flattened into a list of visible rows, plus the scroll position over
/// them. Flattening is lazy — structural changes only mark the list dirty and the next access
/// rebuilds it — so mutating a huge tree stays cheap and painting touches no stale rows.
/// </summary>
internal sealed class TreeRowList
{
    private readonly List<TreeNode> _rows = [];
    private readonly TreeNodeCollection _roots;
    private readonly Func<int> _visibleRows;
    private bool _dirty = true;
    private int _topIndex;

    /// <summary>Creates the row list over the given roots.</summary>
    /// <param name="roots">The root collection to flatten.</param>
    /// <param name="visibleRows">Supplies the number of fully visible rows in the owner's client area.</param>
    public TreeRowList(TreeNodeCollection roots, Func<int> visibleRows)
    {
        _roots = roots;
        _visibleRows = visibleRows;
    }

    /// <summary>The index of the first visible row (scroll position).</summary>
    public int TopIndex => _topIndex;

    /// <summary>The number of rows the expanded part of the tree currently occupies.</summary>
    public int Count
    {
        get
        {
            this.EnsureFlat();
            return _rows.Count;
        }
    }

    /// <summary>The node at the given row index.</summary>
    public TreeNode this[int index]
    {
        get
        {
            this.EnsureFlat();
            return _rows[index];
        }
    }

    /// <summary>Marks the flattened rows stale after a structural change.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>The row index of the node, or -1 while it is not visible.</summary>
    public int IndexOf(TreeNode node)
    {
        this.EnsureFlat();
        return _rows.IndexOf(node);
    }

    /// <summary>Moves the scroll position by the given number of rows, clamped to the valid range.</summary>
    public void ScrollBy(int rowDelta)
    {
        this.EnsureFlat();
        _topIndex += rowDelta;
        this.ClampScroll();
    }

    /// <summary>
    /// Scrolls so the node's row is inside the viewport. Returns whether the node is a visible row at
    /// all — <see langword="false"/> means nothing changed and no repaint is needed.
    /// </summary>
    public bool ScrollIntoView(TreeNode node)
    {
        this.EnsureFlat();
        var index = _rows.IndexOf(node);
        if (index < 0)
            return false;

        var visible = _visibleRows();
        if (index < _topIndex)
            _topIndex = index;
        else if (index >= _topIndex + visible)
            _topIndex = index - visible + 1;

        this.ClampScroll();
        return true;
    }

    private void EnsureFlat()
    {
        if (!_dirty)
            return;

        _rows.Clear();
        _roots.FlattenVisibleInto(_rows);
        _dirty = false;
        this.ClampScroll();
    }

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, _rows.Count - _visibleRows());
        _topIndex = Math.Clamp(_topIndex, 0, maxTop);
    }
}
