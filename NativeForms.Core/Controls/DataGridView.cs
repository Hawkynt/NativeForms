using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn, vertically virtualized data grid painted in the native theme. Rows are arbitrary
/// objects bound through an <see cref="ObservableList{T}"/>; each <see cref="DataGridViewColumn"/>
/// maps a row to its cell content via reflection-free selector delegates, so binding stays
/// trim/AOT-safe. Columns render as text, check, button, link, multi-image or progress cells
/// (<see cref="DataGridViewColumnKind"/>), headers sort through an index indirection that never
/// mutates <see cref="Items"/>, and presentation (row colors/heights/visibility, cell styles) is
/// driven by optional per-row/per-cell selectors.
/// </summary>
/// <remarks>
/// Only the visible row window is ever touched: painting, hit-testing and the hidden-row/row-height
/// selectors walk linearly over that window, so memory stays constant for very large row counts. The
/// scroll range under those selectors is approximated from the default <see cref="RowHeight"/>; the
/// sort map is the one O(n) allocation and exists only while a sort is active.
/// </remarks>
public class DataGridView : OwnerDrawnControl
{
    private const int _CellPadding = 4;
    private const int _IconGap = 4;
    private const int _WheelRows = 3;
    private const int _WheelHorizontalStep = 30;
    private const int _DividerZone = 3;
    private const int _MinColumnWidth = 8;
    private const int _CheckBoxSize = 14;
    private const int _DoubleClickMs = 500;

    private readonly List<DataGridViewColumn> _columns = [];

    private int _selectedRowIndex = -1;
    private int _topRow;
    private int? _rowHeight;
    private int? _columnHeaderHeight;
    private int _currentColumnIndex;

    private DataGridViewColumn? _sortedColumn;
    private SortOrder _sortOrder;
    private int[]? _sortMap;
    private bool _sortDirty;

    private int _resizeColumnIndex = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;

    private long _lastClickTime;
    private int _lastClickRowIndex = -1;
    private int _lastClickColumnIndex = -1;

    /// <summary>Creates a data grid.</summary>
    public DataGridView()
    {
        this.Items = new();
        this.Items.ListChanged += this.OnItemsChanged;
    }

    /// <summary>The columns shown. Mutate then call <see cref="OwnerDrawnControl.Invalidate()"/> to repaint.</summary>
    public IList<DataGridViewColumn> Columns => _columns;

    /// <summary>The row items shown. Mutating this collection repaints the control.</summary>
    public ObservableList<object?> Items { get; }

    /// <summary>The pixel height of a data row. Defaults to the theme row height.</summary>
    public int RowHeight
    {
        get => _rowHeight ?? this.Theme.RowHeight;
        set
        {
            _rowHeight = Math.Max(1, value);
            this.Invalidate();
        }
    }

    /// <summary>The pixel height of the column-header row. Defaults to <see cref="RowHeight"/>.</summary>
    public int ColumnHeaderHeight
    {
        get => _columnHeaderHeight ?? this.RowHeight;
        set
        {
            _columnHeaderHeight = Math.Max(1, value);
            this.Invalidate();
        }
    }

