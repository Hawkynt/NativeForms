using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn TreeView × ListView hybrid painted in the native theme: the first column renders an
/// expandable <see cref="TreeNode"/> hierarchy (indent, expand/collapse glyphs, optional check boxes
/// and per-node icons from an <see cref="ImageList"/>), the remaining columns render per-node text
/// produced by reflection-free <see cref="TreeListViewColumn.TextSelector"/>s under a ListView-style
/// header row. Selection is full-row; expand/collapse, checking and keyboard navigation behave
/// exactly like <see cref="TreeView"/>. The expanded part of the tree is flattened into a list of
/// visible rows and painting is virtualized to the rows intersecting the client area, so it stays
/// cheap for very large trees.
/// </summary>
/// <remarks>
/// Three seams let a caller take part in how a row looks and behaves without subclassing:
/// <see cref="RowBackColorSelector"/> and <see cref="RowForeColorSelector"/> colour a row by what it
/// represents, <see cref="CellPaint"/> hands over a cell whose content is not text, and
/// <see cref="ColumnClick"/> reports a click on the header. Together they are what a process list, a
/// log viewer or a file browser needs and could not previously express.
/// <para>TODO: interactive column resize and label editing.</para>
/// </remarks>
public class TreeListView : OwnerDrawnControl, ITreeNodeHost
{
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.Tree;

    private const int _CheckCellWidth = GlyphRenderer.CheckBoxSize + 4;
    private const int _IconGap = 4;
    private const int _TextPad = 2;
    private const int _CellPad = 2;

    private readonly TreeRowList _rows;

    /// <summary>Painted only while there are more rows than fit.</summary>
    private readonly RowScrollBar _scrollBar = new();

    /// <summary>Whether the sideways bar is being dragged, and where the drag started.</summary>
    private bool _horizontalDragging;
    private int _horizontalGrabX;
    private int _horizontalGrabOffset;
    private readonly List<TreeListViewColumn> _watchedColumns = [];
    private TreeNode? _selectedNode;
    private int? _itemHeight;
    private TreeNode? _lastClickNode;
    private long _lastClickTicks;
    private readonly TreeListViewCellPaintEventArgs _cellPaintArgs = new();

    /// <summary>Creates a tree-list view.</summary>
    public TreeListView()
    {
        this.Nodes = new(this);
        _rows = new(this.Nodes, () => this.VisibleRowCount);
        this.Columns = new();
        this.Columns.ListChanged += this.OnColumnsChanged;
    }

    /// <summary>The root nodes. Mutating any level of the hierarchy re-flattens and repaints.</summary>
    public TreeNodeCollection Nodes { get; }

    /// <summary>
    /// The columns. Index 0 is the tree column; the rest render their
    /// <see cref="TreeListViewColumn.TextSelector"/> text. Mutating the collection — or any column's
    /// caption, width or alignment — repaints the control.
    /// </summary>
    public ObservableList<TreeListViewColumn> Columns { get; }

    /// <summary>
    /// Gives each row a background colour of its own, or <see langword="null"/> to use the theme's.
    /// </summary>
    /// <remarks>
    /// Called once per painted row, so it must be cheap — read a value the caller already computed
    /// rather than computing one here. The selected row keeps the theme's selection colour whatever
    /// this returns, because a selection that some rows swallow is worse than an uncoloured row.
    /// </remarks>
    public Func<TreeNode, Color?>? RowBackColorSelector { get; set; }

    /// <summary>Gives each row a text colour of its own, or <see langword="null"/> for the theme's.</summary>
    public Func<TreeNode, Color?>? RowForeColorSelector { get; set; }

    /// <summary>The selected node, or <see langword="null"/>. Setting it scrolls the node into view.</summary>
    /// <exception cref="ArgumentException">The node belongs to a different control.</exception>
    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
                return;

            if (value is not null && !ReferenceEquals(value.Host, this))
                throw new ArgumentException("The node is not attached to this control.", nameof(value));

