using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn tree view painted in the native theme: hierarchical <see cref="TreeNode"/>s with
/// expand/collapse glyphs, optional connector lines, per-node icons from an <see cref="ImageList"/>,
/// optional check boxes, single selection and wheel/keyboard scrolling. The expanded part of the tree
/// is flattened into a list of visible rows and painting is virtualized to the rows intersecting the
/// client area, so it stays cheap for very large trees.
/// </summary>
/// <remarks>
/// Connector lines are drawn solid in the theme's disabled-text color because <see cref="IGraphics"/>
/// has no dashed strokes. TODO: label editing (<c>BeginEdit</c>, waits on the text-box overlay),
/// multi-selection, drag and drop, and a virtual-mode node API.
/// </remarks>
public class TreeView : OwnerDrawnControl, ITreeNodeHost
{
    private const int _CheckCellWidth = GlyphRenderer.CheckBoxSize + 4;
    private const int _IconGap = 4;
    private const int _TextPad = 2;

    private readonly TreeRowList _rows;
    private TreeNode? _selectedNode;
    private int? _itemHeight;
    private TreeNode? _lastClickNode;
    private long _lastClickTicks;

    /// <summary>Creates a tree view.</summary>
    public TreeView()
    {
        this.Nodes = new(this);
        _rows = new(this.Nodes, () => this.VisibleRowCount);
    }

    /// <summary>The root nodes. Mutating any level of the hierarchy re-flattens and repaints.</summary>
    public TreeNodeCollection Nodes { get; }

    /// <summary>The selected node, or <see langword="null"/>. Setting it scrolls the node into view.</summary>
    /// <exception cref="ArgumentException">The node belongs to a different tree.</exception>
    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
                return;

            if (value is not null && !ReferenceEquals(value.Host, this))
                throw new ArgumentException("The node is not attached to this tree.", nameof(value));

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

    /// <summary>Whether every node shows a themed check box. Defaults to <see langword="false"/>.</summary>
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

    /// <summary>The pixel height of a row (also the indent per level). Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            this.Invalidate();
        }
    }

    /// <summary>Whether connector lines are drawn between nodes. Defaults to <see langword="true"/>.</summary>
    public bool ShowLines
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

    /// <summary>Whether expand/collapse glyphs are drawn for parent nodes. Defaults to <see langword="true"/>.</summary>
    public bool ShowPlusMinus
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

    /// <summary>
    /// Whether root nodes get their own glyph/line cell. When <see langword="false"/>, everything
    /// shifts one indent level left and roots lose their glyphs. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ShowRootLines
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

    /// <summary>The number of fully visible rows in the client area.</summary>
    protected int VisibleRowCount => Math.Max(1, this.Height / this.ItemHeight);

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

    /// <summary>The left edge of a node's glyph/line cell; negative when that cell is suppressed.</summary>
    private int GlyphCellLeft(TreeNode node)
        => (this.ShowRootLines ? node.Level : node.Level - 1) * this.ItemHeight;

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var count = _rows.Count;
        var row = _rows.TopIndex + (e.Y / this.ItemHeight);
        if (row < 0 || row >= count)
        {
            _lastClickNode = null;
            return;
        }

        var node = _rows[row];
        var indent = this.ItemHeight;
        var glyphCellLeft = this.GlyphCellLeft(node);
        var contentLeft = glyphCellLeft + indent;

        if (this.ShowPlusMinus && node.HasChildren && glyphCellLeft >= 0 && e.X >= glyphCellLeft && e.X < contentLeft)
        {
            _lastClickNode = null;
            node.Toggle();
            return;
        }

        if (this.CheckBoxes && e.X >= contentLeft && e.X < contentLeft + _CheckCellWidth)
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
        var top = _rows.TopIndex;
        var last = Math.Min(_rows.Count, top + this.VisibleRowCount + 1);
        for (var i = top; i < last; ++i)
            this.PaintRow(g, theme, _rows[i], (i - top) * rowHeight, rowHeight);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintRow(IGraphics g, ITheme theme, TreeNode node, int y, int rowHeight)
    {
        var selected = ReferenceEquals(node, _selectedNode);
        if (selected)
            GlyphRenderer.FillSelection(g, theme, new Rectangle(0, y, this.Width, rowHeight));

        var indent = rowHeight;
        var glyphCellLeft = this.GlyphCellLeft(node);
        var contentLeft = glyphCellLeft + indent;

        if (this.ShowLines)
            this.PaintLines(g, theme, node, y, rowHeight, glyphCellLeft, contentLeft);

        if (this.ShowPlusMinus && node.HasChildren && glyphCellLeft >= 0)
            ExpandGlyph.Draw(g, theme, glyphCellLeft, y, indent, rowHeight, node.IsExpanded);

        var x = contentLeft;
        if (this.CheckBoxes)
        {
            var boxTop = y + ((rowHeight - GlyphRenderer.CheckBoxSize) / 2);
            GlyphRenderer.DrawCheckBox(g, theme, new(x + 2, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), node.Checked);
            x += _CheckCellWidth;
        }

        x = this.PaintImage(g, node, selected, x, y, rowHeight);

        var textColor = selected ? theme.SelectionText : theme.ControlText;
        var textRect = new Rectangle(x + _TextPad, y, this.Width - x - (2 * _TextPad), rowHeight);
        g.DrawText(node.Text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
    }

    private void PaintLines(IGraphics g, ITheme theme, TreeNode node, int y, int rowHeight, int glyphCellLeft, int contentLeft)
    {
        // Solid faint connectors — IGraphics has no dashed strokes, so the classic dotted look is
        // approximated with the disabled-text color.
        var color = theme.DisabledText;
        var midY = y + (rowHeight / 2);
        if (glyphCellLeft >= 0)
        {
            var midX = glyphCellLeft + (rowHeight / 2);
            g.DrawLine(color, midX, midY, contentLeft, midY);
            if (node.HasPreviousSibling || node.Parent is not null)
                g.DrawLine(color, midX, y, midX, midY);

            if (node.HasNextSibling)
                g.DrawLine(color, midX, midY, midX, y + rowHeight);
        }

        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            var cellLeft = this.GlyphCellLeft(ancestor);
            if (cellLeft >= 0 && ancestor.HasNextSibling)
            {
                var midX = cellLeft + (rowHeight / 2);
                g.DrawLine(color, midX, y, midX, y + rowHeight);
            }
        }
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
