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
/// TODO: column sorting, interactive column resize and label editing.
/// </remarks>
public class TreeListView : OwnerDrawnControl, ITreeNodeHost
{
    private const int _CheckCellWidth = GlyphRenderer.CheckBoxSize + 4;
    private const int _IconGap = 4;
    private const int _TextPad = 2;
    private const int _CellPad = 2;
    private const int _DoubleClickMs = 500;

    private readonly TreeRowList _rows;
    private readonly List<TreeListViewColumn> _watchedColumns = [];
    private TreeNode? _selectedNode;
    private int? _itemHeight;
    private TreeNode? _lastClickNode;
    private long _lastClickTicks;

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
    public int TopIndex => _rows.TopIndex;

    /// <summary>The number of rows the expanded part of the tree currently occupies.</summary>
    public int VisibleNodeCount => _rows.Count;

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

    /// <summary>Raised after a node's <see cref="TreeNode.Checked"/> state changed.</summary>
    public event EventHandler<TreeViewEventArgs>? AfterCheck;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The pixel height reserved for the header row (0 while headers are hidden).</summary>
    protected int HeaderHeight => this.ShowColumnHeaders ? this.ItemHeight : 0;

    /// <summary>The number of fully visible rows in the item area.</summary>
    protected int VisibleRowCount => Math.Max(1, (this.Height - this.HeaderHeight) / this.ItemHeight);

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

    /// <summary>Raises <see cref="AfterCheck"/>.</summary>
    protected virtual void OnAfterCheck(TreeViewEventArgs e) => this.AfterCheck?.Invoke(this, e);

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

        var contentY = e.Y - this.HeaderHeight;
        if (contentY < 0)
        {
            _lastClickNode = null;
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
        var glyphCellLeft = node.Level * indent;
        var contentLeft = glyphCellLeft + indent;

        // The glyph/check cells only react inside the tree column — painting clips them there, so a
        // click on a neighboring column always selects even when a deep node's cells would overlap.
        var treeCellRight = this.Columns.Count == 0 ? this.Width : this.Columns[0].Width;
        var inTreeCell = e.X < treeCellRight;

        if (inTreeCell && node.HasChildren && e.X >= glyphCellLeft && e.X < contentLeft)
        {
            _lastClickNode = null;
            node.Toggle();
            return;
        }

        if (inTreeCell && this.CheckBoxes && e.X >= contentLeft && e.X < contentLeft + _CheckCellWidth)
        {
            _lastClickNode = null;
            node.Checked = !node.Checked;
            return;
        }

        this.SelectedNode = node;

        var now = Environment.TickCount64;
        if (ReferenceEquals(node, _lastClickNode) && now - _lastClickTicks <= _DoubleClickMs)
        {
            _lastClickNode = null;
            node.Toggle();
            return;
        }

        _lastClickNode = node;
        _lastClickTicks = now;
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _rows.ScrollBy(-Math.Sign(e.Delta) * 3);
        this.Invalidate();
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
            HeaderRowPainter.Draw(g, theme, this.Columns, this.Width, headerHeight);

        var top = _rows.TopIndex;
        var last = Math.Min(_rows.Count, top + this.VisibleRowCount + 1);
        for (var i = top; i < last; ++i)
            this.PaintRow(g, theme, _rows[i], headerHeight + ((i - top) * rowHeight), rowHeight);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintRow(IGraphics g, ITheme theme, TreeNode node, int y, int rowHeight)
    {
        var selected = ReferenceEquals(node, _selectedNode);
        if (selected)
            g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, this.Width, rowHeight));

        var textColor = selected ? theme.SelectionText : theme.ControlText;
        if (this.Columns.Count == 0)
        {
            this.PaintTreeCell(g, theme, node, selected, textColor, this.Width, y, rowHeight);
            return;
        }

        var x = 0;
        for (var c = 0; c < this.Columns.Count; ++c)
        {
            var col = this.Columns[c];
            g.PushClip(new Rectangle(x, y, col.Width, rowHeight));
            if (c == 0)
                this.PaintTreeCell(g, theme, node, selected, textColor, col.Width, y, rowHeight);
            else
            {
                var text = col.TextSelector?.Invoke(node) ?? string.Empty;
                var textRect = new Rectangle(x + _CellPad, y, col.Width - (2 * _CellPad), rowHeight);
                g.DrawText(text, theme.DefaultFont, textColor, textRect, col.TextAlign);
            }

            g.PopClip();
            x += col.Width;
        }
    }

    private void PaintTreeCell(IGraphics g, ITheme theme, TreeNode node, bool selected, Color textColor, int width, int y, int rowHeight)
    {
        var indent = rowHeight;
        var glyphCellLeft = node.Level * indent;
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

        var index = selected && node.SelectedImageIndex >= 0 ? node.SelectedImageIndex : node.ImageIndex;
        if (index < 0 || index >= images.Count)
            return x;

        var iconSize = rowHeight - 4;
        g.DrawImage(images.GetImage(index, backend), new Rectangle(x + _TextPad, y + 2, iconSize, iconSize));
        return x + iconSize + _IconGap;
    }
}
