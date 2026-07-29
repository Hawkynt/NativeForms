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
/// trim/AOT-safe. Columns render as text, check, button, link, multi-image, progress, color, list or
/// checked-list cells (<see cref="DataGridViewColumnKind"/>), headers sort through an index indirection that never
/// mutates <see cref="Items"/>, and presentation (row colors/heights/visibility, cell styles,
/// display formatting, cell tooltips) is driven by optional per-row/per-cell selectors. Cells edit
/// in place through <see cref="BeginEdit"/> — a hosted editor, popup or dialog per
/// <see cref="DataGridViewColumnKind"/>, entered per <see cref="EditMode"/>, with Tab/Enter walking
/// the editable cells — columns can be frozen, fill-sized by weight, drag-reordered through a
/// display-order indirection and copied to (or pasted from, via <see cref="Paste"/>) the clipboard
/// as tab-separated text, <see cref="MultiSelect"/> extends the full-row selection to Ctrl/Shift
/// sets, and overflowing content grows interactive scrollbar strips along the grid's edges.
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
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.Table;

    private const int _CellPadding = 4;
    private const int _IconGap = 4;
    private const int _WheelRows = 3;
    private const int _WheelHorizontalStep = 30;
    private const int _DividerZone = 3;
    private const int _MinColumnWidth = 8;
    private const int _CheckBoxSize = 14;
    private const int _ComboArrowRows = 5;
    private const int _ComboArrowZone = 16;
    private const int _MaxComboPopupRows = 8;
    private const int _MaxListPopupRows = 12;
    private const int _CheckGlyphGap = 4;

    /// <summary>What joins the display texts of a set-valued cell's items into its closed-cell
    /// summary — and what <see cref="Paste"/> splits such a summary back apart on.</summary>
    private const string _SetSummarySeparator = ", ";

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
    private DomainUpDown? _domainEditor;
    private TimePicker? _timeEditor;
    private bool _editDirty;
    private IPopupPeer? _editPopup;
    private bool _editPopupShown;
    private CalendarCore? _editCalendar;
    private IReadOnlyList<object?>? _editChoices;
    private bool[]? _editItemStates;
    private int _editAnchorIndex = -1;
    private int _editHoverIndex;
    private int _editPopupTop;
    private int _editPopupRows;
    private Size _editPopupSize;

    private bool _verticalScrollBarVisible;
    private bool _horizontalScrollBarVisible;
    /// <summary>The live rubber-band gesture, allocated only while one is in flight.</summary>
    private MarqueeDrag? _marquee;

    private bool _scrollDragging;
    private bool _scrollDragVertical;
    private int _scrollDragOffset;

    private int _hoverRowIndex = -1;
    private int _hoverColumnIndex = -1;
    private int _hoverImageIndex = -1;   // the hovered icon of a MultiImage cell, for per-icon tooltips
    private Point _hoverPoint;
    private Timer? _tipTimer;
    private IPopupPeer? _tipPopup;
    private string _tipText = string.Empty;
    private bool _tipShown;
    private bool _tipAutoPopPhase;

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

    /// <summary>
    /// Whether each column header offers a filter: a funnel glyph that opens the column's distinct
    /// display values as a searchable, checkable menu (PRD §14). Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The menu is a <see cref="ContextMenuStrip"/> with <see cref="ContextMenuStrip.ShowSearchBox"/>
    /// on, which is the whole point of building type-to-filter into the menu engine rather than into
    /// this grid: a column of four values needs no search box and a column of four hundred is
    /// unusable without one, and the same menu serves both.
    /// </remarks>
    public bool AllowUserToFilterColumns { get; set; }

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
            var clamped = value < -1 || value >= this.RowSourceCount ? -1 : value;
            if (clamped != _selectedRowIndex && !this.ValidateRowChange())
                return;

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
        get => _selectedRowIndex >= 0 && _selectedRowIndex < this.RowSourceCount ? this.GetRowItem(_selectedRowIndex) : null;
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
                    if (multi[i] < this.RowSourceCount)
                        yield return this.GetRowItem(multi[i]);

                yield break;
            }

            if (_selectedRowIndex >= 0 && _selectedRowIndex < this.RowSourceCount)
                yield return this.GetRowItem(_selectedRowIndex);
        }
    }

    /// <summary>The column index keyboard activation (Space/Enter) targets; follows the last clicked
    /// cell.</summary>
    public int CurrentColumnIndex
    {
        get => _currentColumnIndex;
        set => _currentColumnIndex = Math.Max(0, value);
    }

    /// <summary>The display index of the first visible data row (vertical scroll position).
    /// Assigning scrolls there, clamped to the scrollable range — the same state the vertical
    /// scrollbar's thumb reads and writes.</summary>
    public int TopRow
    {
        get => _topRow;
        set
        {
            _topRow = Math.Max(0, value);
            this.ClampScroll();
            this.Invalidate();
            this.SyncEditorToScroll();
        }
    }

    /// <summary>How cells enter edit mode. Defaults to
    /// <see cref="DataGridViewEditMode.EditOnKeystrokeOrF2"/>.</summary>
    public DataGridViewEditMode EditMode { get; set; }

    /// <summary>Whether resting the pointer on a cell whose column has a
    /// <see cref="DataGridViewColumn.TooltipSelector"/> pops that text up near the cursor. Defaults
    /// to <see langword="true"/>.</summary>
    public bool ShowCellToolTips { get; set; } = true;

    /// <summary>Whether the hosted editor's content has changed since the edit began — cleared when
    /// the edit ends. Popup-based kinds commit through their pick gesture and never report dirty.</summary>
    public bool IsCurrentCellDirty => _editDirty;

    /// <summary>Whether the vertical scrollbar strip is currently shown (the rows overflow the
    /// viewport).</summary>
    public bool IsVerticalScrollBarVisible
    {
        get
        {
            this.UpdateScrollBarVisibility();
            return _verticalScrollBarVisible;
        }
    }

    /// <summary>Whether the horizontal scrollbar strip is currently shown (the columns overflow the
    /// viewport).</summary>
    public bool IsHorizontalScrollBarVisible
    {
        get
        {
            this.UpdateScrollBarVisibility();
            return _horizontalScrollBarVisible;
        }
    }

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

    /// <summary>
    /// Raised before an item's tick flips inside the popup of a
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> cell — the grid-side sibling of
    /// <see cref="CheckedListBox.ItemCheck"/>, with the same veto shape: a handler resets
    /// <see cref="ItemCheckEventArgs.NewValue"/> to <see cref="ItemCheckEventArgs.CurrentValue"/> to
    /// keep the tick as it was. <see cref="ItemCheckEventArgs.Index"/> indexes the popup's item list;
    /// the cell it belongs to is the one <see cref="SelectedRowIndex"/>/<see cref="CurrentColumnIndex"/>
    /// report while the popup is open. Only the popup's own ticks raise it — the whole set still commits through
    /// <see cref="DataGridViewColumn.CheckedItemsSetter"/> afterwards.
    /// </summary>
    public event EventHandler<ItemCheckEventArgs>? CellItemCheck;

    /// <summary>Raised when <see cref="IsCurrentCellDirty"/> flips — on the first editor change after
    /// the edit begins, and again when the edit ends.</summary>
    public event EventHandler? CurrentCellDirtyStateChanged;

    /// <summary>Raised before the current row is left for another one, carrying the row being left;
    /// setting <see cref="DataGridViewCellCancelEventArgs.Cancel"/> keeps the selection where it is.</summary>
    public event EventHandler<DataGridViewCellCancelEventArgs>? RowValidating;

    /// <summary>Raised after the current row was left without a <see cref="RowValidating"/> veto.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? RowValidated;

    /// <summary>Raised after <see cref="Paste"/> processed clipboard text — every attempted cell
    /// already ran its own <see cref="CellValidating"/>.</summary>
    public event EventHandler? PasteCompleted;

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

    /// <summary>The grid claims Enter (activate/commit) always, plus Escape (cancel) and
    /// Tab/Shift+Tab (in-grid cell navigation) while a cell edit runs.</summary>
    protected override bool IsInputKey(Keys keyData)
        => keyData == Keys.Enter
           || (this.IsEditing && keyData is Keys.Escape or Keys.Tab or (Keys.Tab | Keys.Shift));

    /// <summary>The pixel height of the column-header row, or 0 when hidden.</summary>
    protected int HeaderHeight => this.ShowColumnHeaders ? this.ColumnHeaderHeight : 0;

    /// <summary>The number of fully visible data rows, assuming the default <see cref="RowHeight"/>
    /// and accounting for the horizontal scrollbar strip when it is shown.</summary>
    protected int VisibleRowCount
    {
        get
        {
            this.UpdateScrollBarVisibility();
            var height = this.Height - this.HeaderHeight - (_horizontalScrollBarVisible ? this.Theme.ScrollBarSize : 0);
            return Math.Max(1, height / this.RowHeight);
        }
    }

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
    /// only the non-frozen columns scroll, within the viewport right of the frozen run (narrowed by
    /// the vertical scrollbar strip when it is shown).</summary>
    private int MaxHorizontalOffset
    {
        get
        {
            this.UpdateScrollBarVisibility();
            var frozenWidth = this.FrozenWidth;
            var viewport = this.Width - this.ContentLeft - frozenWidth - (_verticalScrollBarVisible ? this.Theme.ScrollBarSize : 0);
            return Math.Max(0, this.TotalColumnWidth - frozenWidth - Math.Max(0, viewport));
        }
    }

    /// <summary>Whether a cell is currently in edit mode.</summary>
    public bool IsEditing => _editRowIndex >= 0;

    /// <summary>The hosted editor control while a <see cref="DataGridViewColumnKind.Text"/>,
    /// <see cref="DataGridViewColumnKind.MaskedText"/>, <see cref="DataGridViewColumnKind.NumericUpDown"/>
    /// or <see cref="DataGridViewColumnKind.DomainUpDown"/> cell is in edit mode, or
    /// <see langword="null"/> (popup- and dialog-based kinds host no child control).</summary>
    public Control? EditingControl => _textEditor is not null ? _textEditor
        : _numericEditor is not null ? _numericEditor
        : _timeEditor is not null ? _timeEditor
        : _domainEditor;

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged(EventArgs e) => this.SelectionChanged?.Invoke(this, e);

    /// <summary>
    /// Runs the row-validation pair for leaving the current row: <see cref="RowValidating"/> first —
    /// a veto returns <see langword="false"/> and the caller keeps the selection — then
    /// <see cref="RowValidated"/>. Trivially <see langword="true"/> without a current row, and
    /// allocation-free while nobody listens.
    /// </summary>
    private bool ValidateRowChange()
    {
        var current = _selectedRowIndex;
        if (current < 0)
            return true;

        var columnIndex = _columns.Count == 0 ? -1 : Math.Min(_currentColumnIndex, _columns.Count - 1);
        if (this.RowValidating is not null)
        {
            var e = new DataGridViewCellCancelEventArgs(current, columnIndex);
            this.OnRowValidating(e);
            if (e.Cancel)
                return false;
        }

        if (this.RowValidated is not null)
            this.OnRowValidated(new(current, columnIndex));

        return true;
    }

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

    /// <summary>Raises <see cref="CellItemCheck"/>.</summary>
    protected virtual void OnCellItemCheck(ItemCheckEventArgs e) => this.CellItemCheck?.Invoke(this, e);

    /// <summary>Raises <see cref="CurrentCellDirtyStateChanged"/>.</summary>
    protected virtual void OnCurrentCellDirtyStateChanged(EventArgs e) => this.CurrentCellDirtyStateChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="RowValidating"/>.</summary>
    protected virtual void OnRowValidating(DataGridViewCellCancelEventArgs e) => this.RowValidating?.Invoke(this, e);

    /// <summary>Raises <see cref="RowValidated"/>.</summary>
    protected virtual void OnRowValidated(DataGridViewCellEventArgs e) => this.RowValidated?.Invoke(this, e);

    /// <summary>Raises <see cref="PasteCompleted"/>.</summary>
    protected virtual void OnPasteCompleted(EventArgs e) => this.PasteCompleted?.Invoke(this, e);

    /// <summary>
    /// Whether the given cell refuses edits and check toggling: read-only at any level (grid, column,
    /// or the column's per-cell predicate) makes the cell read-only, matching WinForms semantics.
    /// </summary>
    public bool IsCellReadOnly(object? rowItem, DataGridViewColumn column)
        => this.ReadOnly || column.ReadOnly || (column.ReadOnlyCellSelector?.Invoke(rowItem) ?? false);

    /// <summary>The tooltip text the column's <see cref="DataGridViewColumn.TooltipSelector"/> yields
    /// for the given cell, or <see langword="null"/>. Indices are model (Items/Columns) indices.</summary>
    public string? GetCellTooltip(int rowIndex, int columnIndex) => this.GetCellTooltip(rowIndex, columnIndex, -1);

    /// <summary>The tooltip text for a cell, preferring the column's
    /// <see cref="DataGridViewColumn.ImageTooltipSelector"/> when <paramref name="imageIndex"/> names an
    /// icon of a <see cref="DataGridViewColumnKind.MultiImage"/> cell, and falling back to the
    /// cell-wide <see cref="DataGridViewColumn.TooltipSelector"/>. Indices are model indices.</summary>
    public string? GetCellTooltip(int rowIndex, int columnIndex, int imageIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.RowSourceCount || columnIndex < 0 || columnIndex >= _columns.Count)
            return null;

        var column = _columns[columnIndex];
        var item = this.GetRowItem(rowIndex);
        if (imageIndex >= 0 && column.ImageTooltipSelector is { } perImage && perImage(item, imageIndex) is { } text)
            return text;

        return column.TooltipSelector?.Invoke(item);
    }

    /// <summary>The index of the icon under a point inside a <see cref="DataGridViewColumnKind.MultiImage"/>
    /// cell, or <c>-1</c>. Mirrors the per-icon hit-test used for clicks.</summary>
    private int HitTestCellImage(int rowIndex, int columnIndex, int cellX, int rowHeight)
    {
        if (rowIndex < 0 || rowIndex >= this.RowSourceCount || columnIndex < 0 || columnIndex >= _columns.Count)
            return -1;

        var column = _columns[columnIndex];
        if (column.Kind != DataGridViewColumnKind.MultiImage)
            return -1;

        var images = column.ImagesSelector?.Invoke(this.GetRowItem(rowIndex));
        var (iconSize, slot, _) = MultiImageMetrics(column, rowHeight);
        if (images is null || iconSize <= 0)
            return -1;

        var relative = cellX - _CellPadding;
        if (relative < 0)
            return -1;

        var index = relative / slot;
        return index < images.Count && (relative % slot) < iconSize ? index : -1;
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
        if (_selectedRowIndex >= this.RowSourceCount)
            _selectedRowIndex = this.RowSourceCount - 1;

        if (_multiSelection is { } multi)
            while (multi.Count > 0 && multi[^1] >= this.RowSourceCount)
                multi.RemoveAt(multi.Count - 1);

        if (_anchorRowIndex >= this.RowSourceCount)
            _anchorRowIndex = this.RowSourceCount - 1;

        if (this.IsEditing && _editRowIndex >= this.RowSourceCount)
            this.CancelEdit();

        _sortDirty = true;
        this.InvalidateDisplayText();
        this.ClampScroll();
        this.Invalidate();
        this.SyncEditorToScroll();
    }

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.RowSourceCount - this.VisibleRowCount);
        _topRow = Math.Clamp(_topRow, 0, maxTop);
    }

    /// <summary>Rebuilds the display→model sort map when a sort is active and the items changed.
    /// Kept closure-free so the steady-state calls (every repaint passes through here) allocate
    /// nothing — the rebuild itself lives in <see cref="RebuildSortMap"/>, entered only on a sort
    /// gesture or item mutation.</summary>
    private void EnsureSortMap()
    {
        var column = _sortedColumn;

        // Virtual mode sorts at the source: building a map here would have to fetch every row to compare
        // them, which is exactly what the mode exists to avoid.
        if (column is null || _sortOrder == SortOrder.None || this.VirtualMode)
        {
            _sortMap = null;
            return;
        }

        var count = this.RowSourceCount;
        var map = _sortMap;
        if (!_sortDirty && map is not null && map.Length == count)
            return;

        this.RebuildSortMap(column, count);
    }

    /// <summary>Sorts the display→model map. Lives apart from <see cref="EnsureSortMap"/> because the
    /// comparison closure's display class is allocated on scope entry, unconditionally — inlined into
    /// the ensure method it would cost 40 bytes per frame even while nothing sorts.</summary>
    private void RebuildSortMap(DataGridViewColumn column, int count)
    {
        var map = _sortMap;
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

    // --- Virtual mode ----------------------------------------------------------------------------

    private int _virtualDiscovered;
    private bool _virtualCountFinal;

    /// <summary>
    /// Whether rows are served on demand by <see cref="RetrieveVirtualRow"/> over a
    /// <see cref="VirtualRowCount"/> instead of from <see cref="Items"/>, so a million-row query never
    /// materialises. Sorting is left to the data source while virtual (the grid cannot compare rows it
    /// has not fetched), and <see cref="Items"/> is ignored. Defaults to <see langword="false"/>.
    /// </summary>
    public bool VirtualMode
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _virtualDiscovered = 0;
            _virtualCountFinal = false;
            _selectedRowIndex = -1;
            _sortMap = null;   // the grid cannot compare rows it has not fetched
            this.Invalidate();
        }
    }

    /// <summary>The row count exposed while <see cref="VirtualMode"/> is on, or <c>-1</c> for an unknown
    /// size: the grid then probes past what it has confirmed, growing as you scroll until
    /// <see cref="RetrieveVirtualRow"/> reports the end.</summary>
    public int VirtualRowCount
    {
        get => field;
        set
        {
            value = value < 0 ? -1 : value;
            if (field == value)
                return;

            field = value;
            _virtualDiscovered = 0;
            _virtualCountFinal = false;
            if (this.VirtualMode)
            {
                _sortMap = null;
                this.Invalidate();
            }
        }
    }

    /// <summary>Raised while <see cref="VirtualMode"/> is on to fetch the row item at an index. Called
    /// once per visible row per paint — keep it cheap.</summary>
    public event EventHandler<RetrieveVirtualRowEventArgs>? RetrieveVirtualRow;

    /// <summary>Whether the grid is in the unknown-size virtual mode.</summary>
    private bool UnknownVirtualSize => this.VirtualMode && this.VirtualRowCount < 0;

    /// <summary>The number of rows the presentation draws from: <see cref="Items"/> normally, the fixed
    /// <see cref="VirtualRowCount"/> in known virtual mode, or a probe window past the confirmed rows.</summary>
    private int RowSourceCount
    {
        get
        {
            if (!this.VirtualMode)
                return this.Items.Count;

            if (!this.UnknownVirtualSize)
                return this.VirtualRowCount;

            if (_virtualCountFinal)
                return _virtualDiscovered;

            // Deliberately NOT VisibleRowCount: that settles the scroll bars, which read this count back
            // and would recurse. A plain row estimate is enough to size the probe window.
            var rows = Math.Max(1, (this.Height - this.HeaderHeight) / this.RowHeight);
            return Math.Max(_virtualDiscovered, _topRow + (2 * rows));
        }
    }

    /// <summary>The row item at a model index: fetched from <see cref="RetrieveVirtualRow"/> while
    /// virtual, otherwise the model item. Returns <see langword="false"/> when the row turns out not to
    /// exist, which the unknown-size mode only learns by asking.</summary>
    private bool TryGetRowItem(int modelIndex, out object? item)
    {
        item = null;
        if (!this.VirtualMode)
        {
            if (modelIndex < 0 || modelIndex >= this.Items.Count)
                return false;

            item = this.Items[modelIndex];
            return true;
        }

        if (modelIndex < 0)
            return false;

        var probe = new RetrieveVirtualRowEventArgs(modelIndex);
        this.RetrieveVirtualRow?.Invoke(this, probe);
        if (this.UnknownVirtualSize)
        {
            if (probe.EndOfRows || probe.Item is null)
            {
                _virtualCountFinal = true;
                _virtualDiscovered = modelIndex;
                return false;
            }

            if (modelIndex + 1 > _virtualDiscovered)
                _virtualDiscovered = modelIndex + 1;
        }

        item = probe.Item;
        return probe.Item is not null || !this.UnknownVirtualSize;
    }

    /// <summary>The row item at a model index, or <see langword="null"/> when it does not exist.</summary>
    private object? GetRowItem(int modelIndex)
    {
        this.TryGetRowItem(modelIndex, out var item);
        return item;
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

    private bool IsRowHidden(object? item)
        => (this.RowHiddenSelector?.Invoke(item) ?? false) || this.IsFilteredOut(item);

    /// <summary>Whether any column's filter rejects the text that column displays for this row.</summary>
    private bool IsFilteredOut(object? item)
    {
        for (var i = 0; i < _columns.Count; ++i)
        {
            var column = _columns[i];
            if (column.Filter is not { } accepted)
                continue;

            if (!accepted.Contains(ComputeDisplayText(column, item)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The distinct values a column displays, in first-seen order, as the filter menu offers them.
    /// Built from the rows the <em>other</em> columns' filters still admit, so narrowing one column
    /// does not empty the menus of the rest — the behaviour every spreadsheet has.
    /// </summary>
    public IReadOnlyList<string> GetFilterValues(DataGridViewColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var seen = new HashSet<string>(StringComparer.CurrentCulture);
        var values = new List<string>();
        var count = this.RowSourceCount;
        for (var i = 0; i < count; ++i)
        {
            if (!this.TryGetRowItem(i, out var item))
                break;

            if ((this.RowHiddenSelector?.Invoke(item) ?? false) || this.IsFilteredOutByOthers(item, column))
                continue;

            var text = ComputeDisplayText(column, item);
            if (seen.Add(text))
                values.Add(text);
        }

        return values;
    }

    /// <summary>Whether a row is rejected by some column's filter other than <paramref name="except"/>.</summary>
    private bool IsFilteredOutByOthers(object? item, DataGridViewColumn except)
    {
        for (var i = 0; i < _columns.Count; ++i)
        {
            var column = _columns[i];
            if (ReferenceEquals(column, except) || column.Filter is not { } accepted)
                continue;

            if (!accepted.Contains(ComputeDisplayText(column, item)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Applies a column's accepted values and re-lays the grid out. A set covering every value the
    /// column shows is stored as no filter at all, so the header glyph stops claiming one is active.
    /// </summary>
    public void SetColumnFilter(DataGridViewColumn column, IReadOnlyCollection<string>? accepted)
    {
        ArgumentNullException.ThrowIfNull(column);

        column.Filter = accepted;
        _sortDirty = true;
        this.ClampScroll();
        this.Invalidate();
        this.OnColumnFilterChanged(new(-1, _columns.IndexOf(column)));
    }

    /// <summary>Raised after a column's filter changed, whether from the menu or from code.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? ColumnFilterChanged;

    /// <summary>Raises <see cref="ColumnFilterChanged"/>.</summary>
    protected virtual void OnColumnFilterChanged(DataGridViewCellEventArgs e) => this.ColumnFilterChanged?.Invoke(this, e);

    /// <summary>
    /// Builds the filter menu for a column: a searchable list of its distinct values as check items,
    /// headed by an all-or-nothing toggle. Public so an application can open it from its own gesture
    /// — a keyboard shortcut, a toolbar button — rather than only from the header glyph.
    /// </summary>
    public ContextMenuStrip CreateColumnFilterMenu(DataGridViewColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var values = this.GetFilterValues(column);
        var accepted = column.Filter;
        var menu = new ContextMenuStrip { ShowSearchBox = true };

        var all = new ToolStripMenuItem(Strings.FilterAll) { Checked = accepted is null, CheckOnClick = true };
        all.Click += (_, _) => this.SetColumnFilter(column, all.Checked ? null : []);
        menu.Items.Add(all);
        menu.Items.Add(new ToolStripSeparator());

        foreach (var value in values)
        {
            var text = value;
            var item = new ToolStripMenuItem(text.Length > 0 ? text : Strings.FilterBlank)
            {
                Checked = accepted is null || accepted.Contains(text),
                CheckOnClick = true,
            };

            item.Click += (_, _) =>
            {
                // The click has already flipped the item, so the new state is what to apply.
                var next = new HashSet<string>(accepted ?? values, StringComparer.CurrentCulture);
                if (item.Checked)
                    next.Add(text);
                else
                    next.Remove(text);

                this.SetColumnFilter(column, next.Count == values.Count ? null : next);
            };

            menu.Items.Add(item);
        }

        return menu;
    }

    private bool IsRowSelectable(object? item) => this.RowSelectableSelector?.Invoke(item) ?? true;

    private string? MergedTextOf(object? item) => this.FullRowTextSelector?.Invoke(item);

    private bool IsRowNavigable(int modelIndex)
    {
        var item = this.GetRowItem(modelIndex);
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
    /// range from the anchor, and a plain click collapses the set to the clicked row. A gesture that
    /// moves the current row runs the same <see cref="RowValidating"/>/<see cref="RowValidated"/>
    /// pipeline as the <see cref="SelectedRowIndex"/> setter — a veto keeps the current row and the
    /// selection untouched.
    /// </summary>
    private void SelectRowWithModifiers(int modelIndex, KeyModifiers modifiers)
    {
        if (!this.MultiSelect)
        {
            this.SelectedRowIndex = modelIndex;
            return;
        }

        if (modelIndex != _selectedRowIndex && !this.ValidateRowChange())
            return;

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

    /// <summary>The text a cell displays, cached per model row in the column: selectors, boxing and
    /// formatting run once per changed cell, never per frame, so a steady-state repaint (and a
    /// scroll over already-shown rows) allocates nothing.</summary>
    private string GetDisplayText(DataGridViewColumn column, object? item, int modelIndex)
    {
        var count = this.RowSourceCount;
        var cache = column.DisplayTextCache;
        if (cache is null || cache.Length != count)
            column.DisplayTextCache = cache = count > 0 ? new string?[count] : [];

        return (uint)modelIndex < (uint)cache.Length
            ? cache[modelIndex] ??= ComputeDisplayText(column, item)
            : ComputeDisplayText(column, item);
    }

    /// <summary>Builds the text a cell displays: the display-text override, else — for a set-valued
    /// cell — the joined summary of its items, else the value formatted by
    /// <see cref="DataGridViewColumn.FormatSelector"/> (the CellFormatting seam), else the value's
    /// <c>ToString()</c>.</summary>
    private static string ComputeDisplayText(DataGridViewColumn column, object? item)
    {
        if (column.DisplayTextSelector?.Invoke(item) is { } overridden)
            return overridden;

        if (IsSetValued(column))
            return SetSummaryText(column, column.CheckedItemsSelector?.Invoke(item));

        var value = column.ValueSelector(item);
        if (column.Kind == DataGridViewColumnKind.ListBox && column.ItemDisplaySelector is { } display)
            return display(value);

        return (column.FormatSelector is { } format ? format(value) : value?.ToString()) ?? string.Empty;
    }

    /// <summary>Whether the column's cells hold a whole set of items rather than one value: a
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> cell always, a
    /// <see cref="DataGridViewColumnKind.ListBox"/> cell once its
    /// <see cref="DataGridViewColumn.SelectionMode"/> admits more than one pick.</summary>
    private static bool IsSetValued(DataGridViewColumn column) => column.Kind switch
    {
        DataGridViewColumnKind.CheckedListBox => true,
        DataGridViewColumnKind.ListBox => column.SelectionMode is SelectionMode.MultiSimple or SelectionMode.MultiExtended,
        _ => false,
    };

    /// <summary>The closed-cell text of a set-valued cell: the items' display texts joined with
    /// <see cref="_SetSummarySeparator"/>, empty for an empty set. Runs only when the cell's cached
    /// text is missing, never per frame.</summary>
    private static string SetSummaryText(DataGridViewColumn column, IReadOnlyList<object?>? items)
    {
        if (items is null || items.Count == 0)
            return string.Empty;

        if (items.Count == 1)
            return ChoiceDisplayText(column, items[0]);

        var builder = new StringBuilder();
        for (var i = 0; i < items.Count; ++i)
        {
            if (i > 0)
                builder.Append(_SetSummarySeparator);

            builder.Append(ChoiceDisplayText(column, items[i]));
        }

        return builder.ToString();
    }

    /// <summary>Drops every column's cached display text — the row set itself changed.</summary>
    private void InvalidateDisplayText()
    {
        var columns = _columns;
        for (var i = 0; i < columns.Count; ++i)
            columns[i].DisplayTextCache = null;
    }

    /// <summary>Drops one row's cached display text in every column — a cell write mutated the row
    /// item, so any cell derived from it must re-format on the next repaint.</summary>
    private void InvalidateDisplayText(int modelIndex)
    {
        var columns = _columns;
        for (var i = 0; i < columns.Count; ++i)
            if (columns[i].DisplayTextCache is { } cache && (uint)modelIndex < (uint)cache.Length)
                cache[modelIndex] = null;
    }

    /// <summary>The text an editor seeds from: the raw <see cref="DataGridViewColumn.ValueSelector"/>
    /// value — formatting is display-only.</summary>
    private static string GetEditText(DataGridViewColumn column, object? item)
        => column.ValueSelector(item)?.ToString() ?? string.Empty;

    /// <summary>Finds the data row at the given y-coordinate by walking the visible window (skipping
    /// hidden rows, honoring per-row heights). Returns the model index, or -1.</summary>
    private int HitTestRow(int y, out int rowTop, out int rowHeight)
    {
        this.EnsureSortMap();
        rowTop = 0;
        rowHeight = 0;

        var count = this.RowSourceCount;
        var height = this.Height;
        var currentY = this.HeaderHeight;
        var display = Math.Max(0, _topRow);
        while (currentY < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            var item = this.GetRowItem(modelIndex);
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

        this.ApplyFillWidths();
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

    // --- Scrollbars --------------------------------------------------------------------------------

    /// <summary>
    /// Recomputes which scrollbar strips are needed: rows against the viewport height, columns
    /// against the viewport width, each retried once with the other bar's strip subtracted — the
    /// classic two-pass resolution. The row extent is approximated from the default
    /// <see cref="RowHeight"/>, like the rest of the scroll range under per-row heights.
    /// </summary>
    private void UpdateScrollBarVisibility()
    {
        var size = this.Theme.ScrollBarSize;
        var viewportWidth = this.Width - this.ContentLeft;
        var viewportHeight = this.Height - this.HeaderHeight;
        var contentWidth = this.TotalColumnWidth;
        var contentHeight = this.RowSourceCount * this.RowHeight;
        var vertical = contentHeight > viewportHeight;
        var horizontal = contentWidth > viewportWidth - (vertical ? size : 0);
        if (horizontal)
            vertical = contentHeight > viewportHeight - size;

        _verticalScrollBarVisible = vertical;
        _horizontalScrollBarVisible = horizontal;
    }

    /// <summary>The strip the vertical scrollbar occupies: the right edge below the header, stopping
    /// above the horizontal strip when both are shown.</summary>
    private Rectangle VerticalScrollBarBounds
    {
        get
        {
            var size = this.Theme.ScrollBarSize;
            var height = this.Height - this.HeaderHeight - (_horizontalScrollBarVisible ? size : 0);
            return new(this.Width - size, this.HeaderHeight, size, Math.Max(0, height));
        }
    }

    /// <summary>The strip the horizontal scrollbar occupies: the bottom edge, stopping left of the
    /// vertical strip when both are shown.</summary>
    private Rectangle HorizontalScrollBarBounds
    {
        get
        {
            var size = this.Theme.ScrollBarSize;
            var width = this.Width - (_verticalScrollBarVisible ? size : 0);
            return new(0, this.Height - size, Math.Max(0, width), size);
        }
    }

    /// <summary>The vertical scroll range in display rows: <see cref="TopRow"/> travels it with the
    /// visible page as the thumb's share.</summary>
    private void GetVerticalScrollRange(out int maximum, out int largeChange)
    {
        maximum = Math.Max(0, this.RowSourceCount - 1);
        largeChange = this.VisibleRowCount;
    }

    /// <summary>The horizontal scroll range in pixels over the scrolling (non-frozen) columns:
    /// <see cref="HorizontalOffset"/> travels it with the scrolling viewport as the thumb's share.</summary>
    private void GetHorizontalScrollRange(out int maximum, out int largeChange)
    {
        var scrollable = this.TotalColumnWidth - this.FrozenWidth;
        maximum = Math.Max(0, scrollable - 1);
        largeChange = Math.Max(1, scrollable - this.MaxHorizontalOffset);
    }

    /// <summary>
    /// Paints the scrollbar strips (and the corner square between them) when the content overflows —
    /// the same renderer as the standalone <see cref="ScrollBar"/>, driven directly by
    /// <see cref="TopRow"/> and <see cref="HorizontalOffset"/> so the thumbs can never drift from the
    /// scroll state.
    /// </summary>
    private void PaintScrollBars(IGraphics g, ITheme theme)
    {
        this.UpdateScrollBarVisibility();
        if (_verticalScrollBarVisible)
        {
            this.GetVerticalScrollRange(out var maximum, out var largeChange);
            ScrollBarRenderer.Paint(g, theme, this.VerticalScrollBarBounds, vertical: true, 0, maximum, _topRow, largeChange,
                _scrollDragging && _scrollDragVertical ? ScrollBarPart.Thumb : ScrollBarPart.None);
        }

        if (_horizontalScrollBarVisible)
        {
            this.GetHorizontalScrollRange(out var maximum, out var largeChange);
            ScrollBarRenderer.Paint(g, theme, this.HorizontalScrollBarBounds, vertical: false, 0, maximum, this.HorizontalOffset, largeChange,
                _scrollDragging && !_scrollDragVertical ? ScrollBarPart.Thumb : ScrollBarPart.None);
        }

        if (_verticalScrollBarVisible && _horizontalScrollBarVisible)
        {
            var size = theme.ScrollBarSize;
            g.FillRectangle(theme.ControlBackground, new Rectangle(this.Width - size, this.Height - size, size, size));
        }
    }

    /// <summary>
    /// Routes a press inside a scrollbar strip: arrows step one row / one horizontal notch, the
    /// channel pages, the thumb arms a drag. Returns whether the press was consumed by a strip.
    /// </summary>
    private bool HandleScrollBarMouseDown(Point location)
    {
        this.UpdateScrollBarVisibility();
        if (_verticalScrollBarVisible && this.VerticalScrollBarBounds.Contains(location))
        {
            var bounds = this.VerticalScrollBarBounds;
            this.GetVerticalScrollRange(out var maximum, out var largeChange);
            switch (ScrollBarRenderer.HitTest(bounds, true, 0, maximum, _topRow, largeChange, location))
            {
                case ScrollBarPart.DecreaseArrow: this.ScrollRows(-1); break;
                case ScrollBarPart.IncreaseArrow: this.ScrollRows(1); break;
                case ScrollBarPart.DecreaseChannel: this.ScrollRows(-largeChange); break;
                case ScrollBarPart.IncreaseChannel: this.ScrollRows(largeChange); break;
                case ScrollBarPart.Thumb:
                {
                    var thumb = ScrollBarRenderer.ThumbRect(bounds, true, 0, maximum, _topRow, largeChange);
                    _scrollDragging = true;
                    _scrollDragVertical = true;
                    _scrollDragOffset = location.Y - thumb.Y;
                    this.Invalidate();
                    break;
                }
            }

            return true;
        }

        if (_horizontalScrollBarVisible && this.HorizontalScrollBarBounds.Contains(location))
        {
            var bounds = this.HorizontalScrollBarBounds;
            this.GetHorizontalScrollRange(out var maximum, out var largeChange);
            switch (ScrollBarRenderer.HitTest(bounds, false, 0, maximum, this.HorizontalOffset, largeChange, location))
            {
                case ScrollBarPart.DecreaseArrow: this.HorizontalOffset -= _WheelHorizontalStep; break;
                case ScrollBarPart.IncreaseArrow: this.HorizontalOffset += _WheelHorizontalStep; break;
                case ScrollBarPart.DecreaseChannel: this.HorizontalOffset -= largeChange; break;
                case ScrollBarPart.IncreaseChannel: this.HorizontalOffset += largeChange; break;
                case ScrollBarPart.Thumb:
                {
                    var thumb = ScrollBarRenderer.ThumbRect(bounds, false, 0, maximum, this.HorizontalOffset, largeChange);
                    _scrollDragging = true;
                    _scrollDragVertical = false;
                    _scrollDragOffset = location.X - thumb.X;
                    this.Invalidate();
                    break;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>Scrubs the dragged thumb to the pointer, mapping the pixel offset back onto
    /// <see cref="TopRow"/> or <see cref="HorizontalOffset"/>.</summary>
    private void HandleScrollBarDrag(MouseEventArgs e)
    {
        if (_scrollDragVertical)
        {
            var bounds = this.VerticalScrollBarBounds;
            this.GetVerticalScrollRange(out var maximum, out var largeChange);
            var track = ScrollBarRenderer.TrackRect(bounds, true);
            var offset = e.Y - _scrollDragOffset - track.Y;
            this.TopRow = ScrollBarRenderer.ValueFromThumbOffset(bounds, true, 0, maximum, largeChange, offset);
            return;
        }

        var hBounds = this.HorizontalScrollBarBounds;
        this.GetHorizontalScrollRange(out var hMaximum, out var hLargeChange);
        var hTrack = ScrollBarRenderer.TrackRect(hBounds, false);
        var hOffset = e.X - _scrollDragOffset - hTrack.X;
        this.HorizontalOffset = ScrollBarRenderer.ValueFromThumbOffset(hBounds, false, 0, hMaximum, hLargeChange, hOffset);
    }

    /// <summary>Scrolls the top row by the given number of display rows, skipping hidden rows —
    /// the shared tail of the wheel, the scrollbar arrows and the channel pages.</summary>
    private void ScrollRows(int delta)
    {
        this.EnsureSortMap();
        _topRow = this.StepDisplayRow(_topRow, Math.Sign(delta), Math.Abs(delta));
        this.ClampScroll();
        this.Invalidate();
        this.SyncEditorToScroll();
    }

    /// <summary>Steps a display row index by up to <paramref name="steps"/> non-hidden rows.</summary>
    private int StepDisplayRow(int from, int direction, int steps)
    {
        if (direction == 0)
            return from;

        var count = this.RowSourceCount;
        var display = from;
        while (steps-- > 0)
        {
            var next = display + direction;
            while (next >= 0 && next < count && this.IsRowHidden(this.GetRowItem(this.ToModelIndex(next))))
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
        var count = this.RowSourceCount;
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
            // The extending move changes the current row, so it validates like every other leave.
            if (modelIndex != _selectedRowIndex && !this.ValidateRowChange())
                return;

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
        var count = this.RowSourceCount;
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
        this.HideCellToolTip();
        if (e.Button != MouseButtons.Left)
            return;

        // A press inside a scrollbar strip scrolls without touching selection — or the active edit,
        // whose scroll-out commit the strips share with the wheel.
        this.ApplyFillWidths();
        if (this.HandleScrollBarMouseDown(e.Location))
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

        // Armed before the press selects anything, so Ctrl comes out right: the press toggles the row
        // under the pointer, the band covers that same row, and the two must not cancel out.
        this.BeginMarquee(e);
        if (rowIndex < 0)
            return;

        var item = this.GetRowItem(rowIndex);
        if (this.MergedTextOf(item) is not null)
            return; // merged rows have no cells and take no selection

        if (this.IsRowSelectable(item))
            this.SelectRowWithModifiers(rowIndex, e.Modifiers);

        var columnIndex = this.HitTestColumn(e.X, out var cellLeft);
        if (columnIndex < 0)
            return;

        _currentColumnIndex = columnIndex;
        this.HandleCellMouseDown(rowIndex, columnIndex, item, e.X - cellLeft, rowHeight);
        if (this.EditMode == DataGridViewEditMode.EditOnEnter)
            this.BeginEdit(rowIndex, columnIndex); // the cell became current, so it edits
    }

    private void HandleHeaderMouseDown(int x)
    {
        var divider = this.HitTestColumnDivider(x);
        if (divider >= 0 && this.CanUserResizeColumn(_columns[divider]))
        {
            _resizeColumnIndex = divider;
            _resizeStartX = x;
            _resizeStartWidth = _columns[divider].Width;
            return;
        }

        var columnIndex = this.HitTestColumn(x, out var columnLeft);
        if (columnIndex < 0)
            return;

        // The funnel owns its corner of the header: a press there opens the filter rather than
        // sorting, which is what every grid that carries one does.
        if (this.AllowUserToFilterColumns && x >= columnLeft + _columns[columnIndex].Width - _FilterZoneWidth)
        {
            this.OpenColumnFilterMenu(columnIndex, columnLeft);
            return;
        }

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

    /// <summary>The width of the header zone the filter funnel claims from the sort/reorder gesture.</summary>
    private const int _FilterZoneWidth = 16;

    /// <summary>The width the sort arrow occupies at a header's trailing edge.</summary>
    private const int _SortArrowZoneWidth = 14;

    /// <summary>The funnel's own width inside that zone.</summary>
    private const int _FilterGlyphWidth = 10;

    /// <summary>The filter menu currently open, kept alive for as long as it is showing.</summary>
    private ContextMenuStrip? _filterMenu;

    /// <summary>Opens a column's filter menu under its header cell.</summary>
    private void OpenColumnFilterMenu(int columnIndex, int columnLeft)
    {
        _filterMenu?.Dispose();
        _filterMenu = this.CreateColumnFilterMenu(_columns[columnIndex]);
        _filterMenu.Show(this, new(columnLeft, this.HeaderHeight));
    }

    /// <summary>Whether the user may drag this column's divider: the column's tri-state
    /// <see cref="DataGridViewColumn.Resizable"/> wins over the grid's
    /// <see cref="AllowUserToResizeColumns"/> default, and auto-sized columns never resize by hand —
    /// their width is the auto-size policy's to give.</summary>
    private bool CanUserResizeColumn(DataGridViewColumn column)
        => column.AutoSizeMode == DataGridViewAutoSizeColumnMode.None
            && column.Resizable switch
            {
                DataGridViewTriState.True => true,
                DataGridViewTriState.False => false,
                _ => this.AllowUserToResizeColumns,
            };

    private void HandleCellMouseDown(int rowIndex, int columnIndex, object? item, int cellX, int rowHeight)
    {
        var now = Environment.TickCount64;
        var isDouble = rowIndex == _lastClickRowIndex
            && columnIndex == _lastClickColumnIndex
            && now - _lastClickTime <= this.Theme.DoubleClickTime;
        _lastClickRowIndex = rowIndex;
        _lastClickColumnIndex = columnIndex;
        _lastClickTime = isDouble ? 0 : now; // reset so a triple click is not two doubles

        this.OnCellClick(new(rowIndex, columnIndex));
        if (isDouble)
        {
            this.OnCellDoubleClick(new(rowIndex, columnIndex));
            if (this.EditMode != DataGridViewEditMode.EditProgrammatically)
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
                this.InvalidateDisplayText(rowIndex);
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
                var (iconSize, slot, _) = MultiImageMetrics(column, rowHeight);
                if (images is null || iconSize <= 0)
                    break;

                var relative = cellX - _CellPadding;
                if (relative < 0)
                    break;

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
        if (_scrollDragging)
        {
            this.HandleScrollBarDrag(e);
            return;
        }

        if (_resizeColumnIndex >= 0 && _resizeColumnIndex < _columns.Count)
        {
            var column = _columns[_resizeColumnIndex];
            var width = Math.Max(Math.Max(_MinColumnWidth, column.MinimumWidth), _resizeStartWidth + (e.X - _resizeStartX));
            if (width == column.Width)
                return;

            column.Width = width;
            this.Invalidate();
            return;
        }

        if (_dragColumnIndex >= 0 && _dragColumnIndex < _columns.Count)
        {
            var target = this.HitTestColumn(e.X, out _);
            if (target < 0 || target == _dragColumnIndex)
                return;

            if (_columns[target].Frozen != _columns[_dragColumnIndex].Frozen)
                return; // a drag never crosses the frozen boundary

            this.MoveColumnToDisplayPositionOf(_dragColumnIndex, target);
            this.Invalidate();
            return;
        }

        if (this.DragMarquee(e))
            return;

        this.TrackHoverCell(e);
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
        if (_scrollDragging)
        {
            _scrollDragging = false;
            this.Invalidate();
        }

        _resizeColumnIndex = -1;
        _dragColumnIndex = -1;
        this.EndMarquee();
    }

    // --- Rubber-band selection -------------------------------------------------------------------

    /// <summary>
    /// Arms a rubber band at the press point, snapshotting the selection before the press changes it.
    /// Nothing moves until the pointer passes <see cref="MarqueeDrag.Threshold"/>, so a plain click
    /// still behaves as a plain click.
    /// </summary>
    private void BeginMarquee(MouseEventArgs e)
    {
        if (!this.MultiSelect)
            return;

        var combine = (e.Modifiers & KeyModifiers.Control) != 0
            ? MarqueeCombine.Toggle
            : (e.Modifiers & KeyModifiers.Shift) != 0
                ? MarqueeCombine.Add
                : MarqueeCombine.Replace;

        _marquee?.Dispose();
        _marquee = new(new(e.X, e.Y), combine, _multiSelection is { } multi ? [.. multi] : []);
    }

    /// <summary>Grows the band to the pointer and re-derives the selection, reporting whether it owns
    /// the move.</summary>
    private bool DragMarquee(MouseEventArgs e)
    {
        var drag = _marquee;
        if (drag is null)
            return false;

        if (!drag.MoveTo(new(e.X, e.Y)))
            return false;

        this.ApplyMarquee();
        drag.AutoScroll(this.Backend, this.OnMarqueeScroll, e.Y < this.HeaderHeight || e.Y >= this.Height);
        this.Invalidate();
        return true;
    }

    /// <summary>Ends the gesture, keeping whatever the band last selected.</summary>
    private void EndMarquee()
    {
        var drag = _marquee;
        if (drag is null)
            return;

        _marquee = null;
        var swept = drag.Active;
        drag.Dispose();

        if (swept)
            this.Invalidate();
    }

    /// <summary>Scrolls one row while the pointer sits outside the viewport, and re-sweeps the band.</summary>
    private void OnMarqueeScroll(object? sender, EventArgs e)
    {
        var drag = _marquee;
        if (drag is null)
            return;

        this.ScrollRows(drag.Current.Y < this.HeaderHeight ? -1 : 1);
        this.ApplyMarquee();
    }

    /// <summary>Replaces the multi-selection with the one the band implies, reporting it once per move.</summary>
    private void ApplyMarquee()
    {
        var drag = _marquee;
        if (drag is null || !drag.Active)
            return;

        var multi = _multiSelection ??= [];
        this.CollectBandRows(drag.Band, drag.Covered);
        drag.BuildDesired(this.IsRowNavigable);
        if (drag.Matches(multi))
            return;

        multi.Clear();
        multi.AddRange(drag.Desired);

        // The current row follows the edge the pointer is dragging, as it would if the same rows had
        // been walked with Shift held on the keyboard.
        var current = multi.Count > 0 ? (drag.Current.Y >= drag.Origin.Y ? multi[^1] : multi[0]) : _selectedRowIndex;
        _selectedRowIndex = current;
        this.Invalidate();
        this.OnSelectionChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Collects the rows the band crosses, walking the visible display rows once. A grid row spans the
    /// full width, so only the band's vertical extent can decide — a band drawn over one column still
    /// selects whole rows, which is what row selection means.
    /// </summary>
    private void CollectBandRows(Rectangle band, List<int> into)
    {
        into.Clear();
        this.EnsureSortMap();

        var count = this.RowSourceCount;
        var height = this.Height;
        var y = this.HeaderHeight;
        var display = Math.Max(0, _topRow);

        while (y < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;

            ++display;
            if (this.IsRowHidden(item))
                continue;

            var rowHeight = this.GetRowHeightFor(item);
            if (band.Y < y + rowHeight && band.Bottom > y && this.IsRowNavigable(modelIndex))
                into.Add(modelIndex);

            y += rowHeight;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverRowIndex = -1;
        _hoverColumnIndex = -1;
        this.HideCellToolTip();
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        this.HideCellToolTip();
        if ((e.Modifiers & KeyModifiers.Shift) != 0)
        {
            this.HorizontalOffset = this.HorizontalOffset - (Math.Sign(e.Delta) * _WheelHorizontalStep);
            return;
        }

        this.ScrollRows(-Math.Sign(e.Delta) * _WheelRows);
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
            case Keys.Home when this.RowSourceCount > 0: this.SelectEdge(first: true); break;
            case Keys.End: this.SelectEdge(first: false); break;
            case Keys.PageDown: this.MoveSelection(this.VisibleRowCount, e.Shift); break;
            case Keys.PageUp: this.MoveSelection(-this.VisibleRowCount, e.Shift); break;
            case Keys.F2 when _selectedRowIndex >= 0 && _columns.Count > 0
                && this.EditMode != DataGridViewEditMode.EditProgrammatically:
                this.BeginEdit(_selectedRowIndex, Math.Min(_currentColumnIndex, _columns.Count - 1));
                break;
            case Keys.C when e.Control:
            {
                var content = this.GetClipboardContent();
                if (content.Length > 0)
                    this.Backend?.SetClipboardText(content);
                break;
            }

            case Keys.V when e.Control:
            {
                if (this.Backend?.GetClipboardText() is { Length: > 0 } text)
                    this.Paste(text);
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

        if (_selectedRowIndex < 0 || _columns.Count == 0 || this.EditMode == DataGridViewEditMode.EditProgrammatically)
            return;

        var columnIndex = Math.Min(_currentColumnIndex, _columns.Count - 1);
        var kind = _columns[columnIndex].Kind;
        if (kind is not (DataGridViewColumnKind.Text or DataGridViewColumnKind.NumericUpDown or DataGridViewColumnKind.MaskedText))
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
        this.ApplyFillWidths();

        var header = this.HeaderHeight;
        var contentLeft = this.ContentLeft;
        var frozenWidth = this.FrozenWidth;
        var scrollEdge = contentLeft + frozenWidth;
        var showGridLines = this.ShowGridLines;
        var count = this.RowSourceCount;

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
            // The unknown-size virtual source only reveals the end when asked, so a row the loop
            // already scheduled can turn out not to exist.
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;
            var displayIndex = display;
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var rowHeight = this.GetRowHeightFor(item);
            var selected = this.IsRowSelected(modelIndex);
            if (selected)
                GlyphRenderer.FillSelection(g, theme, new Rectangle(0, y, width, rowHeight));
            else if (this.RowBackColorSelector?.Invoke(item) is { } rowBack)
                g.FillRectangle(rowBack, new Rectangle(0, y, width, rowHeight));
            else if (this.AlternatingRows && (displayIndex & 1) == 1)
                g.FillRectangle(this.AlternatingRowColor, new Rectangle(0, y, width, rowHeight));

            if (this.MergedTextOf(item) is { } mergedText)
                g.DrawText(mergedText, theme.DefaultFont, theme.ControlText,
                    new Rectangle(contentLeft + _CellPadding, y, Math.Max(0, width - contentLeft - _CellPadding), rowHeight), ContentAlignment.MiddleLeft);
            else if (frozenWidth == 0)
                this.PaintRowCells(g, theme, item, modelIndex, y, rowHeight, selected, frozen: false);

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

        if (_marquee is { Active: true } marquee)
            GlyphRenderer.DrawSelectionBand(g, theme, marquee.Band);

        g.PopClip();

        if (this.ShowRowHeaders)
            this.PaintRowHeaders(g, theme, header, height, count);

        this.PaintScrollBars(g, theme);
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
                // The funnel takes the trailing corner and the sort arrow sits inboard of it, so the
                // two never share pixels and the caption is told what both cost.
                var filterReserve = this.AllowUserToFilterColumns ? _FilterZoneWidth : 0;
                var sorted = ReferenceEquals(column, _sortedColumn) && _sortOrder != SortOrder.None;
                var sortReserve = sorted ? _SortArrowZoneWidth : 0;

                GlyphRenderer.DrawHeaderCell(
                    g,
                    theme,
                    new Rectangle(x, 0, column.Width, header),
                    column.HeaderText,
                    column.Alignment,
                    _CellPadding,
                    separator: false,
                    trailingReserve: filterReserve + sortReserve);

                if (sorted)
                    GlyphRenderer.DrawSortArrow(
                        g,
                        theme.HeaderText,
                        new Rectangle(x + column.Width - _SortArrowZoneWidth - filterReserve, 0, 10, header),
                        _sortOrder == SortOrder.Ascending);

                if (filterReserve > 0)
                    GlyphRenderer.DrawFilterFunnel(
                        g,
                        column.Filter is null ? theme.HeaderText : theme.Accent,
                        new Rectangle(x + column.Width - _FilterZoneWidth, 0, _FilterGlyphWidth, header),
                        active: column.Filter is not null);
            }

            x += column.Width;
        }
    }

    /// <summary>Paints the data cells of one row for one run (frozen or scrolling columns), walking
    /// the display order with the same geometry as <see cref="PaintHeaderCells"/>.</summary>
    private void PaintRowCells(IGraphics g, ITheme theme, object? item, int modelIndex, int y, int rowHeight, bool selected, bool frozen)
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
                this.PaintCell(g, theme, column, item, modelIndex, new Rectangle(x, y, column.Width, rowHeight), selected);

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
            // The unknown-size virtual source only reveals the end when asked, so a row the loop
            // already scheduled can turn out not to exist.
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;
            ++display;
            if (this.IsRowHidden(item))
                continue;

            var rowHeight = this.GetRowHeightFor(item);
            if (this.MergedTextOf(item) is null)
                this.PaintRowCells(g, theme, item, modelIndex, y, rowHeight, this.IsRowSelected(modelIndex), frozen);

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
            // The unknown-size virtual source only reveals the end when asked, so a row the loop
            // already scheduled can turn out not to exist.
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;
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
    private void PaintCell(IGraphics g, ITheme theme, DataGridViewColumn column, object? item, int modelIndex, Rectangle cellRect, bool selected)
    {
        var style = column.CellStyleSelector?.Invoke(item) ?? default;
        if (style.BackColor is { } backColor)
            g.FillRectangle(backColor, cellRect);

        var alignment = style.Alignment ?? column.Alignment;
        var foreColor = style.ForeColor ?? (selected ? theme.SelectionText : theme.ControlText);

        // Clip content to the cell so a value wider than its column — a long link and its full-width
        // underline especially — cannot bleed into the neighbouring column. The background fill above
        // is deliberately outside the clip so it still paints the whole cell.
        g.PushClip(cellRect);
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
                GlyphRenderer.DrawButtonFace(g, theme, face, this.GetDisplayText(column, item, modelIndex), column.EnabledSelector?.Invoke(item) ?? true);
                break;
            }

            case DataGridViewColumnKind.Link:
            {
                // The link is the one cell kind that puts a colour of its own straight onto the row
                // background instead of using foreColor or laying down an opaque face first, so it is
                // also the one that has to fall back to the selection foreground: a theme whose
                // selection background is the accent (the default one, and GTK's Adwaita) would
                // otherwise paint the selected row's link in its own background and lose it entirely.
                var text = this.GetDisplayText(column, item, modelIndex);
                var linkColor = style.ForeColor ?? (selected ? theme.SelectionText : theme.Accent);
                var textRect = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - _CellPadding), cellRect.Height);
                g.DrawText(text, theme.DefaultFont, linkColor, textRect, alignment);

                var size = g.MeasureText(text, theme.DefaultFont);
                var left = textRect.X;
                if (alignment is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter)
                    left = textRect.X + ((textRect.Width - size.Width) / 2);
                else if (alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight)
                    left = textRect.Right - size.Width;

                var underlineY = textRect.Y + ((textRect.Height + size.Height) / 2) - 1;
                g.DrawLine(linkColor, left, underlineY, left + size.Width, underlineY);
                break;
            }

            case DataGridViewColumnKind.MultiImage:
            {
                var images = column.ImagesSelector?.Invoke(item);
                var (iconSize, stride, inset) = MultiImageMetrics(column, cellRect.Height);
                if (images is null || iconSize <= 0)
                    break;

                var x = cellRect.X + _CellPadding;
                var iconTop = cellRect.Y + inset + Math.Max(0, (cellRect.Height - (2 * inset) - iconSize) / 2);
                for (var i = 0; i < images.Count; ++i)
                {
                    if (this.CurrentFrameOf(images[i]) is { } frame)
                        g.DrawImage(frame, new Rectangle(x, iconTop, iconSize, iconSize));
                    x += stride;
                }

                break;
            }

            case DataGridViewColumnKind.Progress:
            {
                var bar = new Rectangle(cellRect.X + 2, cellRect.Y + 2, Math.Max(0, cellRect.Width - 4), Math.Max(0, cellRect.Height - 4));
                GlyphRenderer.DrawProgressBar(g, theme, bar, column.ProgressSelector?.Invoke(item) ?? 0, 0, 100);
                break;
            }

            case DataGridViewColumnKind.Color:
            {
                var swatch = new Rectangle(
                    cellRect.X + _CellPadding,
                    cellRect.Y + _CellPadding,
                    Math.Max(0, cellRect.Width - (_CellPadding * 2)),
                    Math.Max(0, cellRect.Height - (_CellPadding * 2)));
                g.FillRectangle(column.ColorSelector?.Invoke(item) ?? theme.FieldBackground, swatch);
                g.DrawRectangle(theme.Border, swatch);
                break;
            }

            // The three popup-list kinds share one painter: the cell's text (a single value for the
            // combo and a single-select list, the joined set summary for the set-valued ones) plus the
            // drop affordance that says a list opens here.
            case DataGridViewColumnKind.ComboBox or DataGridViewColumnKind.ListBox or DataGridViewColumnKind.CheckedListBox:
            {
                var arrowZone = Math.Min(_ComboArrowZone, cellRect.Width);
                var textRect = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - _CellPadding - arrowZone), cellRect.Height);
                g.DrawText(this.GetDisplayText(column, item, modelIndex), theme.DefaultFont, foreColor, textRect, alignment);

                // The drop arrow: a themed triangle of stacked lines, like the ComboBox field's.
                var centerX = cellRect.Right - arrowZone + (arrowZone / 2);
                var arrowTop = cellRect.Y + ((cellRect.Height - _ComboArrowRows) / 2);
                for (var i = 0; i < _ComboArrowRows; ++i)
                    g.DrawLine(foreColor, centerX - _ComboArrowRows + 1 + i, arrowTop + i, centerX + _ComboArrowRows - 1 - i, arrowTop + i);
                break;
            }

            default:
            {
                var text = this.GetDisplayText(column, item, modelIndex);
                // Resolve through CurrentFrameOf, as every other icon-bearing control does: a
                // selector may hand back an AnimatedImage (a decoded PNG, an animated GIF), which is
                // a description of pixels rather than a bitmap the backend can blit. Drawing it raw
                // silently paints nothing.
                var icon = this.CurrentFrameOf(column.ImageSelector?.Invoke(item));
                if (icon is null)
                {
                    var plain = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - (2 * _CellPadding)), cellRect.Height);
                    g.DrawText(text, theme.DefaultFont, foreColor, plain, alignment);
                    break;
                }

                // Image + text share the shared ContentLayout geometry (PRD §5), so a grid cell places
                // its icon exactly like every other icon+text control.
                var content = new Rectangle(cellRect.X + _CellPadding, cellRect.Y, Math.Max(0, cellRect.Width - (2 * _CellPadding)), cellRect.Height);
                var box = ImageBox(column, icon, cellRect.Height);
                ContentLayout.Arrange(
                    content,
                    box,
                    text.Length == 0 ? Size.Empty : g.MeasureText(text, theme.DefaultFont),
                    column.TextImageRelation,
                    alignment,
                    out var imageRect,
                    out var textRect);

                if (!imageRect.IsEmpty)
                {
                    g.DrawImage(icon, imageRect);
                    this.PaintOverlays(g, column, item, imageRect);
                }

                if (text.Length > 0)
                {
                    // Side-by-side relations keep the classic full-height text band so the column's
                    // ContentAlignment still governs the text exactly as it does without an icon; the
                    // stacked relations use the arranged rectangle.
                    var (band, bandAlignment) = column.TextImageRelation switch
                    {
                        TextImageRelation.ImageBeforeText => (
                            new Rectangle(imageRect.Right + ContentLayout.Gap, cellRect.Y, Math.Max(0, content.Right - imageRect.Right - ContentLayout.Gap), cellRect.Height),
                            alignment),
                        TextImageRelation.TextBeforeImage => (
                            new Rectangle(content.X, cellRect.Y, Math.Max(0, imageRect.X - ContentLayout.Gap - content.X), cellRect.Height),
                            alignment),
                        _ => (textRect, ContentAlignment.MiddleLeft),
                    };

                    g.DrawText(text, theme.DefaultFont, foreColor, band, bandAlignment);
                }

                break;
            }
        }

        g.PopClip();
    }

    /// <summary>The box a cell icon occupies: the column's explicit <see cref="DataGridViewColumn.ImageSize"/>
    /// (letterboxed to the icon's aspect ratio unless the column opts out), otherwise a square inset from
    /// the row height — the historical behavior.</summary>
    private static Size ImageBox(DataGridViewColumn column, IImage icon, int rowHeight)
    {
        var explicitBox = column.ImageSize;
        if (explicitBox.Width <= 0 || explicitBox.Height <= 0)
        {
            var square = Math.Max(0, rowHeight - 4);
            return new Size(square, square);
        }

        if (!column.KeepImageAspectRatio || icon.Width <= 0 || icon.Height <= 0)
            return explicitBox;

        // Letterbox: the largest rectangle with the icon's ratio that fits the requested box.
        var scale = Math.Min((double)explicitBox.Width / icon.Width, (double)explicitBox.Height / icon.Height);
        return new Size(Math.Max(1, (int)Math.Round(icon.Width * scale)), Math.Max(1, (int)Math.Round(icon.Height * scale)));
    }

    /// <summary>Draws the column's conditional badge overlays over a cell icon: bottom-right anchored,
    /// each shifted one badge width left, so several conditions stack on one icon.</summary>
    private void PaintOverlays(IGraphics g, DataGridViewColumn column, object? item, Rectangle host)
    {
        var overlays = column.OverlayImagesSelector?.Invoke(item);
        if (overlays is null || overlays.Count == 0)
            return;

        var badge = column.OverlaySize > 0 ? column.OverlaySize : Math.Max(1, host.Height / 2);
        var x = host.Right - badge;
        var y = host.Bottom - badge;
        for (var i = 0; i < overlays.Count && x + badge > host.Left; ++i, x -= badge)
            if (this.CurrentFrameOf(overlays[i]) is { } frame)
                g.DrawImage(frame, new Rectangle(x, y, badge, badge));
    }

    /// <summary>The icon edge and stride of a <see cref="DataGridViewColumnKind.MultiImage"/> cell, shared
    /// by its painting and its per-icon hit-testing so the two never drift apart.</summary>
    private static (int Size, int Stride, int Inset) MultiImageMetrics(DataGridViewColumn column, int rowHeight)
    {
        var inset = Math.Max(0, column.ImagePadding);
        var size = Math.Max(0, rowHeight - (2 * inset));
        if (column.MaxImageSize > 0)
            size = Math.Min(size, column.MaxImageSize);

        return (size, size + Math.Max(0, column.ImageGap), inset);
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
            // The unknown-size virtual source only reveals the end when asked, so a row the loop
            // already scheduled can turn out not to exist.
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;
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
            var count = this.RowSourceCount;
            var height = this.Height;
            var y = this.HeaderHeight;
            var display = Math.Max(0, _topRow);
            while (y < height && display < count)
            {
                var modelIndex = this.ToModelIndex(display);
                // The unknown-size virtual source only reveals the end when asked, so a row the loop
                // already scheduled can turn out not to exist.
                if (!this.TryGetRowItem(modelIndex, out var item))
                    break;
                ++display;
                if (this.IsRowHidden(item))
                    continue;

                var rowHeight = this.GetRowHeightFor(item);
                var cellWidth = g.MeasureText(this.GetDisplayText(column, item, modelIndex), font).Width;
                if (column.ImageSelector?.Invoke(item) is not null)
                    cellWidth += rowHeight - 4 + _IconGap;

                if (cellWidth > widest)
                    widest = cellWidth;

                y += rowHeight;
            }

            column.Width = Math.Max(_MinColumnWidth, widest + (_CellPadding * 2));
        }
    }

    /// <summary>
    /// Applies <see cref="DataGridViewAutoSizeColumnMode.Fill"/>: the viewport width left after the
    /// fixed columns is split over the fill columns proportionally to their
    /// <see cref="DataGridViewColumn.FillWeight"/>, each floored at its
    /// <see cref="DataGridViewColumn.MinimumWidth"/>, with running-share rounding so the widths sum
    /// to the viewport. Recomputed on demand (paint, hit-testing, resize) — no cached layout, so a
    /// grid resize re-fills on its next paint.
    /// </summary>
    private void ApplyFillWidths()
    {
        var columns = _columns;
        var totalWeight = 0f;
        var fixedWidth = 0;
        var hasFill = false;
        for (var i = 0; i < columns.Count; ++i)
        {
            var column = columns[i];
            if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill)
            {
                totalWeight += column.FillWeight;
                hasFill = true;
            }
            else
                fixedWidth += column.Width;
        }

        if (!hasFill)
            return;

        var verticalOverflow = this.RowSourceCount * this.RowHeight > this.Height - this.HeaderHeight;
        var available = Math.Max(0, this.Width - this.ContentLeft - (verticalOverflow ? this.Theme.ScrollBarSize : 0) - fixedWidth);
        var assigned = 0;
        var weightUsed = 0f;
        for (var i = 0; i < columns.Count; ++i)
        {
            var column = columns[i];
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
                continue;

            weightUsed += column.FillWeight;
            var share = (int)(available * weightUsed / totalWeight) - assigned;
            assigned += share;
            column.Width = Math.Max(column.MinimumWidth, share);
        }
    }

    // --- Cell editing ------------------------------------------------------------------------------

    /// <summary>
    /// Puts the given cell into edit mode: a hosted <see cref="TextBox"/>
    /// (<see cref="DataGridViewColumnKind.Text"/>) or <see cref="NumericUpDown"/>
    /// (<see cref="DataGridViewColumnKind.NumericUpDown"/>) positioned over the cell, or a popup — the
    /// choice list of a <see cref="DataGridViewColumnKind.ComboBox"/> cell, the taller list of a
    /// <see cref="DataGridViewColumnKind.ListBox"/> or <see cref="DataGridViewColumnKind.CheckedListBox"/>
    /// cell, the month calendar of a <see cref="DataGridViewColumnKind.DateTime"/> cell — below it.
    /// Refused (returning
    /// <see langword="false"/>) for read-only cells, kinds without their edit selectors/setters,
    /// merged or hidden rows, cells outside the visible window, a veto from
    /// <see cref="CellBeginEdit"/>, or popup kinds before realization. An edit already active on
    /// another cell is committed first; its validation veto also refuses the new edit.
    /// </summary>
    public bool BeginEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.RowSourceCount || columnIndex < 0 || columnIndex >= _columns.Count)
            return false;

        if (_editRowIndex == rowIndex && _editColumnIndex == columnIndex)
            return true;

        if (this.IsEditing && !this.CommitEdit())
            return false;

        var column = _columns[columnIndex];
        var item = this.GetRowItem(rowIndex);
        if (this.IsCellReadOnly(item, column) || !IsCellEditable(column))
            return false;

        if (this.MergedTextOf(item) is not null || this.IsRowHidden(item))
            return false;

        var needsBackend = column.Kind is DataGridViewColumnKind.ComboBox
            or DataGridViewColumnKind.ListBox
            or DataGridViewColumnKind.CheckedListBox
            or DataGridViewColumnKind.DateTime
            or DataGridViewColumnKind.Color;
        var backend = this.Backend;
        if (needsBackend && backend is null)
            return false; // only a live widget knows where to float the popup (or run the dialog)

        var beginArgs = new DataGridViewCellCancelEventArgs(rowIndex, columnIndex);
        this.OnCellBeginEdit(beginArgs);
        if (beginArgs.Cancel)
            return false;

        if (column.Kind == DataGridViewColumnKind.Color)
            return this.EditColorCell(backend!, rowIndex, columnIndex, column, item);

        this.EnsureVisible(rowIndex);
        var cellBounds = this.GetCellBounds(rowIndex, columnIndex);
        if (cellBounds.IsEmpty)
            return false;

        switch (column.Kind)
        {
            case DataGridViewColumnKind.Text:
            {
                var editor = new TextBox { Text = GetEditText(column, item), Bounds = cellBounds, TabStop = false };
                _textEditor = editor;
                this.Controls.Add(editor);
                this.HookEditorDirty(editor);
                break;
            }

            case DataGridViewColumnKind.MaskedText:
            {
                var editor = new MaskedTextBox { Mask = column.Mask, Bounds = cellBounds, TabStop = false };
                editor.Text = GetEditText(column, item); // after the mask, so the seed maps into it
                _textEditor = editor;
                this.Controls.Add(editor);
                this.HookEditorDirty(editor);
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
                this.HookEditorDirty(editor);
                break;
            }

            case DataGridViewColumnKind.TimePicker:
            {
                var editor = new TimePicker
                {
                    ShowSeconds = column.ShowSeconds,
                    Use24HourClock = column.Use24HourClock,
                    MaxTime = column.MaxTime,
                    MinTime = column.MinTime,
                    Value = column.TimeSelector!(item),
                    Bounds = cellBounds,
                    TabStop = false,
                };
                _timeEditor = editor;
                this.Controls.Add(editor);
                editor.ValueChanged += this.OnEditorTextChanged; // the field has no text; a step is the edit
                break;
            }

            case DataGridViewColumnKind.DomainUpDown:
            {
                var choices = column.ItemsSelector!(item);
                _editChoices = choices;
                var editor = new DomainUpDown { Bounds = cellBounds, TabStop = false };
                var current = column.ValueSelector(item);
                for (var i = 0; i < choices.Count; ++i)
                    editor.Items.Add(ChoiceDisplayText(column, choices[i]));

                for (var i = 0; i < choices.Count; ++i)
                    if (Equals(choices[i], current))
                    {
                        editor.SelectedIndex = i;
                        break;
                    }

                _domainEditor = editor;
                this.Controls.Add(editor);
                this.HookEditorDirty(editor);
                break;
            }

            case DataGridViewColumnKind.ComboBox:
                this.OpenComboPopup(backend!, column, item, cellBounds);
                break;

            case DataGridViewColumnKind.ListBox or DataGridViewColumnKind.CheckedListBox:
                this.OpenListPopup(backend!, column, item, cellBounds);
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
    /// single-value popup kinds — which commit through their own pick gestures — this closes the
    /// popup without a write; for the set-valued ones it is the commit, writing everything the popup
    /// has ticked through <see cref="DataGridViewColumn.CheckedItemsSetter"/>. A no-op returning
    /// <see langword="true"/> while nothing edits.
    /// </summary>
    public bool CommitEdit()
    {
        if (!this.IsEditing)
            return true;

        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        var column = _columns[columnIndex];
        var item = this.GetRowItem(rowIndex);
        switch (column.Kind)
        {
            case DataGridViewColumnKind.Text or DataGridViewColumnKind.MaskedText:
            {
                var text = _textEditor!.Text; // for a masked cell this is the masked rendering
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

            case DataGridViewColumnKind.TimePicker:
            {
                var value = _timeEditor!.Value;
                if (!this.ValidateCell(rowIndex, columnIndex, value))
                    return false;

                column.TimeSetter!(item, value);
                break;
            }

            case DataGridViewColumnKind.DomainUpDown:
            {
                // Match the editor's text against the choices, like the editor's own commit points
                // do — this also catches a typed choice that was never stepped to.
                var text = _domainEditor!.Text;
                var choices = _editChoices!;
                for (var i = 0; i < choices.Count; ++i)
                {
                    if (!string.Equals(ChoiceDisplayText(column, choices[i]), text, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!this.ValidateCell(rowIndex, columnIndex, choices[i]))
                        return false;

                    column.ValueSetter!(item, choices[i]);
                    break;
                }

                break; // no match writes nothing — the cell keeps its value, like the editor's revert
            }

            case DataGridViewColumnKind.ListBox or DataGridViewColumnKind.CheckedListBox:
            {
                // A single-select list popup already committed its pick on the click, exactly like the
                // combo's; only the set-valued kinds carry pending state to write here.
                if (_editItemStates is not { } states)
                    break;

                var picked = PickedItems(_editChoices!, states);
                if (!this.ValidateCell(rowIndex, columnIndex, picked))
                    return false;

                column.CheckedItemsSetter!(item, picked);
                break;
            }
        }

        this.InvalidateDisplayText(rowIndex);
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
        DataGridViewColumnKind.Text or DataGridViewColumnKind.MaskedText => column.TextSetter is not null,
        DataGridViewColumnKind.ComboBox or DataGridViewColumnKind.DomainUpDown => column.ItemsSelector is not null && column.ValueSetter is not null,
        DataGridViewColumnKind.ListBox => column.ItemsSelector is not null && column.SelectionMode switch
        {
            SelectionMode.None => false,
            SelectionMode.One => column.ValueSetter is not null,
            _ => column.CheckedItemsSelector is not null && column.CheckedItemsSetter is not null,
        },
        DataGridViewColumnKind.CheckedListBox => column.ItemsSelector is not null && column.CheckedItemsSelector is not null && column.CheckedItemsSetter is not null,
        DataGridViewColumnKind.NumericUpDown => column.NumberSelector is not null && column.NumberSetter is not null,
        DataGridViewColumnKind.TimePicker => column.TimeSelector is not null && column.TimeSetter is not null,
        DataGridViewColumnKind.DateTime => column.DateSelector is not null && column.DateSetter is not null,
        DataGridViewColumnKind.Color => column.ColorSelector is not null && column.ColorSetter is not null,
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

    /// <summary>
    /// The modal edit of a <see cref="DataGridViewColumnKind.Color"/> cell: the platform color dialog
    /// opens seeded with the cell's color, a pick validates and writes through
    /// <see cref="DataGridViewColumn.ColorSetter"/>, cancelling (or a validation veto) writes
    /// nothing. The session begins and ends within this call — the grid never stays in edit mode —
    /// and <see cref="CellEndEdit"/> closes it either way; the return value says whether a color was
    /// picked.
    /// </summary>
    private bool EditColorCell(IPlatformBackend backend, int rowIndex, int columnIndex, DataGridViewColumn column, object? item)
    {
        _currentColumnIndex = columnIndex;
        var picked = backend.ShowColorDialog(column.ColorSelector!(item));
        if (picked is { } color && this.ValidateCell(rowIndex, columnIndex, color))
        {
            column.ColorSetter!(item, color);
            this.InvalidateDisplayText(rowIndex);
            this.Invalidate();
        }

        this.OnCellEndEdit(new(rowIndex, columnIndex));
        return picked is not null;
    }

    /// <summary>Tears the editor surface down (hosted child or popup), resets the edit state and
    /// raises <see cref="CellEndEdit"/> — the shared tail of commit and cancel.</summary>
    private void EndEdit(int rowIndex, int columnIndex)
    {
        _editRowIndex = -1;
        _editColumnIndex = -1;
        _editChoices = null;
        _editItemStates = null;
        _editAnchorIndex = -1;
        if (_textEditor is { } textEditor)
        {
            _textEditor = null;
            textEditor.TextChanged -= this.OnEditorTextChanged;
            this.Controls.Remove(textEditor);
        }

        if (_numericEditor is { } numericEditor)
        {
            _numericEditor = null;
            numericEditor.TextChanged -= this.OnEditorTextChanged;
            this.Controls.Remove(numericEditor);
        }

        if (_domainEditor is { } domainEditor)
        {
            _domainEditor = null;
            domainEditor.TextChanged -= this.OnEditorTextChanged;
            this.Controls.Remove(domainEditor);
        }

        if (_timeEditor is { } timeEditor)
        {
            _timeEditor = null;
            timeEditor.ValueChanged -= this.OnEditorTextChanged;
            this.Controls.Remove(timeEditor);
        }

        if (_editPopupShown)
        {
            _editPopupShown = false;
            _editPopup?.Hide();
        }

        if (_editDirty)
        {
            _editDirty = false;
            this.OnCurrentCellDirtyStateChanged(EventArgs.Empty);
        }

        this.Invalidate();
        this.OnCellEndEdit(new(rowIndex, columnIndex));
    }

    /// <summary>Watches a hosted editor for its first content change, which flips
    /// <see cref="IsCurrentCellDirty"/>.</summary>
    private void HookEditorDirty(Control editor) => editor.TextChanged += this.OnEditorTextChanged;

    /// <summary>Flips the dirty flag on the first editor change of the active edit.</summary>
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editDirty || !this.IsEditing)
            return;

        _editDirty = true;
        this.OnCurrentCellDirtyStateChanged(EventArgs.Empty);
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

            case DataGridViewColumnKind.ListBox or DataGridViewColumnKind.CheckedListBox:
                switch (e.KeyCode)
                {
                    case Keys.Escape:
                        this.CancelEdit();
                        e.Handled = true;
                        break;

                    case Keys.Enter:
                        // A set-valued popup commits whatever is ticked; a single-select one behaves
                        // exactly like the combo and commits the row the caret sits on.
                        if (_editItemStates is not null)
                            this.CommitEdit();
                        else if (_editChoices is { } listChoices && _editHoverIndex >= 0 && _editHoverIndex < listChoices.Count)
                            this.CommitComboChoice(_editHoverIndex);
                        else
                            this.CancelEdit();

                        e.Handled = true;
                        break;

                    case Keys.Space when _editItemStates is not null && _editHoverIndex >= 0:
                        this.ToggleEditItem(_editHoverIndex, !_editItemStates[_editHoverIndex]);
                        _editPopup?.InvalidateAll();
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
                        this.CommitAndMoveDown();
                        e.Handled = true;
                        break;

                    case Keys.Escape:
                        this.CancelEdit();
                        e.Handled = true;
                        break;

                    case Keys.Tab:
                        this.CommitAndMoveSideways(e.Shift ? -1 : 1);
                        e.Handled = true;
                        break;

                    case Keys.Up when _domainEditor is { } domainUp:
                        domainUp.UpButton();
                        e.Handled = true;
                        break;

                    case Keys.Down when _domainEditor is { } domainDown:
                        domainDown.DownButton();
                        e.Handled = true;
                        break;

                    // The time editor owns the arrows entirely: up/down step the part under its
                    // caret, left/right move that caret between hours, minutes, seconds and AM/PM.
                    case Keys.Up when _timeEditor is { } timeUp:
                        timeUp.UpButton();
                        e.Handled = true;
                        break;

                    case Keys.Down when _timeEditor is { } timeDown:
                        timeDown.DownButton();
                        e.Handled = true;
                        break;

                    case Keys.Left when _timeEditor is { } timeLeft:
                        timeLeft.SelectPreviousField();
                        e.Handled = true;
                        break;

                    case Keys.Right when _timeEditor is { } timeRight:
                        timeRight.SelectNextField();
                        e.Handled = true;
                        break;
                }

                break;
        }
    }

    /// <summary>
    /// The hosted editors' Enter: commits the edit and moves the selection one display row down in
    /// the same column, matching the classic grid; under
    /// <see cref="DataGridViewEditMode.EditOnEnter"/> the new current cell starts editing again. A
    /// validation veto keeps the edit where it is.
    /// </summary>
    private void CommitAndMoveDown()
    {
        var columnIndex = _editColumnIndex;
        if (!this.CommitEdit())
            return;

        this.MoveSelection(1);
        if (this.EditMode == DataGridViewEditMode.EditOnEnter)
            this.BeginEdit(_selectedRowIndex, columnIndex);
    }

    /// <summary>
    /// The hosted editors' Tab and Shift+Tab: commits the edit and makes the next (or previous)
    /// editable cell in display order the current cell, wrapping to the following (or preceding)
    /// navigable row; under <see cref="DataGridViewEditMode.EditOnEnter"/> that cell starts editing
    /// again. A validation veto keeps the edit where it is; without another editable cell the commit
    /// stands and nothing moves.
    /// </summary>
    private void CommitAndMoveSideways(int direction)
    {
        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        if (!this.CommitEdit())
            return;

        if (!this.FindNextEditableCell(rowIndex, columnIndex, direction, out var nextRow, out var nextColumn))
            return;

        if (nextRow != _selectedRowIndex)
            this.SelectedRowIndex = nextRow;

        _currentColumnIndex = nextColumn;
        if (this.EditMode == DataGridViewEditMode.EditOnEnter)
            this.BeginEdit(nextRow, nextColumn);
    }

    /// <summary>
    /// Finds the next cell Tab can edit, walking the display columns from the given cell in
    /// <paramref name="direction"/> and wrapping over navigable rows; bails out immediately when no
    /// column can edit at all, so a grid of display-only columns never walks its rows.
    /// </summary>
    private bool FindNextEditableCell(int rowIndex, int columnIndex, int direction, out int nextRow, out int nextColumn)
    {
        nextRow = -1;
        nextColumn = -1;

        var anyEditable = false;
        for (var i = 0; i < _columns.Count; ++i)
            anyEditable |= IsCellEditable(_columns[i]);
        if (!anyEditable)
            return false;

        this.EnsureSortMap();
        this.EnsureDisplayMap();
        var map = _displayMap!;
        var count = this.RowSourceCount;
        var display = this.ToDisplayIndex(rowIndex);
        if (display < 0)
            return false;

        var d = Array.IndexOf(map, columnIndex) + direction;
        while (display >= 0 && display < count)
        {
            var modelRow = this.ToModelIndex(display);
            if (this.IsRowNavigable(modelRow))
            {
                var item = this.GetRowItem(modelRow);
                while (d >= 0 && d < map.Length)
                {
                    var column = _columns[map[d]];
                    if (IsCellEditable(column) && !this.IsCellReadOnly(item, column))
                    {
                        nextRow = modelRow;
                        nextColumn = map[d];
                        return true;
                    }

                    d += direction;
                }
            }

            var next = display + direction;
            if (next < 0 || next >= count)
                break;

            display = next;
            d = direction > 0 ? 0 : map.Length - 1;
        }

        return false;
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

        // A band in flight owns an auto-scroll timer, whose source is the backend that just went away.
        _marquee?.Dispose();
        _marquee = null;

        _filterMenu?.Dispose();
        _filterMenu = null;
        _editPopupShown = false;
        _editPopup?.Dispose();
        _editPopup = null;
        _editCalendar = null;
        _scrollDragging = false;
        _hoverRowIndex = -1;
        _hoverColumnIndex = -1;
        _tipShown = false;
        _tipAutoPopPhase = false;
        _tipTimer?.Dispose();
        _tipTimer = null;
        _tipPopup?.Dispose();
        _tipPopup = null;
    }

    /// <summary>
    /// The client-space rectangle of a cell, honoring scroll positions, per-row heights, hidden rows,
    /// sorting and the display order (frozen columns at their pinned x). <see cref="Rectangle.Empty"/>
    /// when the cell lies outside the visible window — the geometry editors are hosted over.
    /// </summary>
    public Rectangle GetCellBounds(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.RowSourceCount || columnIndex < 0 || columnIndex >= _columns.Count)
            return Rectangle.Empty;

        this.ApplyFillWidths();
        this.EnsureSortMap();
        this.EnsureDisplayMap();

        var count = this.RowSourceCount;
        var height = this.Height;
        var y = this.HeaderHeight;
        var display = Math.Max(0, _topRow);
        var rowTop = -1;
        var rowHeight = 0;
        while (y < height && display < count)
        {
            var modelIndex = this.ToModelIndex(display);
            // The unknown-size virtual source only reveals the end when asked, so a row the loop
            // already scheduled can turn out not to exist.
            if (!this.TryGetRowItem(modelIndex, out var item))
                break;
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

    /// <summary>
    /// Opens the popup list of a <see cref="DataGridViewColumnKind.ListBox"/> or
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> cell below the cell — the combo's popup
    /// with a taller row budget. A single-select list starts its caret on the cell's current value; a
    /// set-valued one seeds one pending state per choice from
    /// <see cref="DataGridViewColumn.CheckedItemsSelector"/> and scrolls the first picked item into
    /// view.
    /// </summary>
    private void OpenListPopup(IPlatformBackend backend, DataGridViewColumn column, object? item, Rectangle cellBounds)
    {
        var choices = column.ItemsSelector!(item);
        _editChoices = choices;
        _editPopupRows = Math.Max(1, Math.Min(choices.Count, _MaxListPopupRows));
        _editPopupSize = new Size(cellBounds.Width, _editPopupRows * this.Theme.RowHeight);
        _editPopupTop = 0;
        _editHoverIndex = -1;
        _editAnchorIndex = -1;

        if (IsSetValued(column))
        {
            var states = new bool[choices.Count];
            var current = column.CheckedItemsSelector!(item);
            for (var i = 0; i < choices.Count; ++i)
                for (var c = 0; c < current.Count; ++c)
                    if (Equals(choices[i], current[c]))
                    {
                        states[i] = true;
                        if (_editHoverIndex < 0)
                            _editHoverIndex = i;

                        break;
                    }

            _editItemStates = states;
            _editAnchorIndex = _editHoverIndex;
        }
        else
        {
            _editItemStates = null;
            var current = column.ValueSelector(item);
            for (var i = 0; i < choices.Count; ++i)
                if (Equals(choices[i], current))
                {
                    _editHoverIndex = i;
                    break;
                }
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
        calendar.Level = CalendarLevel.Month; // every open starts on the day page, however it was left

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

        popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnEditPopupPaint(e);
        popup.MouseDown += (_, e) => this.OnEditPopupMouseDown(e);
        popup.MouseMove += (_, e) => this.OnEditPopupMouseMove(e);
        popup.MouseUp += (_, e) => this.OnEditPopupMouseUp(e);
        popup.MouseWheel += (_, e) => this.OnEditPopupMouseWheel(e);
        popup.KeyDown += (_, e) => this.OnKeyDown(e); // backends with a keyboard grab route keys here
        popup.Dismissed += (_, _) => this.OnEditPopupDismissed();
        return _editPopup = popup;
    }

    /// <summary>Whether the active edit is the popup calendar (as opposed to one of the popup lists).</summary>
    private bool IsCalendarEditing => this.IsEditing && _columns[_editColumnIndex].Kind == DataGridViewColumnKind.DateTime;

    /// <summary>Whether the popup currently open carries a check square in front of every row — the
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> editor.</summary>
    private bool IsCheckedListEditing => this.IsEditing && _columns[_editColumnIndex].Kind == DataGridViewColumnKind.CheckedListBox;

    private void OnEditPopupPaint(PaintEventArgs e)
    {
        if (!this.IsEditing)
            return;

        if (this.IsCalendarEditing)
        {
            _editCalendar!.Paint(e.Graphics, this.Theme, _editPopupSize, true);
            return;
        }

        // The combo/list choice rows, painted exactly like ComboBox drop-down and ListBox rows.
        var g = e.Graphics;
        var theme = this.Theme;
        var size = _editPopupSize;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, size.Width, size.Height));

        var column = _columns[_editColumnIndex];
        var choices = _editChoices!;
        var states = _editItemStates;
        var checkBoxes = this.IsCheckedListEditing;
        var rowHeight = theme.RowHeight;
        var last = Math.Min(choices.Count, _editPopupTop + _editPopupRows);
        for (var i = _editPopupTop; i < last; ++i)
        {
            var rowRect = new Rectangle(0, (i - _editPopupTop) * rowHeight, size.Width, rowHeight);

            // A checked list highlights only the caret row — its ticks carry the state; a plain list
            // highlights every picked row, the caret row included.
            var highlighted = i == _editHoverIndex || (!checkBoxes && states is not null && states[i]);
            if (highlighted)
                GlyphRenderer.FillSelection(g, theme, rowRect);

            var textRect = rowRect;
            if (checkBoxes)
            {
                var boxTop = rowRect.Y + Math.Max(0, (rowRect.Height - GlyphRenderer.CheckBoxSize) / 2);
                GlyphRenderer.DrawCheckBox(g, theme, new(rowRect.X + 2, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), states![i]);

                var indent = GlyphRenderer.CheckBoxSize + _CheckGlyphGap + 2;
                textRect = new(rowRect.X + indent, rowRect.Y, Math.Max(0, rowRect.Width - indent), rowRect.Height);
            }

            ListBox.DrawRowContent(g, theme, textRect, ChoiceDisplayText(column, choices[i]), null, highlighted);
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
        if (row >= _editChoices!.Count)
            return;

        // A single-value popup commits on the click; a set-valued one only updates its pending state
        // and waits for Enter (or the click outside that closes it) to commit the whole set.
        if (_editItemStates is not { } states)
        {
            this.CommitComboChoice(row);
            return;
        }

        _editHoverIndex = row;
        if (this.IsCheckedListEditing || _columns[_editColumnIndex].SelectionMode == SelectionMode.MultiSimple || e.Control)
            this.ToggleEditItem(row, !states[row]);
        else if (e.Shift && _editAnchorIndex >= 0)
        {
            // MultiExtended's Shift+click: the anchor..row run replaces the picked set.
            Array.Clear(states);
            var from = Math.Min(_editAnchorIndex, row);
            var to = Math.Max(_editAnchorIndex, row);
            for (var i = from; i <= to; ++i)
                states[i] = true;
        }
        else
        {
            Array.Clear(states);
            states[row] = true;
            _editAnchorIndex = row;
        }

        _editPopup?.InvalidateAll();
    }

    /// <summary>
    /// Flips one popup item's pending state, announcing it through <see cref="CellItemCheck"/> first
    /// so a handler can veto or redirect it — the grid-side shape of
    /// <see cref="CheckedListBox.SetItemChecked"/>. Also re-anchors the range gesture on the item.
    /// </summary>
    private void ToggleEditItem(int index, bool value)
    {
        var states = _editItemStates!;
        var current = states[index];
        if (current == value)
            return;

        if (this.CellItemCheck is not null)
        {
            var args = new ItemCheckEventArgs(index, current, value);
            this.OnCellItemCheck(args);
            if (args.NewValue == current)
                return;

            value = args.NewValue;
        }

        states[index] = value;
        _editAnchorIndex = index;
    }

    /// <summary>The picked items of a set-valued popup, in <see cref="DataGridViewColumn.ItemsSelector"/>
    /// order — one array per commit, handed to the setter and never held on to.</summary>
    private static object?[] PickedItems(IReadOnlyList<object?> choices, bool[] states)
    {
        var count = 0;
        for (var i = 0; i < states.Length; ++i)
            if (states[i])
                ++count;

        var picked = new object?[count];
        var next = 0;
        for (var i = 0; i < states.Length; ++i)
            if (states[i])
                picked[next++] = choices[i];

        return picked;
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

    /// <summary>
    /// Reacts to light dismissal (click outside, grab loss, Escape): the surface is already hidden, so
    /// the edit just ends without a write — dismissal cancels, for every popup kind including the
    /// set-valued ones.
    /// </summary>
    /// <remarks>
    /// Committing the ticked set here instead would read better for a mouse-only user, but the
    /// backends cannot agree on what dismissal means. A popup surface swallows Escape at its own
    /// top-level on some of them and routes it to the grid on others, so "dismissal commits" would
    /// make Escape abandon the edit on one backend and save it on the next — the one outcome a user
    /// must never have to guess at. Dismissal therefore always abandons, and Enter is the commit.
    /// </remarks>
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

        _columns[columnIndex].ValueSetter!(this.GetRowItem(rowIndex), choice);
        this.InvalidateDisplayText(rowIndex);
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
        var item = this.GetRowItem(rowIndex);
        var proposed = _editCalendar!.SelectionStart.Date + column.DateSelector!(item).TimeOfDay;
        if (!this.ValidateCell(rowIndex, columnIndex, proposed))
            return;

        column.DateSetter!(item, proposed);
        this.InvalidateDisplayText(rowIndex);
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

    // --- Cell tooltips -----------------------------------------------------------------------------

    /// <summary>
    /// Tracks the hovered cell for the tooltip: entering a cell whose column yields tooltip text arms
    /// the show delay, leaving it hides the tip — the grid-internal, per-cell sibling of the
    /// <see cref="ToolTip"/> component, sharing its delays and popup painting.
    /// </summary>
    private void TrackHoverCell(MouseEventArgs e)
    {
        if (!this.ShowCellToolTips || this.Backend is null)
            return;

        var hoverRowHeight = 0;
        var hoverCellX = 0;
        var rowIndex = e.Y >= this.HeaderHeight ? this.HitTestRow(e.Y, out hoverRowHeight, out _) : -1;
        var columnIndex = rowIndex >= 0 ? this.HitTestColumn(e.X, out hoverCellX) : -1;
        var imageIndex = columnIndex >= 0 ? this.HitTestCellImage(rowIndex, columnIndex, hoverCellX, hoverRowHeight) : -1;
        _hoverPoint = e.Location;
        if (rowIndex == _hoverRowIndex && columnIndex == _hoverColumnIndex && imageIndex == _hoverImageIndex)
            return;

        _hoverRowIndex = rowIndex;
        _hoverColumnIndex = columnIndex;
        _hoverImageIndex = imageIndex;
        this.HideCellToolTip();
        if (rowIndex < 0 || columnIndex < 0 || this.GetCellTooltip(rowIndex, columnIndex, imageIndex) is null)
            return;

        var timer = this.EnsureTipTimer();
        timer.Interval = 500; // the ToolTip component's initial delay
        timer.Start();
    }

    /// <summary>Hides the cell tip and disarms any pending delay.</summary>
    private void HideCellToolTip()
    {
        _tipTimer?.Stop();
        _tipAutoPopPhase = false;
        if (!_tipShown)
            return;

        _tipShown = false;
        _tipPopup?.Hide();
    }

    /// <summary>The delay elapsed: shows the hovered cell's tip near the cursor, then hides it again
    /// after the auto-pop phase.</summary>
    private void OnTipTimerTick(object? sender, EventArgs e)
    {
        var timer = _tipTimer!;
        timer.Stop();
        if (_tipAutoPopPhase)
        {
            this.HideCellToolTip();
            return;
        }

        if (this.Backend is not { } backend || this.GetCellTooltip(_hoverRowIndex, _hoverColumnIndex, _hoverImageIndex) is not { } text)
            return;

        _tipText = text;
        var popup = this.EnsureTipPopup(backend);
        _tipShown = true;
        popup.ShowAt(this.PointToScreen(new Point(_hoverPoint.X, _hoverPoint.Y + ToolTip.CursorOffset)), ToolTip.MeasureTip(backend, text));

        _tipAutoPopPhase = true;
        timer.Interval = 5000; // the ToolTip component's auto-pop delay
        timer.Start();
    }

    /// <summary>Creates the tip delay timer on first use.</summary>
    private Timer EnsureTipTimer()
    {
        var timer = _tipTimer;
        if (timer is not null)
            return timer;

        timer = new(this.Backend!);
        timer.Tick += this.OnTipTimerTick;
        return _tipTimer = timer;
    }

    /// <summary>Creates the tip popup on first use, painting through the shared
    /// <see cref="ToolTip"/> renderer.</summary>
    private IPopupPeer EnsureTipPopup(IPlatformBackend backend)
    {
        var popup = _tipPopup;
        if (popup is not null)
            return popup;

        popup = backend.CreatePopup(this.OwnerWindowPeer);

        // Passive, exactly like ToolTip's own surface: a tip that grabbed would eat the next click
        // on the grid underneath it.
        popup.LightDismiss = false;
        popup.Paint += (_, e) => ToolTip.PaintTip(e.Graphics, this.Theme, _tipText);
        popup.Dismissed += (_, _) => _tipShown = false;
        return _tipPopup = popup;
    }

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
        var count = this.RowSourceCount;
        var first = true;
        for (var display = 0; display < count; ++display)
        {
            var modelIndex = this.ToModelIndex(display);
            if (!this.IsRowSelected(modelIndex))
                continue;

            if (!first)
                builder.Append("\r\n");
            first = false;

            var item = this.GetRowItem(modelIndex);
            if (this.MergedTextOf(item) is { } mergedText)
            {
                builder.Append(mergedText);
                continue;
            }

            for (var d = 0; d < map.Length; ++d)
            {
                if (d > 0)
                    builder.Append('\t');
                builder.Append(this.GetDisplayText(_columns[map[d]], item, modelIndex));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Pastes tab-separated text into the grid starting at the current cell: lines map onto display
    /// rows from the current row downward (skipping hidden, unselectable and merged rows), cells onto
    /// display columns from the current column rightward; content past the last column or row is
    /// dropped. Each target cell converts its text to the column kind's value and writes through that
    /// kind's setter — read-only cells, display-only columns and unparseable text are skipped, their
    /// position still consumed, and every write runs <see cref="CellValidating"/> first (a veto skips
    /// that one cell). <see cref="PasteCompleted"/> closes the operation. Ctrl+V feeds this from the
    /// system clipboard through the backend. A no-op without a current cell.
    /// </summary>
    /// <remarks>
    /// The classic toolkit ships no built-in grid paste, so this follows its clipboard-copy shape in
    /// reverse (the Excel-style block paste every WinForms grid hand-rolls).
    /// </remarks>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text) || _selectedRowIndex < 0 || _columns.Count == 0)
            return;

        this.EnsureSortMap();
        this.EnsureDisplayMap();
        var map = _displayMap!;
        var startColumn = Array.IndexOf(map, Math.Min(_currentColumnIndex, _columns.Count - 1));
        var display = this.ToDisplayIndex(_selectedRowIndex);
        if (startColumn < 0 || display < 0)
            return;

        var count = this.RowSourceCount;
        var lines = text.Split('\n');
        var lineCount = lines.Length;
        if (lineCount > 0 && lines[lineCount - 1].Length == 0)
            --lineCount; // a trailing newline carries no row

        for (var l = 0; l < lineCount && display < count; ++l)
        {
            while (display < count && !this.IsRowNavigable(this.ToModelIndex(display)))
                ++display;
            if (display >= count)
                break;

            var rowIndex = this.ToModelIndex(display);
            ++display;

            var cells = lines[l].TrimEnd('\r').Split('\t');
            for (var c = 0; c < cells.Length && startColumn + c < map.Length; ++c)
                if (this.TryPasteCell(rowIndex, map[startColumn + c], cells[c]))
                    this.InvalidateDisplayText(rowIndex);
        }

        this.Invalidate();
        this.OnPasteCompleted(EventArgs.Empty);
    }

    /// <summary>
    /// Writes one pasted cell: converts the text to the column kind's value, runs
    /// <see cref="CellValidating"/> and writes through the kind's setter. Returns
    /// <see langword="false"/> — writing nothing — for read-only cells, kinds without a text form or
    /// setter, unparseable text and validation vetoes.
    /// </summary>
    private bool TryPasteCell(int rowIndex, int columnIndex, string text)
    {
        var column = _columns[columnIndex];
        var item = this.GetRowItem(rowIndex);
        if (this.IsCellReadOnly(item, column))
            return false;

        switch (column.Kind)
        {
            case DataGridViewColumnKind.Text or DataGridViewColumnKind.MaskedText:
            {
                if (column.TextSetter is not { } setter || !this.ValidateCell(rowIndex, columnIndex, text))
                    return false;

                setter(item, text);
                return true;
            }

            case DataGridViewColumnKind.Check:
            {
                if (column.CheckedSetter is not { } setter)
                    return false;

                bool state;
                if (bool.TryParse(text, out var parsed))
                    state = parsed;
                else if (text is "1" or "0")
                    state = text == "1";
                else
                    return false;

                if (!this.ValidateCell(rowIndex, columnIndex, state))
                    return false;

                setter(item, state);
                return true;
            }

            case DataGridViewColumnKind.NumericUpDown:
            {
                if (column.NumberSetter is not { } setter || !decimal.TryParse(text, out var number))
                    return false;

                number = Math.Clamp(number, column.Minimum, column.Maximum);
                if (!this.ValidateCell(rowIndex, columnIndex, number))
                    return false;

                setter(item, number);
                return true;
            }

            case DataGridViewColumnKind.DateTime:
            {
                if (column.DateSetter is not { } setter || !DateTime.TryParse(text, out var date))
                    return false;

                if (!this.ValidateCell(rowIndex, columnIndex, date))
                    return false;

                setter(item, date);
                return true;
            }

            case DataGridViewColumnKind.CheckedListBox or DataGridViewColumnKind.ListBox when IsSetValued(column):
            {
                // The mirror image of the cell's own summary: split it back on the separator and map
                // every piece onto a choice. One unknown piece fails the whole cell, so a partial set
                // is never written.
                if (column.ItemsSelector is null || column.CheckedItemsSetter is not { } setter)
                    return false;

                var choices = column.ItemsSelector(item);
                var pieces = text.Split(',');
                var states = new bool[choices.Count];
                for (var p = 0; p < pieces.Length; ++p)
                {
                    var piece = pieces[p].Trim();
                    if (piece.Length == 0)
                        continue;

                    var found = false;
                    for (var i = 0; i < choices.Count; ++i)
                        if (string.Equals(ChoiceDisplayText(column, choices[i]), piece, StringComparison.Ordinal))
                        {
                            states[i] = true;
                            found = true;
                            break;
                        }

                    if (!found)
                        return false;
                }

                var picked = PickedItems(choices, states);
                if (!this.ValidateCell(rowIndex, columnIndex, picked))
                    return false;

                setter(item, picked);
                return true;
            }

            case DataGridViewColumnKind.TimePicker:
            {
                if (column.TimeSetter is not { } setter || !TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time))
                    return false;

                if (time < column.MinTime || time > column.MaxTime)
                    return false;

                if (!this.ValidateCell(rowIndex, columnIndex, time))
                    return false;

                setter(item, time);
                return true;
            }

            case DataGridViewColumnKind.ComboBox or DataGridViewColumnKind.DomainUpDown or DataGridViewColumnKind.ListBox:
            {
                if (column.ItemsSelector is null || column.ValueSetter is not { } setter)
                    return false;

                var choices = column.ItemsSelector(item);
                for (var i = 0; i < choices.Count; ++i)
                {
                    if (!string.Equals(ChoiceDisplayText(column, choices[i]), text, StringComparison.Ordinal))
                        continue;

                    if (!this.ValidateCell(rowIndex, columnIndex, choices[i]))
                        return false;

                    setter(item, choices[i]);
                    return true;
                }

                return false;
            }

            default:
                return false; // button, link, image, progress and color cells take no pasted text
        }
    }
}
