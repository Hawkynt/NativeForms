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
/// multi-selection, and a virtual-mode node API.
/// </remarks>
public class TreeView : OwnerDrawnControl, ITreeNodeHost
{
    private const int _CheckCellWidth = GlyphRenderer.CheckBoxSize + 4;
    private const int _IconGap = 4;
    private const int _TextPad = 2;

    private const int _DragThreshold = 4;   // pixels the pointer must travel before a press becomes a drag

    private readonly TreeRowList _rows;
    private TreeNode? _selectedNode;
    private int? _itemHeight;
    private TreeNode? _lastClickNode;
    private long _lastClickTicks;

    // Drag-and-drop state. All null/zero until a press on a node with AllowDrop on.
    private TreeNode? _pressedNode;   // node under the last left mouse-down — a drag candidate
    private int _pressX, _pressY;
    private TreeNode? _dragNode;      // the node being dragged (non-null iff a drag is in flight)
    private TreeNode? _dropTarget;    // node the pointer is over this frame
    private int _dropRow;             // its flattened row index, for placing the marker
    private TreeViewDropLocation _dropLocation;
    private bool _dropValid;          // whether the current target accepts the drop (drawn + droppable)
    private Point _dragPoint;         // the pointer, for the translucent drag image
    private Timer? _autoExpandTimer;

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

            this.BindImageListAnimation(field, value);
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