            if (value is not null)
            {
                var pending = new TreeViewCancelEventArgs(value);
                this.OnBeforeSelect(pending);
                if (pending.Cancel)
                    return;
            }

            _selectedNode = value;
            if (value is not null)
                this.ScrollNodeIntoView(value);

            this.Invalidate();
            if (value is not null)
                this.OnAfterSelect(new TreeViewEventArgs(value));
        }
    }

    /// <summary>Whether every node shows a themed check box in the tree column. Defaults to <see langword="false"/>.</summary>
    public bool CheckBoxes
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>The icon store for <see cref="TreeNode.ImageIndex"/>, or <see langword="null"/> for no icons.</summary>
    public ImageList? ImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            this.BindImageListAnimation(field, value);
            field = value;
            this.Invalidate();
        }
    }

    /// <summary>The pixel height of a row, the header and the indent per level. Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            this.Invalidate();
        }
    }

    /// <summary>Whether the column header row is shown. Defaults to <see langword="true"/>.</summary>
    public bool ShowColumnHeaders
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = true;

    /// <summary>The index of the first visible row in the flattened tree (scroll position).</summary>
    /// <summary>
    /// The first visible row, and the seam a caller needs to keep a live list still.
    /// </summary>
    /// <remarks>
    /// A list whose contents are replaced every second cannot hold its place through this index
    /// alone: the index survives, but the rows underneath it do not, so the view appears to jump
    /// whenever anything above it is added or removed. A caller that wants stability notes the node
    /// at the top before it rebuilds, finds that node's new row afterwards, and assigns it here.
    /// Clamped to the rows that exist, so a stale index is a no-op rather than an exception.
    /// </remarks>
    public int TopIndex
    {
        get => _rows.TopIndex;
        set
        {
            if (_rows.TopIndex == value)
                return;

            _rows.ScrollTo(value);
            this.Invalidate();
        }
    }

    /// <summary>The row a node occupies in the flattened tree, or -1 when it is not visible.</summary>
    /// <remarks>
    /// The other half of holding a scroll position: without it a caller can name the node it wants
    /// at the top but cannot say which row that has become.
    /// </remarks>
    public int RowOf(TreeNode node) => _rows.IndexOf(node);

    /// <summary>The number of rows the expanded part of the tree currently occupies.</summary>
    public int VisibleNodeCount => _rows.Count;

    /// <summary>The node showing at a row, or null when the tree has no such row.</summary>
    /// <remarks>
    /// The inverse of <see cref="RowOf"/>, and the cheap way to note where a view is before a
    /// rebuild: without it a caller has to ask <see cref="RowOf"/> about every node it holds to find
    /// the one row it cares about.
    /// </remarks>
    // Count flattens the tree if it is stale, so the bounds check is also what makes the read safe.
    public TreeNode? NodeAt(int row) => (uint)row < (uint)_rows.Count ? _rows[row] : null;

    /// <summary>
    /// Which row is under a point in the control's own coordinates, or -1 for none.
    /// </summary>
    /// <remarks>
    /// The arithmetic the mouse handlers already do, made available to a caller — a table that wants
    /// to say something about the row under the pointer, rather than about the row that is selected,
    /// has no other way to find out which one that is. The header is not a row and neither is the
    /// empty space below the last one, and both answer -1 rather than the nearest row: a tooltip
    /// that describes whatever is nearest to a pointer resting on nothing is worse than none.
    /// </remarks>
    public int RowAt(Point point)
    {
        var contentY = point.Y - this.HeaderHeight;
        if (contentY < 0)
            return -1;

        var row = _rows.TopIndex + (contentY / this.ItemHeight);
        return (uint)row < (uint)_rows.Count ? row : -1;
    }

    /// <summary>The node under a point, or null for the header, the empty space, or a stale tree.</summary>
    public TreeNode? NodeAt(Point point)
    {
        var row = this.RowAt(point);
        return row < 0 ? null : _rows[row];
    }

    /// <summary>
    /// Raised for every cell before it is painted, so a caller can draw one itself.
    /// </summary>
    /// <remarks>
    /// The event args are reused between cells — never keep a reference to them past the handler.
    /// The graphics surface is already clipped to the cell.
    /// </remarks>
    public event EventHandler<TreeListViewCellPaintEventArgs>? CellPaint;

    /// <summary>Raised when a column header is clicked, with the column's index.</summary>
    /// <remarks>
    /// Sorting is the caller's to implement — the control has no opinion about what its rows mean.
    /// This reports the gesture that every list makes people expect.
    /// </remarks>
    public event EventHandler<ColumnClickEventArgs>? ColumnClick;

    /// <summary>Raised before <see cref="SelectedNode"/> changes to a node — on every selection path,
    /// mouse, keyboard and assignment alike; set <see cref="TreeViewCancelEventArgs.Cancel"/> to keep
    /// the current selection.</summary>
    public event EventHandler<TreeViewCancelEventArgs>? BeforeSelect;

    /// <summary>Raised after <see cref="SelectedNode"/> changes to a node.</summary>
    public event EventHandler<TreeViewEventArgs>? AfterSelect;

    /// <summary>Raised before a node expands; set <see cref="TreeViewCancelEventArgs.Cancel"/> to veto.</summary>
    public event EventHandler<TreeViewCancelEventArgs>? BeforeExpand;

    /// <summary>Raised after a node expanded.</summary>
    public event EventHandler<TreeViewEventArgs>? AfterExpand;

    /// <summary>Raised before a node collapses; set <see cref="TreeViewCancelEventArgs.Cancel"/> to veto.</summary>
    public event EventHandler<TreeViewCancelEventArgs>? BeforeCollapse;

    /// <summary>Raised after a node collapsed.</summary>
    public event EventHandler<TreeViewEventArgs>? AfterCollapse;

    /// <summary>Raised before a node's <see cref="TreeNode.Checked"/> state changes; set
    /// <see cref="TreeViewCancelEventArgs.Cancel"/> to keep the current state.</summary>
    public event EventHandler<TreeViewCancelEventArgs>? BeforeCheck;

    /// <summary>Raised after a node's <see cref="TreeNode.Checked"/> state changed.</summary>
    public event EventHandler<TreeViewEventArgs>? AfterCheck;

    /// <summary>Expands every node of the tree.</summary>
    public void ExpandAll()
    {
        for (var i = 0; i < this.Nodes.Count; ++i)
            this.Nodes[i].ExpandAll();
    }

    /// <summary>Collapses every node of the tree, descendants included.</summary>
    public void CollapseAll()
    {
        for (var i = 0; i < this.Nodes.Count; ++i)
            this.Nodes[i].Collapse(ignoreChildren: false);
    }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Enter toggles the selected node, so it stays out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter;

    /// <summary>The pixel height reserved for the header row (0 while headers are hidden).</summary>
    /// <summary>
    /// How much of the top of the control the column headers take, or nought when they are hidden.
    /// </summary>
    /// <remarks>
    /// Public alongside <see cref="ItemHeight"/> because the two together are what a caller needs to
    /// reason about where a row is: placing something over a row, or working out which row a point
    /// belongs to, is not possible from outside without both. It was protected while nothing outside
    /// the control had a reason to ask.
    /// </remarks>
    public int HeaderHeight => this.ShowColumnHeaders ? this.ItemHeight : 0;

    /// <summary>The number of fully visible rows in the item area.</summary>
    protected int VisibleRowCount
        => Math.Max(1, (this.Height - this.HeaderHeight - this.HorizontalBarHeight) / this.ItemHeight);

    /// <summary>Whether the tree is showing a scrollbar of its own.</summary>
    private bool HasScrollBar => RowScrollBar.IsNeeded(_rows.Count, this.VisibleRowCount);

    /// <summary>The width the rows and the header have, which is the control's less any bar.</summary>
    protected int ContentWidth => this.Width - (this.HasScrollBar ? this.Theme.ScrollBarSize : 0);

    /// <summary>
    /// The width of the pinned run at the left, which never scrolls.
    /// </summary>
    /// <remarks>
    /// The leading run rather than "every column marked frozen": a pinned third column with two
    /// scrolling ones before it would leave a hole beside it that nothing could fill. So the run
    /// stops at the first column that is not frozen, and anything marked after that is treated as
    /// ordinary — the alternative is a layout that cannot be drawn.
    /// </remarks>
    protected int FrozenWidth
    {
        get
        {
            var total = 0;
            for (var c = 0; c < this.Columns.Count; ++c)
            {
                if (this.Columns[c] is not TreeListViewColumn { Frozen: true })
                    break;

                total += this.Columns[c].Width;
            }

            return total;
        }
    }

    /// <summary>How many columns at the left are pinned.</summary>
    private int FrozenCount
    {
        get
        {
            var count = 0;
            while (count < this.Columns.Count && this.Columns[count] is TreeListViewColumn { Frozen: true })
                ++count;

            return count;
        }
    }

    /// <summary>Everything the columns ask for, whether or not it fits.</summary>
    protected int TotalColumnWidth
    {
        get
        {
            var total = 0;
            for (var c = 0; c < this.Columns.Count; ++c)
                total += this.Columns[c].Width;

            return total;
        }
    }

    /// <summary>
    /// Whether the columns are wider than the control, so there is something to scroll to.
    /// </summary>
    /// <remarks>
    /// Measured against the width less a vertical bar's worth, rather than against
    /// <see cref="ContentWidth"/>: that would ask whether a vertical bar is showing, which asks how
    /// many rows are visible, which asks whether a horizontal bar is taking a row's worth of height.
    /// Assuming the vertical bar shows the horizontal one slightly sooner than strictly necessary,
    /// which is the harmless side of the trade.
    /// </remarks>
    private bool HasHorizontalScrollBar
        => this.Columns.Count > 0 && this.TotalColumnWidth > this.Width - this.Theme.ScrollBarSize;

    private int HorizontalBarHeight => this.HasHorizontalScrollBar ? this.Theme.ScrollBarSize : 0;

    /// <summary>
    /// How far the columns are scrolled sideways, in pixels.
    /// </summary>
    /// <remarks>
    /// Clamped on the way out rather than on the way in, because the columns can change width after
    /// a value is set and an offset that was legal a moment ago must not leave the table showing
    /// blank space to the right of its last column.
    /// </remarks>
    public int HorizontalOffset
    {
        get => Math.Min(field, this.MaxHorizontalOffset);
        set
        {
            field = Math.Max(0, value);
            this.Invalidate();
        }
    }

    /// <summary>
    /// The furthest the table can be scrolled sideways: everything the columns want, less what the
    /// control can show at once. Nought when they already fit.
    /// </summary>
    public int MaxHorizontalOffset
        => Math.Max(0, this.TotalColumnWidth - Math.Max(this.FrozenWidth, this.ContentWidth));

    /// <summary>The strip along the bottom, inside the border and clear of any vertical bar.</summary>
    private Rectangle HorizontalScrollBarStrip
        => new(1, this.Height - this.Theme.ScrollBarSize - 1, Math.Max(0, this.ContentWidth - 2), this.Theme.ScrollBarSize);

    /// <summary>The bar runs below the header, so it never covers a column caption.</summary>
    private Rectangle ScrollBarStrip
        => RowScrollBar.StripOf(this.Theme, this.Width, this.HeaderHeight, this.Height);

    /// <summary>Raises <see cref="BeforeSelect"/>.</summary>
    protected virtual void OnBeforeSelect(TreeViewCancelEventArgs e) => this.BeforeSelect?.Invoke(this, e);

    /// <summary>Raises <see cref="AfterSelect"/>.</summary>
    protected virtual void OnAfterSelect(TreeViewEventArgs e) => this.AfterSelect?.Invoke(this, e);

    /// <summary>Raises <see cref="BeforeExpand"/>.</summary>
    protected virtual void OnBeforeExpand(TreeViewCancelEventArgs e) => this.BeforeExpand?.Invoke(this, e);

    /// <summary>Raises <see cref="AfterExpand"/>.</summary>
    protected virtual void OnAfterExpand(TreeViewEventArgs e) => this.AfterExpand?.Invoke(this, e);

    /// <summary>Raises <see cref="BeforeCollapse"/>.</summary>
    protected virtual void OnBeforeCollapse(TreeViewCancelEventArgs e) => this.BeforeCollapse?.Invoke(this, e);

    /// <summary>Raises <see cref="AfterCollapse"/>.</summary>
    protected virtual void OnAfterCollapse(TreeViewEventArgs e) => this.AfterCollapse?.Invoke(this, e);

    /// <summary>Raises <see cref="BeforeCheck"/>.</summary>
    protected virtual void OnBeforeCheck(TreeViewCancelEventArgs e) => this.BeforeCheck?.Invoke(this, e);

    /// <summary>Raises <see cref="CellPaint"/>.</summary>
    protected virtual void OnCellPaint(TreeListViewCellPaintEventArgs e) => this.CellPaint?.Invoke(this, e);

    /// <summary>Raises <see cref="ColumnClick"/>.</summary>
    protected virtual void OnColumnClick(ColumnClickEventArgs e) => this.ColumnClick?.Invoke(this, e);

    /// <summary>Raises <see cref="AfterCheck"/>.</summary>
    protected virtual void OnAfterCheck(TreeViewEventArgs e) => this.AfterCheck?.Invoke(this, e);

    void ITreeNodeHost.OnBeforeCheck(TreeViewCancelEventArgs e) => this.OnBeforeCheck(e);
    void ITreeNodeHost.OnBeforeExpand(TreeViewCancelEventArgs e) => this.OnBeforeExpand(e);
    void ITreeNodeHost.OnAfterExpand(TreeViewEventArgs e) => this.OnAfterExpand(e);
    void ITreeNodeHost.OnBeforeCollapse(TreeViewCancelEventArgs e) => this.OnBeforeCollapse(e);
    void ITreeNodeHost.OnAfterCollapse(TreeViewEventArgs e) => this.OnAfterCollapse(e);
    void ITreeNodeHost.OnStructureChanged() => this.OnStructureChanged();
    void ITreeNodeHost.OnNodeChecked(TreeNode node) => this.OnNodeChecked(node);
    void ITreeNodeHost.ScrollNodeIntoView(TreeNode node) => this.ScrollNodeIntoView(node);

    /// <summary>
    /// Replaces the tree from a data source: one node per item, labeled via <paramref name="text"/>,
    /// nested via <paramref name="children"/> and carrying the item in <see cref="TreeNode.Tag"/>.
    /// The hierarchy is built eagerly, cut off after <paramref name="maxDepth"/> levels so cyclic
    /// object graphs terminate.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="roots">The root items.</param>
    /// <param name="text">Maps an item to its node label.</param>
    /// <param name="children">Maps an item to its child items; <see langword="null"/> for a leaf.</param>
    /// <param name="maxDepth">The maximum number of levels built. Defaults to 32.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDepth"/> is zero or negative.</exception>
    public void SetDataSource<T>(IEnumerable<T> roots, Func<T, string> text, Func<T, IEnumerable<T>?> children, int maxDepth = 32)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDepth, 0);

        this.Nodes.Clear();
        foreach (var item in roots)
            this.Nodes.Add(BuildNode(item, text, children, maxDepth - 1));
    }

    private static TreeNode BuildNode<T>(T item, Func<T, string> text, Func<T, IEnumerable<T>?> children, int remainingDepth)
    {
        var node = new TreeNode(text(item)) { Tag = item };
        if (remainingDepth <= 0)
            return node;

        var kids = children(item);
        if (kids is null)
            return node;

        foreach (var kid in kids)
            node.Nodes.Add(BuildNode(kid, text, children, remainingDepth - 1));

        return node;
    }

    /// <summary>Called by nodes/collections after any structural change: re-flatten lazily and repaint.</summary>
    internal void OnStructureChanged()
    {
        _rows.MarkDirty();
        if (_selectedNode is not null && !ReferenceEquals(_selectedNode.Host, this))
            _selectedNode = null;

        this.Invalidate();
    }

    /// <summary>Called by a node after its check state changed.</summary>
    internal void OnNodeChecked(TreeNode node)
    {
        this.Invalidate();
        this.OnAfterCheck(new TreeViewEventArgs(node));
    }

    /// <summary>Scrolls so the given (visible) node's row is inside the client area.</summary>
    internal void ScrollNodeIntoView(TreeNode node)
    {
        if (_rows.ScrollIntoView(node))
            this.Invalidate();
    }

    /// <summary>Follows every column's <see cref="ColumnHeader.Changed"/> so width edits repaint.</summary>
    private void OnColumnsChanged(object? sender, ListChangedEventArgs e)
    {
        for (var i = 0; i < _watchedColumns.Count; ++i)
            _watchedColumns[i].Changed -= this.OnColumnChanged;

        _watchedColumns.Clear();
        for (var i = 0; i < this.Columns.Count; ++i)
        {
            var column = this.Columns[i];
            column.Changed += this.OnColumnChanged;
            _watchedColumns.Add(column);
        }

        this.Invalidate();
    }

    private void OnColumnChanged(object? sender, EventArgs e) => this.Invalidate();

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.HasScrollBar)
        {
            var scrolled = _scrollBar.MouseDown(
                this.Theme, this.ScrollBarStrip, _rows.Count, this.VisibleRowCount, _rows.TopIndex, e.Location);

            if (scrolled >= 0)
            {
                _lastClickNode = null;
                _rows.ScrollTo(scrolled);
                this.Invalidate();
                return;
            }
        }

        if (this.HasHorizontalScrollBar && this.HorizontalScrollBarStrip.Contains(e.Location))
        {
            // Anywhere on the strip grabs it: the thumb is small on a wide table, and a click that
            // does nothing because it missed by two pixels reads as a table that cannot scroll.
            _lastClickNode = null;
            _horizontalDragging = true;
            _horizontalGrabX = e.X;
            _horizontalGrabOffset = this.HorizontalOffset;
            return;
        }

        var contentY = e.Y - this.HeaderHeight;
        if (contentY < 0)
        {
            _lastClickNode = null;
            // In the columns' own coordinates, not the control's, or a scrolled table sorts by
            // whichever column happens to be under the pointer's unscrolled position.
            this.RaiseColumnClick(this.ColumnCoordinate(e.X));
            return;
        }

        var count = _rows.Count;
        var row = _rows.TopIndex + (contentY / this.ItemHeight);
        if (row < 0 || row >= count)
        {
            _lastClickNode = null;
            return;
        }

        var node = _rows[row];
        var indent = this.ItemHeight;
        // Column coordinates: the glyph and the check box moved with their column when the table
        // scrolled, so the pointer has to be put back into the same frame they were drawn in.
        var pointerX = this.ColumnCoordinate(e.X);
        var glyphCellLeft = node.Level * indent;
        var contentLeft = glyphCellLeft + indent;

        // The glyph/check cells only react inside the tree column — painting clips them there, so a
        // click on a neighboring column always selects even when a deep node's cells would overlap.
        var treeCellRight = this.Columns.Count == 0 ? this.Width : this.Columns[0].Width;
        var inTreeCell = pointerX < treeCellRight;

        if (inTreeCell && node.HasChildren && pointerX >= glyphCellLeft && pointerX < contentLeft)
        {
            _lastClickNode = null;
            node.Toggle();
            return;
        }

        if (inTreeCell && this.CheckBoxes && pointerX >= contentLeft && pointerX < contentLeft + _CheckCellWidth)
        {
            _lastClickNode = null;
            node.Checked = !node.Checked;
            return;
        }

        this.SelectedNode = node;

        var now = Environment.TickCount64;
        if (ReferenceEquals(node, _lastClickNode) && now - _lastClickTicks <= this.Theme.DoubleClickTime)
        {
            _lastClickNode = null;
            node.Toggle();
            return;
        }

        _lastClickNode = node;
        _lastClickTicks = now;
    }

    /// <summary>Maps an x inside the header onto a column and reports the click.</summary>
    /// <summary>
    /// Turns a position on the control into a position in the columns.
    /// </summary>
    /// <remarks>
    /// A pinned column is where it looks; everything else moved left by the scroll offset. Asking
    /// this rather than adding the offset unconditionally is what stops a click on a pinned caption
    /// from selecting whichever column has scrolled underneath it.
    /// </remarks>
    private int ColumnCoordinate(int x)
    {
        var frozenWidth = this.FrozenWidth;
        return x < frozenWidth ? x : x + this.HorizontalOffset;
    }

    private void RaiseColumnClick(int x)
    {
        if (this.ColumnClick is null || this.HeaderHeight <= 0)
            return;

        var left = 0;
        for (var i = 0; i < this.Columns.Count; ++i)
        {
            var width = this.Columns[i].Width;
            if (x >= left && x < left + width)
            {
                this.OnColumnClick(new(i));
                return;
            }

            left += width;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // A table wider than its control scrolls sideways on a shifted wheel, which is what every
        // other table does and what a hand reaches for without being told.
        if ((e.Modifiers & KeyModifiers.Shift) != 0 && this.HasHorizontalScrollBar)
        {
            this.HorizontalOffset -= Math.Sign(e.Delta) * this.ItemHeight * 3;
            return;
        }

        _rows.ScrollBy(-Math.Sign(e.Delta) * 3);
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_horizontalDragging)
        {
            // The thumb covers the viewport's share of the whole width, so the table travels further
            // than the pointer does, in that proportion.
            var strip = Math.Max(1, this.HorizontalScrollBarStrip.Width);
            this.HorizontalOffset = _horizontalGrabOffset
                + ((e.X - _horizontalGrabX) * this.TotalColumnWidth / strip);
            return;
        }

        if (!_scrollBar.IsDragging)
            return;

        _rows.ScrollTo(_scrollBar.Drag(this.ScrollBarStrip, _rows.Count, this.VisibleRowCount, e.Y));
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _scrollBar.Release();
        _horizontalDragging = false;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
        => e.Handled = TreeNavigation.HandleKey(this, _rows, this.VisibleRowCount, this.CheckBoxes, e);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, this.Height));

        var rowHeight = this.ItemHeight;
        var headerHeight = this.HeaderHeight;
        if (headerHeight > 0)
            HeaderRowPainter.Draw(g, theme, this.Columns, this.ContentWidth, headerHeight, this.HorizontalOffset, this.FrozenCount);

        var top = _rows.TopIndex;
        var last = Math.Min(_rows.Count, top + this.VisibleRowCount + 1);
        for (var i = top; i < last; ++i)
            this.PaintRow(g, theme, _rows[i], headerHeight + ((i - top) * rowHeight), rowHeight);

        if (this.HasScrollBar)
            _scrollBar.Paint(g, theme, this.ScrollBarStrip, _rows.Count, this.VisibleRowCount, _rows.TopIndex);

        if (this.HasHorizontalScrollBar)
        {
            // The same renderer the standalone bar uses, driven straight from the offset so the
            // thumb cannot drift from what is actually drawn.
            var scrollable = this.TotalColumnWidth;
            ScrollBarRenderer.Paint(
                g,
                theme,
                this.HorizontalScrollBarStrip,
                vertical: false,
                0,
                Math.Max(0, scrollable - 1),
                this.HorizontalOffset,
                Math.Max(1, scrollable - this.MaxHorizontalOffset),
                _horizontalDragging ? ScrollBarPart.Thumb : ScrollBarPart.None);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintRow(IGraphics g, ITheme theme, TreeNode node, int y, int rowHeight)
    {
        var width = this.ContentWidth;
        var selected = ReferenceEquals(node, _selectedNode);
        if (selected)
            GlyphRenderer.FillSelection(g, theme, new Rectangle(0, y, width, rowHeight));
        else if (this.RowBackColorSelector?.Invoke(node) is { } back)
            g.FillRectangle(back, new Rectangle(0, y, width, rowHeight));

        var textColor = selected
            ? theme.SelectionText
            : this.RowForeColorSelector?.Invoke(node) ?? theme.ControlText;
        if (this.Columns.Count == 0)
        {
            this.PaintTreeCell(g, theme, node, selected, textColor, width, y, rowHeight);
            return;
        }

        // Twice: the scrolling columns, then the pinned run over whatever slid underneath it. Drawing
        // them in one pass would leave a scrolled cell on top of the column that is supposed to be
        // holding still.
        var frozen = this.FrozenCount;
        for (var pass = 0; pass < 2; ++pass)
        {
        var x = pass == 0 ? -this.HorizontalOffset : 0;
        for (var c = 0; c < this.Columns.Count; ++c)
        {
            var col = this.Columns[c];
            if ((pass == 0 && c < frozen) || (pass == 1 && c >= frozen))
            {
                x += col.Width;
                continue;
            }

            var cell = new Rectangle(x, y, col.Width, rowHeight);
            g.PushClip(cell);

            var handled = false;
            if (this.CellPaint is not null)
            {
                _cellPaintArgs.Rebind(g, theme, node, c, cell, selected);
                this.OnCellPaint(_cellPaintArgs);
                handled = _cellPaintArgs.Handled;
            }

            if (!handled)
            {
                if (c == 0)
                    this.PaintTreeCell(g, theme, node, selected, textColor, col.Width, y, rowHeight, x);
                else
                {
                    var text = col.TextSelector?.Invoke(node) ?? string.Empty;
                    var textRect = new Rectangle(x + _CellPad, y, col.Width - (2 * _CellPad), rowHeight);
                    g.DrawText(text, theme.DefaultFont, textColor, textRect, col.TextAlign);
                }
            }

            g.PopClip();
            x += col.Width;
        }
        }
    }

    /// <param name="left">
    /// Where the tree column begins on screen, which is not nought once the table is scrolled
    /// sideways. The glyph and the check box hang off it, so passing it in is what keeps them with
    /// their own column rather than pinned to the edge of the control.
    /// </param>
    private void PaintTreeCell(IGraphics g, ITheme theme, TreeNode node, bool selected, Color textColor, int width, int y, int rowHeight, int left = 0)
    {
        var indent = rowHeight;
        var glyphCellLeft = left + (node.Level * indent);
        var contentLeft = glyphCellLeft + indent;

        if (node.HasChildren)
            ExpandGlyph.Draw(g, theme, glyphCellLeft, y, indent, rowHeight, node.IsExpanded);

        var x = contentLeft;
        if (this.CheckBoxes)
        {
            var boxTop = y + ((rowHeight - GlyphRenderer.CheckBoxSize) / 2);
            GlyphRenderer.DrawCheckBox(g, theme, new(x + 2, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), node.Checked);
            x += _CheckCellWidth;
        }

        x = this.PaintImage(g, node, selected, x, y, rowHeight);

        var textRect = new Rectangle(x + _TextPad, y, width - x - (2 * _TextPad), rowHeight);
        g.DrawText(node.Text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
    }

    private int PaintImage(IGraphics g, TreeNode node, bool selected, int x, int y, int rowHeight)
    {
        var images = this.ImageList;
        var backend = this.Backend;
        if (images is null || backend is null)
            return x;

        var index = node.ResolveIconIndex(images, selected);
        if (index < 0 || index >= images.Count)
            return x;

        var iconSize = rowHeight - 4;
        g.DrawImage(images.GetImage(index, backend), new Rectangle(x + _TextPad, y + 2, iconSize, iconSize));
        return x + iconSize + _IconGap;
    }
}
