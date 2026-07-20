using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A <see cref="Panel"/> that slices its client area into a <see cref="ColumnCount"/> ×
/// <see cref="RowCount"/> grid and fills each child into its cell minus the child's
/// <see cref="Control.Margin"/>. Tracks are sized by <see cref="ColumnStyles"/>/<see cref="RowStyles"/>
/// (absolute pixels, auto-sized to the largest child, or a percent share of what is left); unstyled
/// tracks share the remaining space equally. Children without an explicit
/// <see cref="SetCellPosition"/> auto-place row-major into free cells, spans stretch across
/// neighboring tracks, and <see cref="CellBorderStyle"/> paints the themed grid. Layout runs in
/// logical space, so <see cref="Panel.AutoScroll"/> scrolls an overflowing grid.
/// </summary>
/// <remarks>
/// Layout is a single deterministic pass over reused buffers: resolve placements, measure the
/// auto-sized tracks from each child's last externally set size, distribute the rest, position.
/// Children that no free cell can hold keep their bounds untouched.
/// </remarks>
public class TableLayoutPanel : Panel
{
    /// <summary>Weight marker for an auto-sized track while measuring.</summary>
    private const float _AutoSizeWeight = -1f;

    /// <summary>Per-child grid assignment: explicit cell (or -1/-1 for auto), spans, measured size.</summary>
    private struct Slot
    {
        public int Column;
        public int Row;
        public int ColumnSpan;
        public int RowSpan;
        public Size Preferred;
    }

    private readonly Dictionary<Control, Slot> _slots = [];
    private Slot[] _placements = [];
    private int[] _columnWidths = [];
    private int[] _columnOffsets = [];
    private float[] _columnWeights = [];
    private int[] _rowHeights = [];
    private int[] _rowOffsets = [];
    private float[] _rowWeights = [];
    private bool[] _occupied = [];
    private bool _layingOut;

    /// <summary>Creates a 1×1 grid.</summary>
    public TableLayoutPanel()
    {
        this.ColumnStyles = new(this);
        this.RowStyles = new(this);
        this.PerformLayout();
    }

    /// <summary>The number of columns; at least one.</summary>
    public int ColumnCount
    {
        get => field;
        set
        {
            value = Math.Max(1, value);
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
        }
    } = 1;

    /// <summary>The number of rows; at least one.</summary>
    public int RowCount
    {
        get => field;
        set
        {
            value = Math.Max(1, value);
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
        }
    } = 1;

    /// <summary>The sizing rules per column, in track order.</summary>
    public TableLayoutStyleCollection<ColumnStyle> ColumnStyles { get; }

    /// <summary>The sizing rules per row, in track order.</summary>
    public TableLayoutStyleCollection<RowStyle> RowStyles { get; }