    /// <summary>
    /// Whether nodes can be dragged within the tree to reorder or reparent them, with a live insertion
    /// marker. Off by default. While on, pressing a node and dragging past a few pixels starts a drag
    /// (<see cref="ItemDrag"/>); the pointer's position within the target row picks above / onto / below;
    /// <see cref="NodeDragOver"/> can reject a target and <see cref="NodeDrop"/> can veto the release. A
    /// node is never droppable into its own subtree. This is intra-tree node movement, distinct from the
    /// cross-control <see cref="Control.AllowDrop"/> data drag.
    /// </summary>
    public bool AllowReorder
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value)
                this.CancelDrag();
        }
    }

    /// <summary>
    /// How long the pointer must dwell on a collapsed node while dragging before it auto-expands, in
    /// milliseconds. Defaults to 700; set to 0 to disable hover-expansion. Only active during a drag.
    /// </summary>
    public int AutoExpandDelay
    {
        get => field;
        set => field = Math.Max(0, value);
    } = 700;

    /// <summary>
    /// Whether a drag carries a translucent image of the dragged node (its icon and label) under the
    /// pointer, MWTreeView-style. On by default; set to <see langword="false"/> for a marker-only drag.
    /// </summary>
    public bool ShowDragImage { get; set; } = true;

    /// <summary>Raised when a node starts being dragged (the pointer crossed the drag threshold).</summary>
    public event EventHandler<TreeViewEventArgs>? ItemDrag;

    /// <summary>Raised continuously as the drop target changes during a drag; set
    /// <see cref="TreeNodeDragEventArgs.Cancel"/> to reject the current target (no marker, no drop).</summary>
    public event EventHandler<TreeNodeDragEventArgs>? NodeDragOver;

    /// <summary>Raised when a dragged node is released over a valid target; set
    /// <see cref="TreeNodeDragEventArgs.Cancel"/> to abort the move.</summary>
    public event EventHandler<TreeNodeDragEventArgs>? NodeDrop;

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

    /// <summary>Raises <see cref="ItemDrag"/>.</summary>
    protected virtual void OnItemDrag(TreeViewEventArgs e) => this.ItemDrag?.Invoke(this, e);

    /// <summary>Raises <see cref="NodeDragOver"/>.</summary>
    protected virtual void OnNodeDragOver(TreeNodeDragEventArgs e) => this.NodeDragOver?.Invoke(this, e);

    /// <summary>Raises <see cref="NodeDrop"/>.</summary>
    protected virtual void OnNodeDrop(TreeNodeDragEventArgs e) => this.NodeDrop?.Invoke(this, e);

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

        if (this.AllowReorder)
        {
            _pressedNode = node;
            _pressX = e.X;
            _pressY = e.Y;
        }

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
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragNode is not null)
        {
            _dragPoint = new Point(e.X, e.Y);
            this.UpdateDropTarget(e.Y);
            return;
        }

        // The press flag, not the move's button field, is the "button is down" signal — platform
        // motion events don't reliably carry it, so tracking it from the down/up pair is portable.
        if (_pressedNode is null)
            return;

        if (Math.Abs(e.X - _pressX) < _DragThreshold && Math.Abs(e.Y - _pressY) < _DragThreshold)
            return;

        _dragNode = _pressedNode;
        _dragPoint = new Point(e.X, e.Y);
        this.OnItemDrag(new TreeViewEventArgs(_dragNode));
        this.UpdateDropTarget(e.Y);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressedNode = null;
        var dragged = _dragNode;
        if (dragged is null)
            return;

        var target = _dropTarget;
        var location = _dropLocation;
        var valid = _dropValid;
        this.CancelDrag();

        if (!valid)
            return;

        var e2 = new TreeNodeDragEventArgs(dragged, target, location);
        this.OnNodeDrop(e2);
        if (e2.Cancel)
            return;

        this.PerformDrop(dragged, target, location);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_dragNode is null)
            _pressedNode = null;
    }

    /// <summary>Recomputes the drop target/location from a client y, validates it, and repaints.</summary>
    private void UpdateDropTarget(int y)
    {
        this.ComputeDrop(y, out var target, out var row, out var location);
        var changed = !ReferenceEquals(target, _dropTarget) || location != _dropLocation;
        _dropTarget = target;
        _dropRow = row;
        _dropLocation = location;

        var valid = IsDroppable(_dragNode!, target, location);
        if (valid)
        {
            var e = new TreeNodeDragEventArgs(_dragNode!, target, location);
            this.OnNodeDragOver(e);
            valid = !e.Cancel;
        }

        _dropValid = valid;
        if (changed)
            this.RestartAutoExpand();

        this.Invalidate();
    }

    /// <summary>Maps a client y to the row under it and which third of that row the pointer is in.</summary>
    private void ComputeDrop(int y, out TreeNode? target, out int row, out TreeViewDropLocation location)
    {
        var count = _rows.Count;
        if (count == 0)
        {
            target = null;
            row = 0;
            location = TreeViewDropLocation.Below;
            return;
        }

        if (y < 0)
            y = 0;

        var rowHeight = this.ItemHeight;
        row = _rows.TopIndex + (y / rowHeight);
        if (row >= count)
        {
            // Past the last visible row: land after the last node.
            row = count - 1;
            target = _rows[row];
            location = TreeViewDropLocation.Below;
            return;
        }

        target = _rows[row];
        var offset = y - ((row - _rows.TopIndex) * rowHeight);
        var band = rowHeight / 4;
        location = offset < band ? TreeViewDropLocation.Above
            : offset >= rowHeight - band ? TreeViewDropLocation.Below
            : TreeViewDropLocation.Onto;
    }

    /// <summary>Whether <paramref name="dragged"/> may land on <paramref name="target"/>: never onto
    /// itself and never into its own subtree.</summary>
    private static bool IsDroppable(TreeNode dragged, TreeNode? target, TreeViewDropLocation location)
    {
        if (target is null)
            return location == TreeViewDropLocation.Below; // append to the root's end

        if (ReferenceEquals(target, dragged))
            return false;

        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, dragged))
                return false;

        return true;
    }

    /// <summary>Reparents/reorders the dragged node at the resolved target and selects it.</summary>
    private void PerformDrop(TreeNode dragged, TreeNode? target, TreeViewDropLocation location)
    {
        dragged.OwnerCollection?.Remove(dragged);

        if (target is null)
            this.Nodes.Add(dragged);
        else if (location == TreeViewDropLocation.Onto)
        {
            target.Nodes.Add(dragged);
            target.Expand();
        }
        else
        {
            // The collection reindexes on the removal above, so the target's index is already current.
            var siblings = target.OwnerCollection ?? this.Nodes;
            var index = target.SiblingIndex + (location == TreeViewDropLocation.Below ? 1 : 0);
            siblings.Insert(index, dragged);
        }

        this.SelectedNode = dragged;
        dragged.EnsureVisible();
    }

    private Timer EnsureAutoExpandTimer()
    {
        if (_autoExpandTimer is not null)
            return _autoExpandTimer;

        _autoExpandTimer = new Timer();
        _autoExpandTimer.Tick += (_, _) => this.AutoExpandTick();
        return _autoExpandTimer;
    }

    /// <summary>Restarts the hover-expand dwell for a fresh target; only a collapsed parent under an
    /// "onto" drop arms it.</summary>
    private void RestartAutoExpand()
    {
        var timer = _autoExpandTimer;
        if (timer is not null)
            timer.Enabled = false;

        if (this.AutoExpandDelay <= 0 || _dropLocation != TreeViewDropLocation.Onto)
            return;

        if (_dropTarget is not { IsExpanded: false, HasChildren: true })
            return;

        timer = this.EnsureAutoExpandTimer();
        timer.Interval = this.AutoExpandDelay;
        timer.Enabled = true;
    }

    /// <summary>Expands the hovered collapsed target once its dwell elapses. Internal so a headless
    /// test can drive it without the platform timer ticking.</summary>
    internal void AutoExpandTick()
    {
        _autoExpandTimer?.Stop();
        if (_dragNode is null || _dropLocation != TreeViewDropLocation.Onto)
            return;

        if (_dropTarget is { IsExpanded: false, HasChildren: true } target)
        {
            target.Expand();
            this.UpdateDropTarget((this._dropRow - _rows.TopIndex) * this.ItemHeight + (this.ItemHeight / 2));
        }
    }

    /// <summary>Ends any in-flight drag and clears the preview.</summary>
    private void CancelDrag()
    {
        _autoExpandTimer?.Stop();
        var wasDragging = _dragNode is not null;
        _dragNode = null;
        _dropTarget = null;
        _dropValid = false;
        if (wasDragging)
            this.Invalidate();
    }

    /// <summary>The current drop preview, exposed for headless drag tests.</summary>
    internal (bool Dragging, TreeNode? Target, TreeViewDropLocation Location, bool Valid) DropPreview
        => (_dragNode is not null, _dropTarget, _dropLocation, _dropValid);

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

        if (_dragNode is not null && _dropValid)
            this.PaintDropMarker(g, theme, rowHeight);

        if (_dragNode is not null && this.ShowDragImage)
            this.PaintDragImage(g, theme, rowHeight);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>Paints a translucent image of the dragged node — its icon and label — under the
    /// pointer, so the drag reads like it is carrying the row.</summary>
    private void PaintDragImage(IGraphics g, ITheme theme, int rowHeight)
    {
        var node = _dragNode!;
        var font = theme.DefaultFont;
        var iconSize = rowHeight - 4;

        var images = this.ImageList;
        var iconIndex = images is not null ? node.ResolveIconIndex(images, selected: false) : -1;
        var hasIcon = images is not null && this.Backend is not null && iconIndex >= 0 && iconIndex < images.Count;
        var iconWidth = hasIcon ? iconSize + _IconGap : 0;

        var width = _TextPad + iconWidth + g.MeasureText(node.Text, font).Width + (2 * _TextPad);
        var x = Math.Max(0, Math.Min(_dragPoint.X + 12, this.Width - width - 2));
        var y = Math.Max(0, Math.Min(_dragPoint.Y + 4, this.Height - rowHeight - 2));
        var rect = new Rectangle(x, y, width, rowHeight);

        // A half-alpha chip so the rows beneath still read through it, MWTreeView-style.
        g.FillRectangle(Color.FromArgb(128, theme.SelectionBackground), rect);
        g.DrawRectangle(Color.FromArgb(160, theme.Accent), new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1));

        var contentX = x + _TextPad;
        if (hasIcon)
        {
            g.DrawImage(images!.GetImage(iconIndex, this.Backend!), new Rectangle(contentX, y + 2, iconSize, iconSize));
            contentX += iconSize + _IconGap;
        }

        g.DrawText(node.Text, font, Color.FromArgb(200, theme.ControlText), new Rectangle(contentX, y, Math.Max(0, rect.Right - _TextPad - contentX), rowHeight), ContentAlignment.MiddleLeft);
    }

    /// <summary>Paints the drag preview: an outline around an "onto" target, or an indented insertion
    /// line with end ticks between rows for a sibling drop.</summary>
    private void PaintDropMarker(IGraphics g, ITheme theme, int rowHeight)
    {
        var color = theme.Accent;
        var y = (_dropRow - _rows.TopIndex) * rowHeight;

        if (_dropLocation == TreeViewDropLocation.Onto)
        {
            g.DrawRectangle(color, new Rectangle(0, y, this.Width - 1, rowHeight - 1));
            return;
        }

        var left = Math.Max(0, this.GlyphCellLeft(_dropTarget!) + rowHeight);
        var markerY = _dropLocation == TreeViewDropLocation.Above ? y : y + rowHeight;
        markerY = Math.Clamp(markerY, 1, this.Height - 2);
        var right = this.Width - 2;
        g.DrawLine(color, left, markerY, right, markerY);
        g.DrawLine(color, left, markerY - 2, left, markerY + 2);
        g.DrawLine(color, right, markerY - 2, right, markerY + 2);
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
