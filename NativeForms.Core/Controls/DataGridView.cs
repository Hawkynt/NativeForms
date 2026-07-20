using System.Collections;
using System.Drawing;
using System.Text;
using Hawkynt.NativeForms.Backends;
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
/// driven by optional per-row/per-cell selectors. Cells edit in place through
/// <see cref="BeginEdit"/> — a hosted editor or popup per <see cref="DataGridViewColumnKind"/> —
/// columns can be frozen, drag-reordered through a display-order indirection and copied to the
/// clipboard as tab-separated text, and <see cref="MultiSelect"/> extends the full-row selection to
/// Ctrl/Shift sets.
/// </summary>
/// <remarks>
/// <para>
/// Only the visible row window is ever touched: painting, hit-testing and the hidden-row/row-height
/// selectors walk linearly over that window, so memory stays constant for very large row counts. The
/// scroll range under those selectors is approximated from the default <see cref="RowHeight"/>; the
/// sort map is the one O(n) allocation and exists only while a sort is active.
/// </para>
/// <para>
/// Keys typed inside a hosted native editor are not observable from the core, so a hosted editor has
/// no guaranteed Enter-key moment to commit at. Like <see cref="UpDownBase"/>, edits are committed at the honest points available:
/// Enter/Escape when the key reaches the grid surface (backends that route popup/canvas keys), a
/// press on the grid outside the editor, the edited row scrolling out of the visible window (commit,
/// matching the classic grid), and the explicit <see cref="CommitEdit"/>/<see cref="CancelEdit"/>
/// calls. User keystrokes flow into the hosted editor's native widget and are read back on commit.
/// </para>
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
    private const int _ComboArrowRows = 5;
    private const int _ComboArrowZone = 16;
    private const int _MaxComboPopupRows = 8;

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
    private int[]? _displayMap;

    private List<int>? _multiSelection;
    private int _anchorRowIndex = -1;

    private int _resizeColumnIndex = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;
    private int _dragColumnIndex = -1;

    private long _lastClickTime;
    private int _lastClickRowIndex = -1;
    private int _lastClickColumnIndex = -1;

    private int _editRowIndex = -1;
    private int _editColumnIndex = -1;
    private TextBox? _textEditor;
    private NumericUpDown? _numericEditor;
    private IPopupPeer? _editPopup;
    private bool _editPopupShown;
    private CalendarCore? _editCalendar;
    private IReadOnlyList<object?>? _editChoices;
    private int _editHoverIndex;
    private int _editPopupTop;
    private int _editPopupRows;
    private Size _editPopupSize;

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

    /// <summary>Whether dragging a column header past a neighbor reorders the display: the drag
    /// rewrites <see cref="DataGridViewColumn.DisplayIndex"/> on every column while
    /// <see cref="Columns"/> keeps its model order. Defaults to <see langword="false"/>.</summary>
    public bool AllowUserToOrderColumns { get; set; }

    /// <summary>
    /// Whether several rows can be selected at once with Ctrl (toggle) and Shift (display-order
    /// range) clicks and Shift+arrows, like a <see cref="SelectionMode.MultiExtended"/> list box.
    /// <see cref="SelectedRowIndex"/> stays the current row; <see cref="SelectedItems"/> enumerates
    /// the whole set. Defaults to <see langword="false"/>.
    /// </summary>
    public bool MultiSelect { get; set; }

    /// <summary>
    /// Optional selector merging a row into one full-width cell: a row whose result is
    /// non-<see langword="null"/> paints that text across every column (a group or separator row) and
    /// is skipped by selection, navigation and editing. Runs on the paint path — return a cached
    /// string, capture nothing.
    /// </summary>
    public Func<object?, string?>? FullRowTextSelector { get; set; }

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

    /// <summary>The horizontal scroll offset in pixels, clamped so the non-frozen columns never
    /// scroll past their total width; they are shifted left by this amount while frozen columns
    /// stay put.</summary>
    public int HorizontalOffset
    {
        get => Math.Min(field, this.MaxHorizontalOffset);
        set
        {
            field = Math.Max(0, value);
            this.Invalidate();
            this.SyncEditorToScroll();
        }
    }

    /// <summary>The selected row index into <see cref="Items"/>, or -1 for none — the current row
    /// while <see cref="MultiSelect"/> holds a wider set. Stable while the grid is sorted — sorting
    /// only reorders the presentation. Assigning collapses a multi-selection to the one row.</summary>
    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            var multiChanged = false;
            if (_multiSelection is { } multi)
            {
                multiChanged = multi.Count != (clamped >= 0 ? 1 : 0) || (clamped >= 0 && multi[0] != clamped);
                multi.Clear();
                if (clamped >= 0)
                    multi.Add(clamped);
            }

            _anchorRowIndex = clamped;
            if (clamped == _selectedRowIndex && !multiChanged)
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

    /// <summary>The selected row items in model order: the whole Ctrl/Shift set while
    /// <see cref="MultiSelect"/> has built one, otherwise the single selected row.</summary>
    public IEnumerable<object?> SelectedItems
    {
        get
        {
            if (this.MultiSelect && _multiSelection is { } multi)
            {
                for (var i = 0; i < multi.Count; ++i)
                    if (multi[i] < this.Items.Count)
                        yield return this.Items[multi[i]];

                yield break;
            }

            if (_selectedRowIndex >= 0 && _selectedRowIndex < this.Items.Count)
                yield return this.Items[_selectedRowIndex];
        }
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

    /// <summary>Raised before a cell enters edit mode; setting
    /// <see cref="DataGridViewCellCancelEventArgs.Cancel"/> keeps it read.</summary>
    public event EventHandler<DataGridViewCellCancelEventArgs>? CellBeginEdit;

    /// <summary>Raised before an edit commits, carrying the proposed value; setting
    /// <see cref="DataGridViewCellValidatingEventArgs.Cancel"/> vetoes the write and keeps the cell
    /// in edit mode.</summary>
    public event EventHandler<DataGridViewCellValidatingEventArgs>? CellValidating;

    /// <summary>Raised after a cell leaves edit mode, whether the edit committed or was cancelled.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? CellEndEdit;

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

    /// <summary>The grid claims Enter (activate/commit) always and Escape while a cell edit runs.</summary>
    protected override bool IsInputKey(Keys keyData)
        => keyData == Keys.Enter || (keyData == Keys.Escape && this.IsEditing);

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

    /// <summary>The combined pixel width of the frozen columns (the pinned leading display run).</summary>
    private int FrozenWidth
    {
        get
        {
            var total = 0;
            for (var i = 0; i < _columns.Count; ++i)
                if (_columns[i].Frozen)
                    total += _columns[i].Width;
            return total;
        }
    }

    /// <summary>The largest permitted <see cref="HorizontalOffset"/> for the current column widths;
    /// only the non-frozen columns scroll, within the viewport right of the frozen run.</summary>
    private int MaxHorizontalOffset
    {
        get
        {
            var frozenWidth = this.FrozenWidth;
            return Math.Max(0, this.TotalColumnWidth - frozenWidth - Math.Max(0, this.Width - this.ContentLeft - frozenWidth));
        }
    }

    /// <summary>Whether a cell is currently in edit mode.</summary>
    public bool IsEditing => _editRowIndex >= 0;

    /// <summary>The hosted editor control while a <see cref="DataGridViewColumnKind.Text"/> or
    /// <see cref="DataGridViewColumnKind.NumericUpDown"/> cell is in edit mode, or
    /// <see langword="null"/> (popup-based kinds host no child control).</summary>
    public Control? EditingControl => _textEditor is not null ? _textEditor : _numericEditor;

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged(EventArgs e) => this.SelectionChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="CellClick"/>.</summary>
    protected virtual void OnCellClick(DataGridViewCellEventArgs e) => this.CellClick?.Invoke(this, e);

    /// <summary>Raises <see cref="CellDoubleClick"/>.</summary>
    protected virtual void OnCellDoubleClick(DataGridViewCellEventArgs e) => this.CellDoubleClick?.Invoke(this, e);

    /// <summary>Raises <see cref="CellContentClick"/>.</summary>
    protected virtual void OnCellContentClick(DataGridViewCellEventArgs e) => this.CellContentClick?.Invoke(this, e);

    /// <summary>Raises <see cref="CellBeginEdit"/>.</summary>
    protected virtual void OnCellBeginEdit(DataGridViewCellCancelEventArgs e) => this.CellBeginEdit?.Invoke(this, e);

    /// <summary>Raises <see cref="CellValidating"/>.</summary>
    protected virtual void OnCellValidating(DataGridViewCellValidatingEventArgs e) => this.CellValidating?.Invoke(this, e);

    /// <summary>Raises <see cref="CellEndEdit"/>.</summary>
    protected virtual void OnCellEndEdit(DataGridViewCellEventArgs e) => this.CellEndEdit?.Invoke(this, e);

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
        this.SyncEditorToScroll();
    }

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedRowIndex >= this.Items.Count)
            _selectedRowIndex = this.Items.Count - 1;

        if (_multiSelection is { } multi)
            while (multi.Count > 0 && multi[^1] >= this.Items.Count)
                multi.RemoveAt(multi.Count - 1);

        if (_anchorRowIndex >= this.Items.Count)
            _anchorRowIndex = this.Items.Count - 1;

        if (this.IsEditing && _editRowIndex >= this.Items.Count)
            this.CancelEdit();

        _sortDirty = true;
        this.ClampScroll();
        this.Invalidate();
        this.SyncEditorToScroll();
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

    /// <summary>
    /// Rebuilds the display→model column map: frozen columns first, then by
    /// <see cref="DataGridViewColumn.DisplayIndex"/> (model position when unset), stably. The array is
    /// reallocated only when the column count changes; the in-place insertion sort makes the map an
    /// indirection like the sort map — <see cref="Columns"/> is never reordered.
    /// </summary>
    private void EnsureDisplayMap()
    {
        var count = _columns.Count;
        var map = _displayMap;
        if (map is null || map.Length != count)
            _displayMap = map = new int[count];

        for (var i = 0; i < count; ++i)
            map[i] = i;

        for (var i = 1; i < count; ++i)
        {
            var value = map[i];
            var j = i - 1;
            while (j >= 0 && this.CompareColumnOrder(map[j], value) > 0)
            {
                map[j + 1] = map[j];
                --j;
            }

            map[j + 1] = value;
        }
    }

    /// <summary>Orders two columns (by model index) for the display map: frozen before scrolling,
    /// then by effective display index, then by model position.</summary>
    private int CompareColumnOrder(int leftModel, int rightModel)
    {
        var left = _columns[leftModel];
        var right = _columns[rightModel];
        if (left.Frozen != right.Frozen)
            return left.Frozen ? -1 : 1;

        var leftKey = left.DisplayIndex < 0 ? leftModel : left.DisplayIndex;
        var rightKey = right.DisplayIndex < 0 ? rightModel : right.DisplayIndex;
        return leftKey != rightKey ? leftKey - rightKey : leftModel - rightModel;
    }

    private bool IsRowHidden(object? item) => this.RowHiddenSelector?.Invoke(item) ?? false;

    private bool IsRowSelectable(object? item) => this.RowSelectableSelector?.Invoke(item) ?? true;

    private string? MergedTextOf(object? item) => this.FullRowTextSelector?.Invoke(item);

    private bool IsRowNavigable(int modelIndex)
    {
        var item = this.Items[modelIndex];
        return !this.IsRowHidden(item) && this.IsRowSelectable(item) && this.MergedTextOf(item) is null;
    }

    /// <summary>Whether the row is part of the selection: the Ctrl/Shift set while
    /// <see cref="MultiSelect"/> has built one, otherwise the single selected row.</summary>
    private bool IsRowSelected(int modelIndex)
        => this.MultiSelect && _multiSelection is { } multi
            ? multi.BinarySearch(modelIndex) >= 0
            : modelIndex == _selectedRowIndex;

    /// <summary>
    /// Applies a mouse row-selection gesture. Without <see cref="MultiSelect"/> this is a plain
    /// single selection; with it, Ctrl toggles the row in the set, Shift selects the display-order
    /// range from the anchor, and a plain click collapses the set to the clicked row.
    /// </summary>
    private void SelectRowWithModifiers(int modelIndex, KeyModifiers modifiers)
    {
        if (!this.MultiSelect)
        {
            this.SelectedRowIndex = modelIndex;
            return;
        }

        var multi = _multiSelection ??= [];
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            var position = multi.BinarySearch(modelIndex);
            if (position >= 0)
                multi.RemoveAt(position);
            else
                multi.Insert(~position, modelIndex);

            _anchorRowIndex = modelIndex;
        }
        else if ((modifiers & KeyModifiers.Shift) != 0 && _anchorRowIndex >= 0)
            this.SelectDisplayRange(_anchorRowIndex, modelIndex);
        else
        {
            multi.Clear();
            multi.Add(modelIndex);
            _anchorRowIndex = modelIndex;
        }

        this.ApplyMultiSelection(modelIndex);
    }

    /// <summary>Replaces the multi-selection with the display-order range between two model rows,
    /// skipping hidden, unselectable and merged rows.</summary>
    private void SelectDisplayRange(int fromModelIndex, int toModelIndex)
    {
        this.EnsureSortMap();
        var multi = _multiSelection ??= [];
        multi.Clear();

        var from = this.ToDisplayIndex(fromModelIndex);
        var to = this.ToDisplayIndex(toModelIndex);
        if (from < 0 || to < 0)
            return;

        if (from > to)
            (from, to) = (to, from);

        for (var display = from; display <= to; ++display)
        {
            var modelIndex = this.ToModelIndex(display);
            if (this.IsRowNavigable(modelIndex))
                multi.Add(modelIndex);
        }

        multi.Sort();
    }

    /// <summary>Makes <paramref name="currentRow"/> the current row after a multi-selection gesture
    /// and reports the changed set — the gesture-shaped sibling of the
    /// <see cref="SelectedRowIndex"/> setter.</summary>
    private void ApplyMultiSelection(int currentRow)
    {
        _selectedRowIndex = currentRow;
        this.EnsureVisible(currentRow);
        this.Invalidate();
        this.OnSelectionChanged(EventArgs.Empty);
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

    /// <summary>Finds the column under the given x-coordinate, walking the display order — frozen
    /// columns at their pinned positions, the rest shifted by <see cref="HorizontalOffset"/> (and
    /// hidden where the frozen run covers them). Returns the model index, or -1 (row-header zone or
    /// past the last column).</summary>
    private int HitTestColumn(int x, out int cellLeft)
    {
        cellLeft = 0;
        var contentLeft = this.ContentLeft;
        if (x < contentLeft)
            return -1;

        this.EnsureDisplayMap();
        var map = _displayMap!;
        var scrollEdge = contentLeft + this.FrozenWidth;
        var cx = contentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                cx -= this.HorizontalOffset;
            }

            var width = column.Width;
            if (x >= cx && x < cx + width && (column.Frozen || x >= scrollEdge))
            {
                cellLeft = cx;
                return map[d];
            }

            cx += width;
        }

        return -1;
    }

    /// <summary>Finds the column whose right divider lies within ±3 px of the given x-coordinate,
    /// in display order. Returns the model index, or -1.</summary>
    private int HitTestColumnDivider(int x)
    {
        this.EnsureDisplayMap();
        var map = _displayMap!;
        var contentLeft = this.ContentLeft;
        var scrollEdge = contentLeft + this.FrozenWidth;
        var cx = contentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                cx -= this.HorizontalOffset;
            }

            cx += column.Width;
            if ((column.Frozen || cx >= scrollEdge) && Math.Abs(x - cx) <= _DividerZone)
                return map[d];
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
    /// unselectable rows; with no selection, any move selects the first reachable row. With
    /// <paramref name="extend"/> (Shift under <see cref="MultiSelect"/>) the move grows the
    /// display-order range from the anchor instead of collapsing the set.</summary>
    private void MoveSelection(int steps, bool extend = false)
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

        if (target < 0)
            return;

        var modelIndex = this.ToModelIndex(target);
        if (extend && this.MultiSelect && _anchorRowIndex >= 0)
        {
            this.SelectDisplayRange(_anchorRowIndex, modelIndex);
            this.ApplyMultiSelection(modelIndex);
        }
        else
            this.SelectedRowIndex = modelIndex;
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

        // A press on the grid surface while a cell edits is a commit point (click-away); a
        // validation veto keeps the edit alive and swallows the press.
        if (this.IsEditing && !this.CommitEdit())
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
        if (this.MergedTextOf(item) is not null)
            return; // merged rows have no cells and take no selection

        if (this.IsRowSelectable(item))
            this.SelectRowWithModifiers(rowIndex, e.Modifiers);

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

        if (this.AllowUserToOrderColumns)
            _dragColumnIndex = columnIndex; // armed; a later move past a neighbor reorders

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
        {
            this.OnCellDoubleClick(new(rowIndex, columnIndex));
            this.BeginEdit(rowIndex, columnIndex);
        }

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
        if (_resizeColumnIndex >= 0 && _resizeColumnIndex < _columns.Count)
        {
            var column = _columns[_resizeColumnIndex];
            var width = Math.Max(_MinColumnWidth, _resizeStartWidth + (e.X - _resizeStartX));
            if (width == column.Width)
                return;

            column.Width = width;
            this.Invalidate();
            return;
        }

        if (_dragColumnIndex < 0 || _dragColumnIndex >= _columns.Count)
            return;

        var target = this.HitTestColumn(e.X, out _);
        if (target < 0 || target == _dragColumnIndex)
            return;

        if (_columns[target].Frozen != _columns[_dragColumnIndex].Frozen)
            return; // a drag never crosses the frozen boundary

        this.MoveColumnToDisplayPositionOf(_dragColumnIndex, target);
        this.Invalidate();
    }

    /// <summary>
    /// Slides the dragged column to the display position the target column occupies, then rewrites
    /// every column's <see cref="DataGridViewColumn.DisplayIndex"/> from the resulting order — the
    /// model <see cref="Columns"/> list is never touched.
    /// </summary>
    private void MoveColumnToDisplayPositionOf(int modelIndex, int targetModelIndex)
    {
        this.EnsureDisplayMap();
        var map = _displayMap!;
        var from = Array.IndexOf(map, modelIndex);
        var to = Array.IndexOf(map, targetModelIndex);
        if (from < 0 || to < 0 || from == to)
            return;

        var moved = map[from];
        if (from < to)
            Array.Copy(map, from + 1, map, from, to - from);
        else
            Array.Copy(map, to, map, to + 1, from - to);
        map[to] = moved;

        for (var d = 0; d < map.Length; ++d)
            _columns[map[d]].DisplayIndex = d;
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _resizeColumnIndex = -1;
        _dragColumnIndex = -1;
    }

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
        this.SyncEditorToScroll();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (this.IsEditing)
        {
            this.HandleEditKey(e);
            return; // the active edit owns the keyboard; grid navigation resumes afterwards
        }

        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Down: this.MoveSelection(1, e.Shift); break;
            case Keys.Up: this.MoveSelection(-1, e.Shift); break;
            case Keys.Home when this.Items.Count > 0: this.SelectEdge(first: true); break;
            case Keys.End: this.SelectEdge(first: false); break;
            case Keys.PageDown: this.MoveSelection(this.VisibleRowCount, e.Shift); break;
            case Keys.PageUp: this.MoveSelection(-this.VisibleRowCount, e.Shift); break;
            case Keys.F2 when _selectedRowIndex >= 0 && _columns.Count > 0:
                this.BeginEdit(_selectedRowIndex, Math.Min(_currentColumnIndex, _columns.Count - 1));
                break;
            case Keys.C when e.Control:
            {
                var content = this.GetClipboardContent();
                if (content.Length > 0)
                    this.Backend?.SetClipboardText(content);
                break;
            }

            case Keys.Space or Keys.Enter when _selectedRowIndex >= 0 && _columns.Count > 0:
                this.OnCellClick(new(_selectedRowIndex, Math.Min(_currentColumnIndex, _columns.Count - 1)));
                break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    /// <inheritdoc/>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (this.IsEditing || char.IsControl(e.KeyChar))
            return;

        if (_selectedRowIndex < 0 || _columns.Count == 0)
            return;

        var columnIndex = Math.Min(_currentColumnIndex, _columns.Count - 1);
        var kind = _columns[columnIndex].Kind;
        if (kind is not (DataGridViewColumnKind.Text or DataGridViewColumnKind.NumericUpDown))
            return; // typing only seeds editors that take free text

        if (!this.BeginEdit(_selectedRowIndex, columnIndex))
            return;

        if (_textEditor is { } textEditor)
            textEditor.Text = e.KeyChar.ToString();
        else if (_numericEditor is { } numericEditor)
            numericEditor.Text = e.KeyChar.ToString();

        e.Handled = true;
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
        this.EnsureDisplayMap();
        this.AutoSizeColumns(g);

        var header = this.HeaderHeight;
        var contentLeft = this.ContentLeft;
        var frozenWidth = this.FrozenWidth;
        var scrollEdge = contentLeft + frozenWidth;
        var showGridLines = this.ShowGridLines;
        var count = this.Items.Count;

        g.PushClip(new Rectangle(contentLeft, 0, Math.Max(0, width - contentLeft), height));

        if (this.ShowColumnHeaders)
        {
            g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, width, header));
            if (frozenWidth > 0)
            {
                g.PushClip(new Rectangle(scrollEdge, 0, Math.Max(0, width - scrollEdge), header));
                this.PaintHeaderCells(g, theme, header, frozen: false);
                g.PopClip();
                this.PaintHeaderCells(g, theme, header, frozen: true);
            }
            else
                this.PaintHeaderCells(g, theme, header, frozen: false);

            g.DrawLine(theme.Border, 0, header - 1, width, header - 1);
        }

        // Pass 1: row backgrounds, merged rows and separators — and every cell while nothing is
        // frozen, so the common grid pays for exactly one row walk.
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
            var selected = this.IsRowSelected(modelIndex);
            if (selected)
                g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, width, rowHeight));
            else if (this.RowBackColorSelector?.Invoke(item) is { } rowBack)
                g.FillRectangle(rowBack, new Rectangle(0, y, width, rowHeight));
            else if (this.AlternatingRows && (displayIndex & 1) == 1)
                g.FillRectangle(this.AlternatingRowColor, new Rectangle(0, y, width, rowHeight));

            if (this.MergedTextOf(item) is { } mergedText)
                g.DrawText(mergedText, theme.DefaultFont, theme.ControlText,
                    new Rectangle(contentLeft + _CellPadding, y, Math.Max(0, width - contentLeft - _CellPadding), rowHeight), ContentAlignment.MiddleLeft);
            else if (frozenWidth == 0)
                this.PaintRowCells(g, theme, item, y, rowHeight, selected, frozen: false);

            if (showGridLines)
                g.DrawLine(theme.GridLine, 0, y + rowHeight - 1, width, y + rowHeight - 1);

            y += rowHeight;
        }

        if (frozenWidth > 0)
        {
            // Pass 2: the scrolling cells, clipped so they slide under the frozen run; pass 3: the
            // frozen cells at their pinned positions, sealed with the frozen seam.
            g.PushClip(new Rectangle(scrollEdge, header, Math.Max(0, width - scrollEdge), Math.Max(0, height - header)));
            this.PaintCellRun(g, theme, header, height, count, frozen: false);
            g.PopClip();
            this.PaintCellRun(g, theme, header, height, count, frozen: true);
            g.DrawLine(theme.Border, scrollEdge - 1, 0, scrollEdge - 1, height);
        }

        if (showGridLines)
        {
            if (this.FullRowTextSelector is null)
                this.PaintColumnGridLines(g, theme, header, height);
            else
                this.PaintColumnGridLineSegments(g, theme, header, height, count);
        }

        g.PopClip();

        if (this.ShowRowHeaders)
            this.PaintRowHeaders(g, theme, header, height, count);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <summary>Paints the header cells of one run — the frozen columns at their pinned positions or
    /// the scrolling columns shifted by <see cref="HorizontalOffset"/> — walking the display order.</summary>
    private void PaintHeaderCells(IGraphics g, ITheme theme, int header, bool frozen)
    {
        var map = _displayMap!;
        var x = this.ContentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                x -= this.HorizontalOffset;
            }

            if (column.Frozen == frozen)
            {
                var cellRect = new Rectangle(x + _CellPadding, 0, Math.Max(0, column.Width - (_CellPadding * 2)), header);
                g.DrawText(column.HeaderText, theme.DefaultFont, theme.HeaderText, cellRect, column.Alignment);
                if (ReferenceEquals(column, _sortedColumn) && _sortOrder != SortOrder.None)
                    GlyphRenderer.DrawSortArrow(g, theme.HeaderText, new Rectangle(x + column.Width - 14, 0, 10, header), _sortOrder == SortOrder.Ascending);
            }

            x += column.Width;
        }
    }

    /// <summary>Paints the data cells of one row for one run (frozen or scrolling columns), walking
    /// the display order with the same geometry as <see cref="PaintHeaderCells"/>.</summary>
    private void PaintRowCells(IGraphics g, ITheme theme, object? item, int y, int rowHeight, bool selected, bool frozen)
    {
        var map = _displayMap!;
        var x = this.ContentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                x -= this.HorizontalOffset;
            }

            if (column.Frozen == frozen)
                this.PaintCell(g, theme, column, item, new Rectangle(x, y, column.Width, rowHeight), selected);

            x += column.Width;
        }
    }

    /// <summary>Walks the visible rows painting the cells of one column run — the frozen-column
    /// passes of <see cref="OnPaint"/>. Merged rows were already painted full-width and are skipped.</summary>
    private void PaintCellRun(IGraphics g, ITheme theme, int header, int height, int count, bool frozen)
    {
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
            if (this.MergedTextOf(item) is null)
                this.PaintRowCells(g, theme, item, y, rowHeight, this.IsRowSelected(modelIndex), frozen);

            y += rowHeight;
        }
    }

    /// <summary>Draws the vertical column dividers between <paramref name="top"/> and
    /// <paramref name="bottom"/>, walking the display order; scrolled dividers under the frozen run
    /// are suppressed.</summary>
    private void PaintColumnGridLines(IGraphics g, ITheme theme, int top, int bottom)
    {
        var map = _displayMap!;
        var contentLeft = this.ContentLeft;
        var scrollEdge = contentLeft + this.FrozenWidth;
        var width = this.Width;
        var x = contentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                x -= this.HorizontalOffset;
            }

            x += column.Width;
            var edge = column.Frozen ? contentLeft : scrollEdge;
            if (x > edge && x < width)
                g.DrawLine(theme.GridLine, x - 1, top, x - 1, bottom);
        }
    }

    /// <summary>Draws the vertical column dividers row by row so merged rows stay one uninterrupted
    /// cell — the gridline variant used while <see cref="FullRowTextSelector"/> is set.</summary>
    private void PaintColumnGridLineSegments(IGraphics g, ITheme theme, int header, int height, int count)
    {
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
            if (this.MergedTextOf(item) is null)
                this.PaintColumnGridLines(g, theme, y, y + rowHeight);

            y += rowHeight;
        }
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

            case DataGridViewColumnKind.ComboBox:
            {
                var arrowZone = Math.Min(_ComboArrowZone, cellRect.Width);
                var textRect = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - _CellPadding - arrowZone), cellRect.Height);
                g.DrawText(GetDisplayText(column, item), theme.DefaultFont, foreColor, textRect, alignment);

                // The drop arrow: a themed triangle of stacked lines, like the ComboBox field's.
                var centerX = cellRect.Right - arrowZone + (arrowZone / 2);
                var arrowTop = cellRect.Y + ((cellRect.Height - _ComboArrowRows) / 2);
                for (var i = 0; i < _ComboArrowRows; ++i)
                    g.DrawLine(foreColor, centerX - _ComboArrowRows + 1 + i, arrowTop + i, centerX + _ComboArrowRows - 1 - i, arrowTop + i);
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

    // --- Cell editing ------------------------------------------------------------------------------

    /// <summary>
    /// Puts the given cell into edit mode: a hosted <see cref="TextBox"/>
    /// (<see cref="DataGridViewColumnKind.Text"/>) or <see cref="NumericUpDown"/>
    /// (<see cref="DataGridViewColumnKind.NumericUpDown"/>) positioned over the cell, or a popup — the
    /// choice list of a <see cref="DataGridViewColumnKind.ComboBox"/> cell, the month calendar of a
    /// <see cref="DataGridViewColumnKind.DateTime"/> cell — below it. Refused (returning
    /// <see langword="false"/>) for read-only cells, kinds without their edit selectors/setters,
    /// merged or hidden rows, cells outside the visible window, a veto from
    /// <see cref="CellBeginEdit"/>, or popup kinds before realization. An edit already active on
    /// another cell is committed first; its validation veto also refuses the new edit.
    /// </summary>
    public bool BeginEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.Items.Count || columnIndex < 0 || columnIndex >= _columns.Count)
            return false;

        if (_editRowIndex == rowIndex && _editColumnIndex == columnIndex)
            return true;

        if (this.IsEditing && !this.CommitEdit())
            return false;

        var column = _columns[columnIndex];
        var item = this.Items[rowIndex];
        if (this.IsCellReadOnly(item, column) || !IsCellEditable(column))
            return false;

        if (this.MergedTextOf(item) is not null || this.IsRowHidden(item))
            return false;

        var isPopupKind = column.Kind is DataGridViewColumnKind.ComboBox or DataGridViewColumnKind.DateTime;
        var backend = this.Backend;
        if (isPopupKind && backend is null)
            return false; // only a live widget knows where to float the popup

        var beginArgs = new DataGridViewCellCancelEventArgs(rowIndex, columnIndex);
        this.OnCellBeginEdit(beginArgs);
        if (beginArgs.Cancel)
            return false;

        this.EnsureVisible(rowIndex);
        var cellBounds = this.GetCellBounds(rowIndex, columnIndex);
        if (cellBounds.IsEmpty)
            return false;

        switch (column.Kind)
        {
            case DataGridViewColumnKind.Text:
            {
                var editor = new TextBox { Text = GetDisplayText(column, item), Bounds = cellBounds, TabStop = false };
                _textEditor = editor;
                this.Controls.Add(editor);
                break;
            }

            case DataGridViewColumnKind.NumericUpDown:
            {
                var editor = new NumericUpDown
                {
                    Maximum = column.Maximum,
                    Minimum = column.Minimum,
                    Increment = column.Increment,
                    DecimalPlaces = column.DecimalPlaces,
                    Value = column.NumberSelector!(item),
                    Bounds = cellBounds,
                    TabStop = false,
                };
                _numericEditor = editor;
                this.Controls.Add(editor);
                break;
            }

            case DataGridViewColumnKind.ComboBox:
                this.OpenComboPopup(backend!, column, item, cellBounds);
                break;

            default: // DataGridViewColumnKind.DateTime — IsCellEditable admits no other kind here
                this.OpenCalendarPopup(backend!, column, item, cellBounds);
                break;
        }

        _editRowIndex = rowIndex;
        _editColumnIndex = columnIndex;
        _currentColumnIndex = columnIndex;
        this.Invalidate();
        return true;
    }

    /// <summary>
    /// Commits the active edit: the editor's value runs through <see cref="CellValidating"/> (a veto
    /// returns <see langword="false"/> and keeps the cell in edit mode), is written through the
    /// column's setter, and the cell leaves edit mode raising <see cref="CellEndEdit"/>. For the
    /// popup kinds — which commit through their own pick gestures — this closes the popup without a
    /// write. A no-op returning <see langword="true"/> while nothing edits.
    /// </summary>
    public bool CommitEdit()
    {
        if (!this.IsEditing)
            return true;

        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        var column = _columns[columnIndex];
        var item = this.Items[rowIndex];
        switch (column.Kind)
        {
            case DataGridViewColumnKind.Text:
            {
                var text = _textEditor!.Text;
                if (!this.ValidateCell(rowIndex, columnIndex, text))
                    return false;

                column.TextSetter!(item, text);
                break;
            }

            case DataGridViewColumnKind.NumericUpDown:
            {
                var value = _numericEditor!.Value; // the getter commits a pending typed edit first
                if (!this.ValidateCell(rowIndex, columnIndex, value))
                    return false;

                column.NumberSetter!(item, value);
                break;
            }
        }

        this.EndEdit(rowIndex, columnIndex);
        return true;
    }

    /// <summary>Leaves edit mode without writing anything, raising <see cref="CellEndEdit"/>. A
    /// no-op while nothing edits.</summary>
    public void CancelEdit()
    {
        if (this.IsEditing)
            this.EndEdit(_editRowIndex, _editColumnIndex);
    }

    /// <summary>Whether the column's kind can edit at all: its kind-specific selectors and setter
    /// must be present, like a check cell without a <see cref="DataGridViewColumn.CheckedSetter"/>
    /// is display-only.</summary>
    private static bool IsCellEditable(DataGridViewColumn column) => column.Kind switch
    {
        DataGridViewColumnKind.Text => column.TextSetter is not null,
        DataGridViewColumnKind.ComboBox => column.ItemsSelector is not null && column.ValueSetter is not null,
        DataGridViewColumnKind.NumericUpDown => column.NumberSelector is not null && column.NumberSetter is not null,
        DataGridViewColumnKind.DateTime => column.DateSelector is not null && column.DateSetter is not null,
        _ => false,
    };

    /// <summary>Runs <see cref="CellValidating"/> over a proposed value; <see langword="false"/>
    /// means a handler vetoed the commit.</summary>
    private bool ValidateCell(int rowIndex, int columnIndex, object? proposedValue)
    {
        var e = new DataGridViewCellValidatingEventArgs(rowIndex, columnIndex, proposedValue);
        this.OnCellValidating(e);
        return !e.Cancel;
    }

    /// <summary>Tears the editor surface down (hosted child or popup), resets the edit state and
    /// raises <see cref="CellEndEdit"/> — the shared tail of commit and cancel.</summary>
    private void EndEdit(int rowIndex, int columnIndex)
    {
        _editRowIndex = -1;
        _editColumnIndex = -1;
        _editChoices = null;
        if (_textEditor is { } textEditor)
        {
            _textEditor = null;
            this.Controls.Remove(textEditor);
        }

        if (_numericEditor is { } numericEditor)
        {
            _numericEditor = null;
            this.Controls.Remove(numericEditor);
        }

        if (_editPopupShown)
        {
            _editPopupShown = false;
            _editPopup?.Hide();
        }

        this.Invalidate();
        this.OnCellEndEdit(new(rowIndex, columnIndex));
    }

    /// <summary>Handles a key while a cell edits: Enter commits and Escape cancels everywhere; the
    /// combo popup adds hover navigation, the calendar popup its month navigation. All other keys
    /// stay with the edit — grid navigation resumes when it ends.</summary>
    private void HandleEditKey(KeyEventArgs e)
    {
        switch (_columns[_editColumnIndex].Kind)
        {
            case DataGridViewColumnKind.ComboBox:
                switch (e.KeyCode)
                {
                    case Keys.Escape:
                        this.CancelEdit();
                        e.Handled = true;
                        break;

                    case Keys.Enter:
                        if (_editChoices is { } choices && _editHoverIndex >= 0 && _editHoverIndex < choices.Count)
                            this.CommitComboChoice(_editHoverIndex);
                        else
                            this.CancelEdit();

                        e.Handled = true;
                        break;

                    case Keys.Down:
                        this.MoveComboHover(+1);
                        e.Handled = true;
                        break;

                    case Keys.Up:
                        this.MoveComboHover(-1);
                        e.Handled = true;
                        break;
                }

                break;

            case DataGridViewColumnKind.DateTime:
                if (e.KeyCode == Keys.Escape)
                {
                    this.CancelEdit();
                    e.Handled = true;
                }
                else
                    _editCalendar?.HandleKeyDown(e); // the popup calendar owns navigation while open

                break;

            default: // hosted editors
                switch (e.KeyCode)
                {
                    case Keys.Enter:
                        this.CommitEdit();
                        e.Handled = true;
                        break;

                    case Keys.Escape:
                        this.CancelEdit();
                        e.Handled = true;
                        break;
                }

                break;
        }
    }

    /// <summary>Repositions the hosted editor over its (possibly scrolled) cell, or commits when the
    /// edited row left the visible window — the classic grid's scroll behavior. A validation veto on
    /// that forced commit abandons the edit instead, so scrolling never wedges.</summary>
    private void SyncEditorToScroll()
    {
        if (!this.IsEditing)
            return;

        var bounds = this.GetCellBounds(_editRowIndex, _editColumnIndex);
        if (bounds.IsEmpty)
        {
            if (!this.CommitEdit())
                this.CancelEdit();
            return;
        }

        if (_textEditor is { } textEditor)
            textEditor.Bounds = bounds;
        else if (_numericEditor is { } numericEditor)
            numericEditor.Bounds = bounds;
    }

    /// <inheritdoc/>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        this.SyncEditorToScroll();
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        this.CancelEdit();
        _editPopupShown = false;
        _editPopup?.Dispose();
        _editPopup = null;
        _editCalendar = null;
    }

    /// <summary>
    /// The client-space rectangle of a cell, honoring scroll positions, per-row heights, hidden rows,
    /// sorting and the display order (frozen columns at their pinned x). <see cref="Rectangle.Empty"/>
    /// when the cell lies outside the visible window — the geometry editors are hosted over.
    /// </summary>
    public Rectangle GetCellBounds(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.Items.Count || columnIndex < 0 || columnIndex >= _columns.Count)
            return Rectangle.Empty;

        this.EnsureSortMap();
        this.EnsureDisplayMap();

        var count = this.Items.Count;
        var height = this.Height;
        var y = this.HeaderHeight;
        var display = Math.Max(0, _topRow);
        var rowTop = -1;
        var rowHeight = 0;
        while (y < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            var item = this.Items[modelIndex];
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var h = this.GetRowHeightFor(item);
            if (modelIndex == rowIndex)
            {
                rowTop = y;
                rowHeight = h;
                break;
            }

            y += h;
        }

        if (rowTop < 0)
            return Rectangle.Empty;

        var map = _displayMap!;
        var contentLeft = this.ContentLeft;
        var scrollEdge = contentLeft + this.FrozenWidth;
        var x = contentLeft;
        var passedFrozen = false;
        for (var d = 0; d < map.Length; ++d)
        {
            var column = _columns[map[d]];
            if (!passedFrozen && !column.Frozen)
            {
                passedFrozen = true;
                x -= this.HorizontalOffset;
            }

            if (map[d] == columnIndex)
            {
                if (x >= this.Width || (!column.Frozen && x + column.Width <= scrollEdge))
                    return Rectangle.Empty; // scrolled out of the viewport or fully under the frozen run

                return new Rectangle(x, rowTop, column.Width, rowHeight);
            }

            x += column.Width;
        }

        return Rectangle.Empty;
    }

    // --- The edit popup (combo choices / calendar) -------------------------------------------------

    /// <summary>Opens the choice list of a <see cref="DataGridViewColumnKind.ComboBox"/> cell below
    /// the cell, hover starting on the current value.</summary>
    private void OpenComboPopup(IPlatformBackend backend, DataGridViewColumn column, object? item, Rectangle cellBounds)
    {
        var choices = column.ItemsSelector!(item);
        _editChoices = choices;
        _editPopupRows = Math.Max(1, Math.Min(choices.Count, _MaxComboPopupRows));
        _editPopupSize = new Size(cellBounds.Width, _editPopupRows * this.Theme.RowHeight);
        _editPopupTop = 0;

        _editHoverIndex = -1;
        var current = column.ValueSelector(item);
        for (var i = 0; i < choices.Count; ++i)
            if (Equals(choices[i], current))
            {
                _editHoverIndex = i;
                break;
            }

        this.EnsureComboPopupVisible(_editHoverIndex);
        var popup = this.EnsureEditPopup(backend);
        _editPopupShown = true;
        popup.ShowAt(this.PointToScreen(new Point(cellBounds.X, cellBounds.Bottom)), _editPopupSize);
    }

    /// <summary>Opens the month calendar of a <see cref="DataGridViewColumnKind.DateTime"/> cell
    /// below the cell, its page centered on the cell's current date — the same engine and popup
    /// geometry as <see cref="DateTimePicker"/>.</summary>
    private void OpenCalendarPopup(IPlatformBackend backend, DataGridViewColumn column, object? item, Rectangle cellBounds)
    {
        var calendar = _editCalendar ??= new()
        {
            Invalidated = () => _editPopup?.InvalidateAll(),
            DateSelected = this.OnEditCalendarDateSelected,
        };

        var theme = this.Theme;
        _editPopupSize = new Size(7 * (theme.RowHeight + 4), 8 * theme.RowHeight);

        var day = column.DateSelector!(item).Date;
        calendar.TodayDate = DateTime.Today;
        calendar.SelectionStart = day;
        calendar.SelectionEnd = day;
        calendar.AnchorDate = day;
        calendar.FocusDate = day;
        calendar.DisplayMonth = new(day.Year, day.Month, 1);

        var popup = this.EnsureEditPopup(backend);
        _editPopupShown = true;
        popup.ShowAt(this.PointToScreen(new Point(cellBounds.X, cellBounds.Bottom)), _editPopupSize);
    }

    /// <summary>Creates the shared edit popup on first use; its handlers dispatch on the kind of the
    /// cell currently editing.</summary>
    private IPopupPeer EnsureEditPopup(IPlatformBackend backend)
    {
        var popup = _editPopup;
        if (popup is not null)
            return popup;

        popup = backend.CreatePopup();
        popup.Paint += (_, e) => this.OnEditPopupPaint(e);
        popup.MouseDown += (_, e) => this.OnEditPopupMouseDown(e);
        popup.MouseMove += (_, e) => this.OnEditPopupMouseMove(e);
        popup.MouseUp += (_, e) => this.OnEditPopupMouseUp(e);
        popup.MouseWheel += (_, e) => this.OnEditPopupMouseWheel(e);
        popup.KeyDown += (_, e) => this.OnKeyDown(e); // backends with a keyboard grab route keys here
        popup.Dismissed += (_, _) => this.OnEditPopupDismissed();
        return _editPopup = popup;
    }

    /// <summary>Whether the active edit is the popup calendar (as opposed to the combo list).</summary>
    private bool IsCalendarEditing => this.IsEditing && _columns[_editColumnIndex].Kind == DataGridViewColumnKind.DateTime;

    private void OnEditPopupPaint(PaintEventArgs e)
    {
        if (!this.IsEditing)
            return;

        if (this.IsCalendarEditing)
        {
            _editCalendar!.Paint(e.Graphics, this.Theme, _editPopupSize, true);
            return;
        }

        // The combo choice list, painted exactly like ComboBox drop-down rows.
        var g = e.Graphics;
        var theme = this.Theme;
        var size = _editPopupSize;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, size.Width, size.Height));

        var column = _columns[_editColumnIndex];
        var choices = _editChoices!;
        var rowHeight = theme.RowHeight;
        var last = Math.Min(choices.Count, _editPopupTop + _editPopupRows);
        for (var i = _editPopupTop; i < last; ++i)
        {
            var rowRect = new Rectangle(0, (i - _editPopupTop) * rowHeight, size.Width, rowHeight);
            var hovered = i == _editHoverIndex;
            if (hovered)
                g.FillRectangle(theme.SelectionBackground, rowRect);

            ListBox.DrawRowContent(g, theme, rowRect, ChoiceDisplayText(column, choices[i]), null, hovered);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
    }

    private void OnEditPopupMouseDown(MouseEventArgs e)
    {
        if (!this.IsEditing)
            return;

        if (this.IsCalendarEditing)
        {
            _editCalendar!.HandleMouseDown(this.Theme, _editPopupSize, e);
            return;
        }

        if (e.Button != MouseButtons.Left || e.Y < 0)
            return;

        var row = _editPopupTop + (e.Y / this.Theme.RowHeight);
        if (row < _editChoices!.Count)
            this.CommitComboChoice(row);
    }

    private void OnEditPopupMouseMove(MouseEventArgs e)
    {
        if (!this.IsEditing)
            return;

        if (this.IsCalendarEditing)
        {
            _editCalendar!.HandleMouseMove(this.Theme, _editPopupSize, e);
            return;
        }

        if (e.Y < 0)
            return;

        var row = _editPopupTop + (e.Y / this.Theme.RowHeight);
        if (row >= _editChoices!.Count || row == _editHoverIndex)
            return;

        _editHoverIndex = row;
        _editPopup?.InvalidateAll();
    }

    private void OnEditPopupMouseUp(MouseEventArgs e)
    {
        if (this.IsCalendarEditing)
            _editCalendar!.HandleMouseUp(e);
    }

    private void OnEditPopupMouseWheel(MouseEventArgs e)
    {
        if (!this.IsEditing)
            return;

        if (this.IsCalendarEditing)
        {
            _editCalendar!.HandleMouseWheel(e.Delta);
            return;
        }

        var maxTop = Math.Max(0, _editChoices!.Count - _editPopupRows);
        var top = Math.Clamp(_editPopupTop - (Math.Sign(e.Delta) * 3), 0, maxTop);
        if (top == _editPopupTop)
            return;

        _editPopupTop = top;
        _editPopup?.InvalidateAll();
    }

    /// <summary>Reacts to light dismissal (click outside, grab loss, Escape): the surface is already
    /// hidden, so the edit just ends without a write — dismissal cancels.</summary>
    private void OnEditPopupDismissed()
    {
        _editPopupShown = false;
        this.CancelEdit();
    }

    /// <summary>Validates and writes the picked combo choice through the column's
    /// <see cref="DataGridViewColumn.ValueSetter"/>, ending the edit; a validation veto keeps the
    /// popup open.</summary>
    private void CommitComboChoice(int index)
    {
        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        var choice = _editChoices![index];
        if (!this.ValidateCell(rowIndex, columnIndex, choice))
            return;

        _columns[columnIndex].ValueSetter!(this.Items[rowIndex], choice);
        this.EndEdit(rowIndex, columnIndex);
    }

    /// <summary>Validates and writes the day picked in the popup calendar through the column's
    /// <see cref="DataGridViewColumn.DateSetter"/> — keeping the time of day — and ends the edit; a
    /// validation veto keeps the popup open.</summary>
    private void OnEditCalendarDateSelected()
    {
        if (!this.IsEditing)
            return;

        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        var column = _columns[columnIndex];
        var item = this.Items[rowIndex];
        var proposed = _editCalendar!.SelectionStart.Date + column.DateSelector!(item).TimeOfDay;
        if (!this.ValidateCell(rowIndex, columnIndex, proposed))
            return;

        column.DateSetter!(item, proposed);
        this.EndEdit(rowIndex, columnIndex);
    }

    /// <summary>Moves the combo hover row by <paramref name="delta"/>, clamped, scrolling it into view.</summary>
    private void MoveComboHover(int delta)
    {
        var count = _editChoices!.Count;
        if (count == 0)
            return;

        var target = Math.Clamp(_editHoverIndex + delta, 0, count - 1);
        if (target == _editHoverIndex)
            return;

        _editHoverIndex = target;
        this.EnsureComboPopupVisible(target);
        _editPopup?.InvalidateAll();
    }

    /// <summary>Scrolls the combo popup so the given choice row is visible.</summary>
    private void EnsureComboPopupVisible(int index)
    {
        if (index < 0)
            return;

        if (index < _editPopupTop)
            _editPopupTop = index;
        else if (index >= _editPopupTop + _editPopupRows)
            _editPopupTop = index - _editPopupRows + 1;

        _editPopupTop = Math.Clamp(_editPopupTop, 0, Math.Max(0, (_editChoices?.Count ?? 0) - _editPopupRows));
    }

    /// <summary>The display text of one combo choice: the column's
    /// <see cref="DataGridViewColumn.ItemDisplaySelector"/>, falling back to <c>ToString()</c>.</summary>
    private static string ChoiceDisplayText(DataGridViewColumn column, object? choice)
        => column.ItemDisplaySelector?.Invoke(choice) ?? choice?.ToString() ?? string.Empty;

    // --- Clipboard ---------------------------------------------------------------------------------

    /// <summary>
    /// The selection as clipboard text: one line per selected row in display order, the cells in
    /// display column order formatted through the usual display selectors and joined with tabs;
    /// merged rows contribute their full-row text as the whole line. Empty without a selection.
    /// Ctrl+C puts exactly this on the system clipboard through the backend.
    /// </summary>
    public string GetClipboardContent()
    {
        this.EnsureSortMap();
        this.EnsureDisplayMap();

        var builder = new StringBuilder();
        var map = _displayMap!;
        var count = this.Items.Count;
        var first = true;
        for (var display = 0; display < count; ++display)
        {
            var modelIndex = this.ToModelIndex(display);
            if (!this.IsRowSelected(modelIndex))
                continue;

            if (!first)
                builder.Append("\r\n");
            first = false;

            var item = this.Items[modelIndex];
            if (this.MergedTextOf(item) is { } mergedText)
            {
                builder.Append(mergedText);
                continue;
            }

            for (var d = 0; d < map.Length; ++d)
            {
                if (d > 0)
                    builder.Append('\t');
                builder.Append(GetDisplayText(_columns[map[d]], item));
            }
        }

        return builder.ToString();
    }
}