    /// <summary>The grid lines painted between cells; <see cref="TableLayoutPanelCellBorderStyle.Single"/> insets every cell by one pixel per line.</summary>
    public TableLayoutPanelCellBorderStyle CellBorderStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
        }
    } = TableLayoutPanelCellBorderStyle.None;

    /// <summary>Pins a control to the given cell; auto-placement flows the other children around it.</summary>
    public void SetCellPosition(Control control, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(control);
        var slot = this.GetSlot(control);
        slot.Column = column;
        slot.Row = row;
        _slots[control] = slot;
        this.PerformLayout();
    }

    /// <summary>The cell a control was pinned to, or (-1, -1) while it auto-places.</summary>
    public TableLayoutPanelCellPosition GetCellPosition(Control control)
        => _slots.TryGetValue(control, out var slot) ? new(slot.Column, slot.Row) : new(-1, -1);

    /// <summary>Stretches a control across the given number of columns.</summary>
    public void SetColumnSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        var slot = this.GetSlot(control);
        slot.ColumnSpan = Math.Max(1, value);
        _slots[control] = slot;
        this.PerformLayout();
    }

    /// <summary>The number of columns the control spans; 1 by default.</summary>
    public int GetColumnSpan(Control control)
        => _slots.TryGetValue(control, out var slot) ? slot.ColumnSpan : 1;

    /// <summary>Stretches a control across the given number of rows.</summary>
    public void SetRowSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        var slot = this.GetSlot(control);
        slot.RowSpan = Math.Max(1, value);
        _slots[control] = slot;
        this.PerformLayout();
    }

    /// <summary>The number of rows the control spans; 1 by default.</summary>
    public int GetRowSpan(Control control)
        => _slots.TryGetValue(control, out var slot) ? slot.RowSpan : 1;

    /// <inheritdoc/>
    private protected override void OnChildAdded(Control child)
    {
        this.RecordPreferredSize(child);
        this.PerformLayout();
    }

    /// <inheritdoc/>
    private protected override void OnChildRemoved(Control child)
    {
        _slots.Remove(child);
        this.PerformLayout();
    }

    /// <inheritdoc/>
    private protected override void OnChildLayoutChanged(Control child)
    {
        if (_layingOut)
            return;

        this.RecordPreferredSize(child);
        this.PerformLayout();
    }

    /// <summary>The child's slot, or a fresh auto-placed single-span one.</summary>
    private Slot GetSlot(Control control)
        => _slots.TryGetValue(control, out var slot)
            ? slot
            : new() { Column = -1, Row = -1, ColumnSpan = 1, RowSpan = 1, Preferred = control.Size };

    /// <summary>
    /// Captures the child's externally set size — the measurement auto-sized tracks use. The layout
    /// pass itself resizes children to their cells, so measuring their live bounds would be circular.
    /// </summary>
    private void RecordPreferredSize(Control child)
    {
        var slot = this.GetSlot(child);
        slot.Preferred = child.Size;
        _slots[child] = slot;
    }

    /// <summary>
    /// Recomputes the grid and repositions every child. Runs automatically whenever the panel
    /// resizes, a child joins, leaves or resizes, or the grid structure changes. Children are
    /// arranged inside their cells by <see cref="ArrangeInCell"/>, which honors an explicitly
    /// assigned <see cref="Control.Dock"/>/<see cref="Control.Anchor"/> cell-relatively.
    /// </summary>
    private protected override void OnLayout()
    {
        var columns = this.ColumnCount;
        var rows = this.RowCount;
        var children = this.Controls.Count;
        this.EnsureCapacity(columns, rows, children);
        this.ResolvePlacements(columns, rows, children);

        var border = this.CellBorderStyle == TableLayoutPanelCellBorderStyle.Single ? 1 : 0;
        this.SizeAxis(horizontal: true, columns, children, border);
        this.SizeAxis(horizontal: false, rows, children, border);

        _layingOut = true;
        try
        {
            for (var i = 0; i < children; ++i)
            {
                var placement = _placements[i];
                if (placement.Column < 0)
                    continue;

                var child = this.Controls[i];
                var margin = child.Margin;
                var width = SpanExtent(_columnWidths, placement.Column, placement.ColumnSpan, border);
                var height = SpanExtent(_rowHeights, placement.Row, placement.RowSpan, border);
                var cell = new Rectangle(
                    _columnOffsets[placement.Column] + margin.Left,
                    _rowOffsets[placement.Row] + margin.Top,
                    Math.Max(0, width - margin.Horizontal),
                    Math.Max(0, height - margin.Vertical));
                child.Bounds = ArrangeInCell(child, cell, placement.Preferred);
            }
        }
        finally
        {
            _layingOut = false;
        }

        this.Invalidate();
    }

    /// <summary>Grows the reused layout buffers to the current grid and child counts.</summary>
    private void EnsureCapacity(int columns, int rows, int children)
    {
        if (_columnWidths.Length < columns)
        {
            _columnWidths = new int[columns];
            _columnOffsets = new int[columns];
            _columnWeights = new float[columns];
        }

        if (_rowHeights.Length < rows)
        {
            _rowHeights = new int[rows];
            _rowOffsets = new int[rows];
            _rowWeights = new float[rows];
        }

        if (_occupied.Length < columns * rows)
            _occupied = new bool[columns * rows];

        if (_placements.Length < children)
            _placements = new Slot[children];
    }

    /// <summary>
    /// Turns each child's slot into a concrete cell: explicit assignments are clamped into the grid
    /// and marked first, then auto-placed children scan row-major for the next run of free cells
    /// their spans fit into. A child nothing can hold keeps <c>Column == -1</c> and is skipped.
    /// </summary>
    private void ResolvePlacements(int columns, int rows, int children)
    {
        var cells = columns * rows;
        Array.Clear(_occupied, 0, cells);

        for (var i = 0; i < children; ++i)
        {
            var placement = this.GetSlot(this.Controls[i]);
            if (placement.Column >= 0 && placement.Row >= 0)
            {
                placement.Column = Math.Min(placement.Column, columns - 1);
                placement.Row = Math.Min(placement.Row, rows - 1);
                placement.ColumnSpan = Math.Min(placement.ColumnSpan, columns - placement.Column);
                placement.RowSpan = Math.Min(placement.RowSpan, rows - placement.Row);
                this.Occupy(placement, columns);
            }
            else
            {
                placement.Column = -1;
                placement.Row = -1;
                placement.ColumnSpan = Math.Min(placement.ColumnSpan, columns);
                placement.RowSpan = Math.Min(placement.RowSpan, rows);
            }

            _placements[i] = placement;
        }

        var cursor = 0;
        for (var i = 0; i < children; ++i)
        {
            var placement = _placements[i];
            if (placement.Column >= 0)
                continue;

            while (cursor < cells)
            {
                var column = cursor % columns;
                var row = cursor / columns;
                if (column + placement.ColumnSpan <= columns
                    && row + placement.RowSpan <= rows
                    && this.IsFree(column, row, placement.ColumnSpan, placement.RowSpan, columns))
                {
                    placement.Column = column;
                    placement.Row = row;
                    this.Occupy(placement, columns);
                    _placements[i] = placement;
                    break;
                }

                ++cursor;
            }
        }
    }

    private void Occupy(in Slot placement, int columns)
    {
        for (var row = 0; row < placement.RowSpan; ++row)
            for (var column = 0; column < placement.ColumnSpan; ++column)
                _occupied[((placement.Row + row) * columns) + placement.Column + column] = true;
    }

    private bool IsFree(int column, int row, int columnSpan, int rowSpan, int columns)
    {
        for (var r = 0; r < rowSpan; ++r)
            for (var c = 0; c < columnSpan; ++c)
                if (_occupied[((row + r) * columns) + column + c])
                    return false;

        return true;
    }

    /// <summary>
    /// Sizes one axis into the reused width/offset buffers: absolute tracks take their pixels,
    /// auto-sized tracks the largest single-span child (measured size plus margin), and percent
    /// tracks split what is left by weight — the last one absorbing the rounding remainder.
    /// Unstyled tracks act as equal percent shares.
    /// </summary>
    private void SizeAxis(bool horizontal, int tracks, int children, int border)
    {
        var sizes = horizontal ? _columnWidths : _rowHeights;
        var weights = horizontal ? _columnWeights : _rowWeights;
        var styled = horizontal ? this.ColumnStyles.Count : this.RowStyles.Count;
        var available = (horizontal ? this.Width : this.Height) - ((tracks + 1) * border);

        for (var i = 0; i < tracks; ++i)
        {
            TableLayoutStyle? style = i >= styled ? null : horizontal ? this.ColumnStyles[i] : this.RowStyles[i];
            switch (style?.SizeType ?? SizeType.Percent)
            {
                case SizeType.Absolute:
                    sizes[i] = (int)style!.Size;
                    weights[i] = 0;
                    break;
                case SizeType.AutoSize:
                    sizes[i] = 0;
                    weights[i] = _AutoSizeWeight;
                    break;
                case SizeType.Percent:
                default:
                    sizes[i] = 0;
                    weights[i] = style?.Size ?? 100f;
                    break;
            }
        }

        for (var i = 0; i < children; ++i)
        {
            var placement = _placements[i];
            var track = horizontal ? placement.Column : placement.Row;
            var span = horizontal ? placement.ColumnSpan : placement.RowSpan;
            if (track < 0 || span != 1 || weights[track] != _AutoSizeWeight)
                continue;

            var margin = this.Controls[i].Margin;
            var wanted = horizontal ? placement.Preferred.Width + margin.Horizontal : placement.Preferred.Height + margin.Vertical;
            sizes[track] = Math.Max(sizes[track], wanted);
        }

        var fixedSum = 0;
        var totalWeight = 0f;
        var lastPercent = -1;
        for (var i = 0; i < tracks; ++i)
            if (weights[i] > 0)
            {
                totalWeight += weights[i];
                lastPercent = i;
            }
            else
                fixedSum += sizes[i];

        var remaining = Math.Max(0, available - fixedSum);
        var assigned = 0;
        for (var i = 0; i < tracks; ++i)
        {
            if (weights[i] <= 0)
                continue;

            sizes[i] = i == lastPercent ? remaining - assigned : (int)(remaining * weights[i] / totalWeight);
            assigned += sizes[i];
        }

        var offsets = horizontal ? _columnOffsets : _rowOffsets;
        var position = border;
        for (var i = 0; i < tracks; ++i)
        {
            offsets[i] = position;
            position += sizes[i] + border;
        }
    }

    /// <summary>
    /// The bounds a child takes inside its (margin-deflated) cell. A child without an explicit
    /// <see cref="Control.Dock"/> or <see cref="Control.Anchor"/> fills the cell — the panel's
    /// historical default. An explicit dock claims the matching cell edge at the child's measured
    /// size (<see cref="DockStyle.Fill"/> keeps the whole cell); an explicit anchor pins the
    /// measured size to the anchored cell edges, stretching between opposing anchors and centering
    /// on an axis with neither — the Windows Forms in-cell contract.
    /// </summary>
    private static Rectangle ArrangeInCell(Control child, Rectangle cell, Size preferred)
    {
        switch (child.Dock)
        {
            case DockStyle.Fill:
                return cell;
            case DockStyle.Top:
                return new(cell.X, cell.Y, cell.Width, Math.Min(cell.Height, preferred.Height));
            case DockStyle.Bottom:
            {
                var docked = Math.Min(cell.Height, preferred.Height);
                return new(cell.X, cell.Bottom - docked, cell.Width, docked);
            }

            case DockStyle.Left:
                return new(cell.X, cell.Y, Math.Min(cell.Width, preferred.Width), cell.Height);
            case DockStyle.Right:
            {
                var docked = Math.Min(cell.Width, preferred.Width);
                return new(cell.Right - docked, cell.Y, docked, cell.Height);
            }

            case DockStyle.None:
            default:
                break;
        }

        if (!child.IsAnchorAssigned)
            return cell;

        var anchor = child.Anchor;
        int x, width;
        if ((anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right))
        {
            x = cell.X;
            width = cell.Width;
        }
        else
        {
            width = Math.Min(cell.Width, preferred.Width);
            x = (anchor & AnchorStyles.Right) != 0 ? cell.Right - width
                : (anchor & AnchorStyles.Left) != 0 ? cell.X
                : cell.X + ((cell.Width - width) / 2);
        }

        int y, height;
        if ((anchor & (AnchorStyles.Top | AnchorStyles.Bottom)) == (AnchorStyles.Top | AnchorStyles.Bottom))
        {
            y = cell.Y;
            height = cell.Height;
        }
        else
        {
            height = Math.Min(cell.Height, preferred.Height);
            y = (anchor & AnchorStyles.Bottom) != 0 ? cell.Bottom - height
                : (anchor & AnchorStyles.Top) != 0 ? cell.Y
                : cell.Y + ((cell.Height - height) / 2);
        }

        return new(x, y, width, height);
    }

    /// <summary>The pixel extent of a span: the covered tracks plus the grid lines between them.</summary>
    private static int SpanExtent(int[] sizes, int start, int span, int border)
    {
        var total = (span - 1) * border;
        for (var i = 0; i < span; ++i)
            total += sizes[start + i];

        return total;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (this.CellBorderStyle != TableLayoutPanelCellBorderStyle.Single)
            return;

        var g = e.Graphics;
        var borderColor = this.Theme.Border;
        var columns = this.ColumnCount;
        var rows = this.RowCount;
        var scroll = this.AutoScrollPosition; // negative offsets shift the grid with the content

        var totalWidth = columns + 1;
        for (var i = 0; i < columns; ++i)
            totalWidth += _columnWidths[i];

        var totalHeight = rows + 1;
        for (var i = 0; i < rows; ++i)
            totalHeight += _rowHeights[i];

        var x = scroll.X;
        for (var i = 0; i <= columns; ++i)
        {
            g.DrawLine(borderColor, x, scroll.Y, x, scroll.Y + totalHeight - 1);
            if (i < columns)
                x += _columnWidths[i] + 1;
        }

        var y = scroll.Y;
        for (var i = 0; i <= rows; ++i)
        {
            g.DrawLine(borderColor, scroll.X, y, scroll.X + totalWidth - 1, y);
            if (i < rows)
                y += _rowHeights[i] + 1;
        }
    }
}

/// <summary>Grid coordinates inside a <see cref="TableLayoutPanel"/>; (-1, -1) marks auto-placement.</summary>
/// <param name="Column">The zero-based column index.</param>
/// <param name="Row">The zero-based row index.</param>
public readonly record struct TableLayoutPanelCellPosition(int Column, int Row);