    /// <summary>Whether the column-header row is painted. Defaults to <see langword="true"/>.</summary>
    public bool ShowColumnHeaders
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    } = true;

    /// <summary>Whether a header column is painted at the left edge, with a marker triangle on the
    /// selected row. Defaults to <see langword="false"/>.</summary>
    public bool ShowRowHeaders
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    }

    /// <summary>The pixel width of the row-header column when <see cref="ShowRowHeaders"/> is enabled.</summary>
    public int RowHeaderWidth
    {
        get => field;
        set
        {
            field = Math.Max(1, value);
            this.Invalidate();
        }
    } = 24;

    /// <summary>Whether grid lines are painted. Defaults to <see langword="true"/>.</summary>
    public bool ShowGridLines
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    } = true;

    /// <summary>Whether every other data row is tinted with <see cref="AlternatingRowColor"/>.</summary>
    public bool AlternatingRows
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    }

    /// <summary>The background tint of alternating rows when <see cref="AlternatingRows"/> is enabled.</summary>
    public Color AlternatingRowColor
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    } = Color.FromArgb(0xFF, 0xF6, 0xF6, 0xF6);

    /// <summary>Whether dragging a column divider in the header resizes that column. Defaults to
    /// <see langword="true"/>; the grab zone is ±3 px around the divider.</summary>
    public bool AllowUserToResizeColumns { get; set; } = true;

    /// <summary>Whether every cell in the grid refuses edits and check toggling. Combined with the
    /// column and per-cell levels by <see cref="IsCellReadOnly"/>.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional per-row background color over the row item; <see langword="null"/> (selector
    /// or result) keeps the default. Runs on the paint path — return a plain color, capture nothing.</summary>
    public Func<object?, Color?>? RowBackColorSelector { get; set; }

    /// <summary>Optional per-row pixel height over the row item; <see langword="null"/> (selector or
    /// result) uses <see cref="RowHeight"/>. Evaluated linearly over the visible window only.</summary>
    public Func<object?, int?>? RowHeightSelector { get; set; }

    /// <summary>Optional predicate hiding rows; hidden rows are skipped by painting, hit-testing and
    /// keyboard navigation. Evaluated linearly over the visible window only.</summary>
    public Func<object?, bool>? RowHiddenSelector { get; set; }

    /// <summary>Optional predicate over the row item deciding whether the row can be selected via
    /// mouse or keyboard; <see langword="null"/> means all rows are selectable.</summary>
    public Func<object?, bool>? RowSelectableSelector { get; set; }

    /// <summary>The horizontal scroll offset in pixels, clamped so the columns never scroll past
    /// their total width; columns are shifted left by this amount.</summary>
    public int HorizontalOffset
    {
        get => Math.Min(field, this.MaxHorizontalOffset);
        set
        {
            field = Math.Max(0, value);
            this.Invalidate();
        }
    }

    /// <summary>The selected row index into <see cref="Items"/>, or -1 for none. Stable while the
    /// grid is sorted — sorting only reorders the presentation.</summary>
    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (clamped == _selectedRowIndex)
                return;

            _selectedRowIndex = clamped;
            this.EnsureVisible(clamped);
            this.Invalidate();
            this.OnSelectionChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected row item, or <see langword="null"/>.</summary>
    public object? SelectedItem
    {
        get => _selectedRowIndex >= 0 && _selectedRowIndex < this.Items.Count ? this.Items[_selectedRowIndex] : null;
        set => this.SelectedRowIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>The column index keyboard activation (Space/Enter) targets; follows the last clicked
    /// cell.</summary>
    public int CurrentColumnIndex
    {
        get => _currentColumnIndex;
        set => _currentColumnIndex = Math.Max(0, value);
    }

    /// <summary>The display index of the first visible data row (vertical scroll position).</summary>
    public int TopRow => _topRow;

    /// <summary>The column the grid is currently sorted by, or <see langword="null"/>.</summary>
    public DataGridViewColumn? SortedColumn => _sortedColumn;

    /// <summary>The active sort direction; <see cref="SortOrder.None"/> shows <see cref="Items"/> order.</summary>
    public SortOrder SortOrder => _sortOrder;

    /// <summary>Raised when <see cref="SelectedRowIndex"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when a data cell is clicked, and on Space/Enter for the current cell.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? CellClick;

    /// <summary>Raised when a data cell is clicked twice in quick succession.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? CellDoubleClick;

    /// <summary>Raised when the content of a check, button, link or multi-image cell is clicked; for
    /// multi-image cells <see cref="DataGridViewCellEventArgs.ContentIndex"/> names the icon.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? CellContentClick;

    /// <summary>Replaces the rows from any sequence (one-way binding convenience).</summary>
    public IEnumerable? DataSource
    {
        set
        {
            this.Items.Clear();
            if (value is null)
                return;

            foreach (var item in value)
                this.Items.Add(item);
        }
    }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The pixel height of the column-header row, or 0 when hidden.</summary>
    protected int HeaderHeight => this.ShowColumnHeaders ? this.ColumnHeaderHeight : 0;

    /// <summary>The number of fully visible data rows, assuming the default <see cref="RowHeight"/>.</summary>
    protected int VisibleRowCount => Math.Max(1, (this.Height - this.HeaderHeight) / this.RowHeight);

    /// <summary>The x-coordinate where the data columns start (right of the row headers).</summary>
    private int ContentLeft => this.ShowRowHeaders ? this.RowHeaderWidth : 0;

    /// <summary>The combined pixel width of all columns.</summary>
    private int TotalColumnWidth
    {
        get
        {
            var total = 0;
            for (var i = 0; i < _columns.Count; ++i)
                total += _columns[i].Width;
            return total;
        }
    }

    /// <summary>The largest permitted <see cref="HorizontalOffset"/> for the current column widths.</summary>
    private int MaxHorizontalOffset => Math.Max(0, this.TotalColumnWidth - Math.Max(0, this.Width - this.ContentLeft));

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged(EventArgs e) => this.SelectionChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="CellClick"/>.</summary>
    protected virtual void OnCellClick(DataGridViewCellEventArgs e) => this.CellClick?.Invoke(this, e);

    /// <summary>Raises <see cref="CellDoubleClick"/>.</summary>
    protected virtual void OnCellDoubleClick(DataGridViewCellEventArgs e) => this.CellDoubleClick?.Invoke(this, e);

    /// <summary>Raises <see cref="CellContentClick"/>.</summary>
    protected virtual void OnCellContentClick(DataGridViewCellEventArgs e) => this.CellContentClick?.Invoke(this, e);

    /// <summary>
    /// Whether the given cell refuses edits and check toggling: read-only at any level (grid, column,
    /// or the column's per-cell predicate) makes the cell read-only, matching WinForms semantics.
    /// </summary>
    public bool IsCellReadOnly(object? rowItem, DataGridViewColumn column)
        => this.ReadOnly || column.ReadOnly || (column.ReadOnlyCellSelector?.Invoke(rowItem) ?? false);

    /// <summary>The tooltip text the column's <see cref="DataGridViewColumn.TooltipSelector"/> yields
    /// for the given cell, or <see langword="null"/>. Indices are model (Items/Columns) indices.</summary>
    public string? GetCellTooltip(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.Items.Count || columnIndex < 0 || columnIndex >= _columns.Count)
            return null;

        return _columns[columnIndex].TooltipSelector?.Invoke(this.Items[rowIndex]);
    }

    /// <summary>
    /// Sorts the presentation by the given column and direction, or clears the sort when
    /// <paramref name="column"/> is <see langword="null"/> or <paramref name="order"/> is
    /// <see cref="SortOrder.None"/>. Sorting reorders an index indirection — <see cref="Items"/> is
    /// never mutated — rebuilt lazily after item changes.
    /// </summary>
    public void Sort(DataGridViewColumn? column, SortOrder order)
    {
        if (column is null || order == SortOrder.None)
        {
            _sortedColumn = null;
            _sortOrder = SortOrder.None;
            _sortMap = null;
        }
        else
        {
            _sortedColumn = column;
            _sortOrder = order;
            _sortDirty = true;
        }

        this.Invalidate();
    }

    /// <summary>Scrolls so the given data row (an <see cref="Items"/> index) is visible.</summary>
    public void EnsureVisible(int rowIndex)
    {
        if (rowIndex < 0)
            return;

        this.EnsureSortMap();
        var display = this.ToDisplayIndex(rowIndex);
        if (display < 0)
            return;

        if (display < _topRow)
            _topRow = display;
        else if (display >= _topRow + this.VisibleRowCount)
            _topRow = display - this.VisibleRowCount + 1;

        this.ClampScroll();
    }

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedRowIndex >= this.Items.Count)
            _selectedRowIndex = this.Items.Count - 1;

        _sortDirty = true;
        this.ClampScroll();
        this.Invalidate();
    }

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.Items.Count - this.VisibleRowCount);
        _topRow = Math.Clamp(_topRow, 0, maxTop);
    }

    /// <summary>Rebuilds the display→model sort map when a sort is active and the items changed. The
    /// comparison closure allocates only here — on a sort gesture or item mutation, never per frame.</summary>
    private void EnsureSortMap()
    {
        var column = _sortedColumn;
        if (column is null || _sortOrder == SortOrder.None)
        {
            _sortMap = null;
            return;
        }

        var count = this.Items.Count;
        var map = _sortMap;
        if (!_sortDirty && map is not null && map.Length == count)
            return;

        if (map is null || map.Length != count)
            map = new int[count];

        for (var i = 0; i < count; ++i)
            map[i] = i;

        var direction = _sortOrder == SortOrder.Descending ? -1 : 1;
        var items = this.Items;
        Array.Sort(map, (a, b) =>
        {
            var result = CompareRows(column, items[a], items[b]);
            return result != 0 ? direction * result : a - b; // ties keep model order
        });

        _sortMap = map;
        _sortDirty = false;
    }

    private static int CompareRows(DataGridViewColumn column, object? x, object? y)
    {
        if (column.SortComparison is { } comparison)
            return comparison(x, y);

        var left = column.ValueSelector(x);
        var right = column.ValueSelector(y);
        if (left is null)
            return right is null ? 0 : -1;
        if (right is null)
            return 1;
        if (left.GetType() == right.GetType() && left is IComparable comparable)
            return comparable.CompareTo(right);

        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    private int ToModelIndex(int displayIndex)
    {
        var map = _sortMap;
        return map is null ? displayIndex : map[displayIndex];
    }

    private int ToDisplayIndex(int modelIndex)
    {
        var map = _sortMap;
        if (map is null)
            return modelIndex;

        for (var i = 0; i < map.Length; ++i)
            if (map[i] == modelIndex)
                return i;

        return -1;
    }

    private bool IsRowHidden(object? item) => this.RowHiddenSelector?.Invoke(item) ?? false;

    private bool IsRowSelectable(object? item) => this.RowSelectableSelector?.Invoke(item) ?? true;

    private bool IsRowNavigable(int modelIndex)
    {
        var item = this.Items[modelIndex];
        return !this.IsRowHidden(item) && this.IsRowSelectable(item);
    }

    private int GetRowHeightFor(object? item) => Math.Max(1, this.RowHeightSelector?.Invoke(item) ?? this.RowHeight);

    private static string GetDisplayText(DataGridViewColumn column, object? item)
        => column.DisplayTextSelector?.Invoke(item) ?? column.ValueSelector(item)?.ToString() ?? string.Empty;

    /// <summary>Finds the data row at the given y-coordinate by walking the visible window (skipping
    /// hidden rows, honoring per-row heights). Returns the model index, or -1.</summary>
    private int HitTestRow(int y, out int rowTop, out int rowHeight)
    {
        this.EnsureSortMap();
        rowTop = 0;
        rowHeight = 0;

        var count = this.Items.Count;
        var height = this.Height;
        var currentY = this.HeaderHeight;
        var display = Math.Max(0, _topRow);
        while (currentY < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            var item = this.Items[modelIndex];
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var h = this.GetRowHeightFor(item);
            if (y < currentY + h)
            {
                rowTop = currentY;
                rowHeight = h;
                return modelIndex;
            }

            currentY += h;
        }

        return -1;
    }

    /// <summary>Finds the column under the given x-coordinate. Returns its index, or -1 (row-header
    /// zone or past the last column).</summary>
    private int HitTestColumn(int x, out int cellLeft)
    {
        cellLeft = 0;
        if (x < this.ContentLeft)
            return -1;

        var cx = this.ContentLeft - this.HorizontalOffset;
        for (var c = 0; c < _columns.Count; ++c)
        {
            var width = _columns[c].Width;
            if (x >= cx && x < cx + width)
            {
                cellLeft = cx;
                return c;
            }

            cx += width;
        }

        return -1;
    }

    /// <summary>Finds the column whose right divider lies within ±3 px of the given x-coordinate.</summary>
    private int HitTestColumnDivider(int x)
    {
        var cx = this.ContentLeft - this.HorizontalOffset;
        for (var c = 0; c < _columns.Count; ++c)
        {
            cx += _columns[c].Width;
            if (Math.Abs(x - cx) <= _DividerZone)
                return c;
        }

        return -1;
    }

    /// <summary>Steps a display row index by up to <paramref name="steps"/> non-hidden rows.</summary>
    private int StepDisplayRow(int from, int direction, int steps)
    {
        if (direction == 0)
            return from;

        var count = this.Items.Count;
        var display = from;
        while (steps-- > 0)
        {
            var next = display + direction;
            while (next >= 0 && next < count && this.IsRowHidden(this.Items[this.ToModelIndex(next)]))
                next += direction;

            if (next < 0 || next >= count)
                break;

            display = next;
        }

        return display;
    }

    /// <summary>Moves the selection by the given number of display rows, skipping hidden and
    /// unselectable rows; with no selection, any move selects the first reachable row.</summary>
    private void MoveSelection(int steps)
    {
        this.EnsureSortMap();
        var count = this.Items.Count;
        if (count == 0 || steps == 0)
            return;

        var direction = Math.Sign(steps);
        if (_selectedRowIndex < 0 && direction < 0)
        {
            this.SelectEdge(first: true);
            return;
        }

        var remaining = Math.Abs(steps);
        var display = _selectedRowIndex >= 0 ? this.ToDisplayIndex(_selectedRowIndex) : -1;
        var target = -1;
        while (remaining-- > 0)
        {
            var next = display + direction;
            while (next >= 0 && next < count && !this.IsRowNavigable(this.ToModelIndex(next)))
                next += direction;

            if (next < 0 || next >= count)
                break;

            display = next;
            target = next;
        }

        if (target >= 0)
            this.SelectedRowIndex = this.ToModelIndex(target);
    }

    /// <summary>Selects the first or last navigable row in display order.</summary>
    private void SelectEdge(bool first)
    {
        this.EnsureSortMap();
        var count = this.Items.Count;
        var direction = first ? 1 : -1;
        var display = first ? 0 : count - 1;
        while (display >= 0 && display < count && !this.IsRowNavigable(this.ToModelIndex(display)))
            display += direction;

        if (display >= 0 && display < count)
            this.SelectedRowIndex = this.ToModelIndex(display);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (e.Y < this.HeaderHeight)
        {
            this.HandleHeaderMouseDown(e.X);
            return;
        }

        var rowIndex = this.HitTestRow(e.Y, out var rowTop, out var rowHeight);
        _ = rowTop;
        if (rowIndex < 0)
            return;

        var item = this.Items[rowIndex];
        if (this.IsRowSelectable(item))
            this.SelectedRowIndex = rowIndex;

        var columnIndex = this.HitTestColumn(e.X, out var cellLeft);
        if (columnIndex < 0)
            return;

        _currentColumnIndex = columnIndex;
        this.HandleCellMouseDown(rowIndex, columnIndex, item, e.X - cellLeft, rowHeight);
    }

    private void HandleHeaderMouseDown(int x)
    {
        if (this.AllowUserToResizeColumns)
        {
            var divider = this.HitTestColumnDivider(x);
            if (divider >= 0)
            {
                _resizeColumnIndex = divider;
                _resizeStartX = x;
                _resizeStartWidth = _columns[divider].Width;
                return;
            }
        }

        var columnIndex = this.HitTestColumn(x, out _);
        if (columnIndex < 0)
            return;

        var column = _columns[columnIndex];
        if (column.SortMode != DataGridViewColumnSortMode.Automatic)
            return;

        var order = ReferenceEquals(column, _sortedColumn) && _sortOrder == SortOrder.Ascending
            ? SortOrder.Descending
            : SortOrder.Ascending;
        this.Sort(column, order);
    }

    private void HandleCellMouseDown(int rowIndex, int columnIndex, object? item, int cellX, int rowHeight)
    {
        var now = Environment.TickCount64;
        var isDouble = rowIndex == _lastClickRowIndex
            && columnIndex == _lastClickColumnIndex
            && now - _lastClickTime <= _DoubleClickMs;
        _lastClickRowIndex = rowIndex;
        _lastClickColumnIndex = columnIndex;
        _lastClickTime = isDouble ? 0 : now; // reset so a triple click is not two doubles

        this.OnCellClick(new(rowIndex, columnIndex));
        if (isDouble)
            this.OnCellDoubleClick(new(rowIndex, columnIndex));

        var column = _columns[columnIndex];
        switch (column.Kind)
        {
            case DataGridViewColumnKind.Check:
            {
                this.OnCellContentClick(new(rowIndex, columnIndex));
                if (column.CheckedSetter is null || this.IsCellReadOnly(item, column))
                    break;

                column.CheckedSetter(item, !(column.CheckedSelector?.Invoke(item) ?? false));
                this.Invalidate();
                break;
            }

            case DataGridViewColumnKind.Button:
            {
                if (column.EnabledSelector?.Invoke(item) ?? true)
                    this.OnCellContentClick(new(rowIndex, columnIndex));
                break;
            }

            case DataGridViewColumnKind.Link:
            {
                this.OnCellContentClick(new(rowIndex, columnIndex));
                break;
            }

            case DataGridViewColumnKind.MultiImage:
            {
                var images = column.ImagesSelector?.Invoke(item);
                var iconSize = rowHeight - 4;
                if (images is null || iconSize <= 0)
                    break;

                var relative = cellX - _CellPadding;
                if (relative < 0)
                    break;

                var slot = iconSize + _IconGap;
                var index = relative / slot;
                if (index < images.Count && (relative % slot) < iconSize)
                    this.OnCellContentClick(new(rowIndex, columnIndex, index));
                break;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_resizeColumnIndex < 0 || _resizeColumnIndex >= _columns.Count)
            return;

        var column = _columns[_resizeColumnIndex];
        var width = Math.Max(_MinColumnWidth, _resizeStartWidth + (e.X - _resizeStartX));
        if (width == column.Width)
            return;

        column.Width = width;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _resizeColumnIndex = -1;

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((e.Modifiers & KeyModifiers.Shift) != 0)
        {
            this.HorizontalOffset = this.HorizontalOffset - (Math.Sign(e.Delta) * _WheelHorizontalStep);
            return;
        }

        this.EnsureSortMap();
        _topRow = this.StepDisplayRow(_topRow, -Math.Sign(e.Delta), _WheelRows);
        this.ClampScroll();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Down: this.MoveSelection(1); break;
            case Keys.Up: this.MoveSelection(-1); break;
            case Keys.Home when this.Items.Count > 0: this.SelectEdge(first: true); break;
            case Keys.End: this.SelectEdge(first: false); break;
            case Keys.PageDown: this.MoveSelection(this.VisibleRowCount); break;
            case Keys.PageUp: this.MoveSelection(-this.VisibleRowCount); break;
            case Keys.Space or Keys.Enter when _selectedRowIndex >= 0 && _columns.Count > 0:
                this.OnCellClick(new(_selectedRowIndex, Math.Min(_currentColumnIndex, _columns.Count - 1)));
                break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        this.EnsureSortMap();
        this.AutoSizeColumns(g);

        var columns = _columns;
        var columnCount = columns.Count;
        var header = this.HeaderHeight;
        var horizontalOffset = this.HorizontalOffset;
        var contentLeft = this.ContentLeft;
        var showGridLines = this.ShowGridLines;
        var count = this.Items.Count;

        g.PushClip(new Rectangle(contentLeft, 0, Math.Max(0, width - contentLeft), height));

        if (this.ShowColumnHeaders)
        {
            g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, width, header));

            var hx = contentLeft - horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                var column = columns[c];
                var cellRect = new Rectangle(hx + _CellPadding, 0, Math.Max(0, column.Width - (_CellPadding * 2)), header);
                g.DrawText(column.HeaderText, theme.DefaultFont, theme.HeaderText, cellRect, column.Alignment);
                if (ReferenceEquals(column, _sortedColumn) && _sortOrder != SortOrder.None)
                    GlyphRenderer.DrawSortArrow(g, theme.HeaderText, new Rectangle(hx + column.Width - 14, 0, 10, header), _sortOrder == SortOrder.Ascending);

                hx += column.Width;
            }

            g.DrawLine(theme.Border, 0, header - 1, width, header - 1);
        }

        var y = header;
        var display = Math.Max(0, _topRow);
        while (y < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            var item = this.Items[modelIndex];
            var displayIndex = display;
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var rowHeight = this.GetRowHeightFor(item);
            var selected = modelIndex == _selectedRowIndex;
            if (selected)
                g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, width, rowHeight));
            else if (this.RowBackColorSelector?.Invoke(item) is { } rowBack)
                g.FillRectangle(rowBack, new Rectangle(0, y, width, rowHeight));
            else if (this.AlternatingRows && (displayIndex & 1) == 1)
                g.FillRectangle(this.AlternatingRowColor, new Rectangle(0, y, width, rowHeight));

            var cx = contentLeft - horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                var column = columns[c];
                this.PaintCell(g, theme, column, item, new Rectangle(cx, y, column.Width, rowHeight), selected);
                cx += column.Width;
            }

            if (showGridLines)
                g.DrawLine(theme.GridLine, 0, y + rowHeight - 1, width, y + rowHeight - 1);

            y += rowHeight;
        }

        if (showGridLines)
        {
            var gx = contentLeft - horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                gx += columns[c].Width;
                if (gx > contentLeft && gx < width)
                    g.DrawLine(theme.GridLine, gx - 1, header, gx - 1, height);
            }
        }

        g.PopClip();

        if (this.ShowRowHeaders)
            this.PaintRowHeaders(g, theme, header, height, count);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <summary>Paints one data cell according to its column's <see cref="DataGridViewColumnKind"/>.</summary>
    private void PaintCell(IGraphics g, ITheme theme, DataGridViewColumn column, object? item, Rectangle cellRect, bool selected)
    {
        var style = column.CellStyleSelector?.Invoke(item) ?? default;
        if (style.BackColor is { } backColor)
            g.FillRectangle(backColor, cellRect);

        var alignment = style.Alignment ?? column.Alignment;
        var foreColor = style.ForeColor ?? (selected ? theme.SelectionText : theme.ControlText);

        switch (column.Kind)
        {
            case DataGridViewColumnKind.Check:
            {
                var boxSize = Math.Max(6, Math.Min(_CheckBoxSize, cellRect.Height - 4));
                var box = new Rectangle(
                    cellRect.X + ((cellRect.Width - boxSize) / 2),
                    cellRect.Y + ((cellRect.Height - boxSize) / 2),
                    boxSize,
                    boxSize);
                GlyphRenderer.DrawCheckBox(g, theme, box, column.CheckedSelector?.Invoke(item) ?? false);
                break;
            }

            case DataGridViewColumnKind.Button:
            {
                var face = new Rectangle(cellRect.X + 2, cellRect.Y + 2, Math.Max(0, cellRect.Width - 4), Math.Max(0, cellRect.Height - 4));
                GlyphRenderer.DrawButtonFace(g, theme, face, GetDisplayText(column, item), column.EnabledSelector?.Invoke(item) ?? true);
                break;
            }

            case DataGridViewColumnKind.Link:
            {
                var text = GetDisplayText(column, item);
                var textRect = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - _CellPadding), cellRect.Height);
                g.DrawText(text, theme.DefaultFont, theme.Accent, textRect, alignment);

                var size = g.MeasureText(text, theme.DefaultFont);
                var left = textRect.X;
                if (alignment is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter)
                    left = textRect.X + ((textRect.Width - size.Width) / 2);
                else if (alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight)
                    left = textRect.Right - size.Width;

                var underlineY = textRect.Y + ((textRect.Height + size.Height) / 2) - 1;
                g.DrawLine(theme.Accent, left, underlineY, left + size.Width, underlineY);
                break;
            }

            case DataGridViewColumnKind.MultiImage:
            {
                var images = column.ImagesSelector?.Invoke(item);
                var iconSize = cellRect.Height - 4;
                if (images is null || iconSize <= 0)
                    break;

                var x = cellRect.X + _CellPadding;
                for (var i = 0; i < images.Count; ++i)
                {
                    g.DrawImage(images[i], new Rectangle(x, cellRect.Y + 2, iconSize, iconSize));
                    x += iconSize + _IconGap;
                }

                break;
            }

            case DataGridViewColumnKind.Progress:
            {
                var bar = new Rectangle(cellRect.X + 2, cellRect.Y + 2, Math.Max(0, cellRect.Width - 4), Math.Max(0, cellRect.Height - 4));
                GlyphRenderer.DrawProgressBar(g, theme, bar, column.ProgressSelector?.Invoke(item) ?? 0, 0, 100);
                break;
            }

            default:
            {
                var textLeft = cellRect.X + _CellPadding;
                var icon = column.ImageSelector?.Invoke(item);
                if (icon is not null)
                {
                    var iconSize = cellRect.Height - 4;
                    g.DrawImage(icon, new Rectangle(cellRect.X + _CellPadding, cellRect.Y + 2, iconSize, iconSize));
                    textLeft += iconSize + _IconGap;
                }

                var textRect = new Rectangle(textLeft, cellRect.Y, Math.Max(0, cellRect.Right - textLeft), cellRect.Height);
                g.DrawText(GetDisplayText(column, item), theme.DefaultFont, foreColor, textRect, alignment);
                break;
            }
        }
    }

    /// <summary>Paints the row-header column: themed strip, per-row separators and the marker
    /// triangle on the selected row.</summary>
    private void PaintRowHeaders(IGraphics g, ITheme theme, int header, int height, int count)
    {
        var rowHeaderWidth = this.RowHeaderWidth;
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, rowHeaderWidth, height));

        var y = header;
        var display = Math.Max(0, _topRow);
        while (y < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            var item = this.Items[modelIndex];
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var rowHeight = this.GetRowHeightFor(item);
            if (modelIndex == _selectedRowIndex)
                GlyphRenderer.DrawRowMarker(g, theme.HeaderText, new Rectangle(0, y, rowHeaderWidth, rowHeight));

            g.DrawLine(theme.GridLine, 0, y + rowHeight - 1, rowHeaderWidth, y + rowHeight - 1);
            y += rowHeight;
        }

        if (this.ShowColumnHeaders)
            g.DrawLine(theme.Border, 0, header - 1, rowHeaderWidth, header - 1);

        g.DrawLine(theme.Border, rowHeaderWidth - 1, 0, rowHeaderWidth - 1, height);
    }

    /// <summary>Applies <see cref="DataGridViewAutoSizeColumnMode.AllCells"/> by measuring the cell
    /// text of the visible row window — deliberately window-scoped so very large grids stay cheap.</summary>
    private void AutoSizeColumns(IGraphics g)
    {
        var columns = _columns;
        for (var c = 0; c < columns.Count; ++c)
        {
            var column = columns[c];
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.AllCells)
                continue;

            var font = this.Theme.DefaultFont;
            var widest = 0;
            var count = this.Items.Count;
            var height = this.Height;
            var y = this.HeaderHeight;
            var display = Math.Max(0, _topRow);
            while (y < height && display < count)
            {
                var item = this.Items[this.ToModelIndex(display)];
                ++display;
                if (this.IsRowHidden(item))
                    continue;

                var rowHeight = this.GetRowHeightFor(item);
                var cellWidth = g.MeasureText(GetDisplayText(column, item), font).Width;
                if (column.ImageSelector?.Invoke(item) is not null)
                    cellWidth += rowHeight - 4 + _IconGap;

                if (cellWidth > widest)
                    widest = cellWidth;

                y += rowHeight;
            }

            column.Width = Math.Max(_MinColumnWidth, widest + (_CellPadding * 2));
        }
    }
}
