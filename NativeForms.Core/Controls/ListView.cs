using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn list view painted in the native theme, covering the full
/// <see cref="ListViewView"/> family: <see cref="ListViewView.Details"/> (multi-column grid with a
/// header row), <see cref="ListViewView.List"/> (single vertical column),
/// <see cref="ListViewView.LargeIcon"/>/<see cref="ListViewView.SmallIcon"/> (cells flowing
/// left-to-right in rows) and <see cref="ListViewView.Tile"/> (icon beside a two-line text block).
/// Items can carry check boxes (<see cref="CheckBoxes"/>, vetoable through <see cref="ItemCheck"/>),
/// group into titled sections (<see cref="Groups"/>/<see cref="ShowGroups"/>), sort in place
/// (<see cref="Sorting"/>/<see cref="ItemSorter"/>/<see cref="Sort"/>, with <see cref="ColumnClick"/>
/// on the Details header) and edit their labels through a hosted native text box
/// (<see cref="LabelEdit"/>/<see cref="BeginEdit"/>). Selection follows the classic control:
/// <see cref="MultiSelect"/> (default) gives the extended Ctrl/Shift model with sorted
/// <see cref="SelectedIndices"/> and one <see cref="SelectedIndexChanged"/> per gesture. Painting is
/// virtualized to the visible row window in every view, so it stays cheap for very large
/// <see cref="Items"/> collections.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="DataGridView"/> — which sorts through an index indirection because its rows are
/// arbitrary bound objects the grid must not reorder — <see cref="Sort"/> mutates the order of
/// <see cref="Items"/> itself, exactly like <c>System.Windows.Forms.ListView</c> does: the items are
/// owned by the control, so item indices always equal presentation order and callers observe the
/// sorted list. Sorting runs on explicit <see cref="Sort"/> calls, on assigning
/// <see cref="Sorting"/>/<see cref="ItemSorter"/> and on Details header clicks; after bulk item
/// mutation, call <see cref="Sort"/> again.
/// </para>
/// <para>
/// Keys typed inside the hosted native editor are not observable from the core, so a label edit has
/// no reliable native Enter moment once the hosted editor holds focus. Edits therefore commit at the honest points available:
/// Enter/Escape while the list surface has focus, any click on the list, <see cref="EndEdit"/>, and
/// starting another edit — mirroring how <see cref="UpDownBase"/> commits its hosted editor.
/// </para>
/// <para>TODO: a virtual-mode item API.</para>
/// </remarks>
public class ListView : OwnerDrawnControl
{
    private const int _IconGap = 4;
    private const int _CellPad = 2;
    private const int _CheckGap = 4;
    private const int _SmallIconLabelWidth = 100;
    private const int _TileLabelWidth = 120;
    private const int _LargeIconCellPad = 16;
    private const int _LargeIconLabelGap = 4;
    private const int _MinLargeIconCellWidth = 64;
    private const int _SortArrowWidth = 10;
    private const string _DefaultGroupHeader = "Default";

    /// <summary>The selected row indices, always kept sorted ascending.</summary>
    private readonly List<int> _selectedIndices = [];

    /// <summary>An index-aligned mirror of <see cref="Items"/>, so removals can detach the item.</summary>
    private readonly List<ListViewItem> _attachedItems = [];

    private int _focusedIndex = -1;
    private int _anchorIndex = -1;
    private int _topIndex;
    private int? _itemHeight;

    /// <summary>Model item indices in display (group) order; <see langword="null"/> while no grouping is active.</summary>
    private List<int>? _displayItems;

    /// <summary>The flattened visual rows (headers + item runs); <see langword="null"/> while no grouping is active.</summary>
    private List<FlatRow>? _flatRows;

    private bool _flatDirty = true;

    private int _sortColumn = -1;

    private TextBox? _labelEditor;
    private int _editIndex = -1;

    /// <summary>Creates a list view.</summary>
    public ListView()
    {
        this.Columns = new();
        this.Items = new();
        this.Groups = new();
        this.Columns.ListChanged += this.OnColumnsChanged;
        this.Items.ListChanged += this.OnItemsChanged;
        this.Groups.ListChanged += this.OnGroupsChanged;
    }

    /// <summary>The columns shown in Details view. Mutating this collection repaints the control.</summary>
    public ObservableList<ColumnHeader> Columns { get; }

    /// <summary>The rows shown. Mutating this collection repaints the control.</summary>
    public ObservableList<ListViewItem> Items { get; }

    /// <summary>The groups items can join via <see cref="ListViewItem.Group"/>, rendered in this order.</summary>
    public ObservableList<ListViewGroup> Groups { get; }

    /// <summary>How items are arranged. Defaults to <see cref="ListViewView.Details"/>. Changing the
    /// view commits a pending label edit.</summary>
    public ListViewView View
    {
        get => field;
        set
        {
            if (field == value)
                return;

            this.EndEdit(cancel: false);
            field = value;
            _flatDirty = true;
            this.Invalidate();
        }
    } = ListViewView.Details;

    /// <summary>Whether the column header row is shown (Details view only). Defaults to <see langword="true"/>.</summary>
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

    /// <summary>Whether items render under their group's header section (in every view except
    /// <see cref="ListViewView.List"/>, like the classic control). Defaults to
    /// <see langword="true"/>; without any <see cref="Groups"/> it has no effect.</summary>
    public bool ShowGroups
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _flatDirty = true;
            this.Invalidate();
        }
    } = true;

    /// <summary>Whether clicking anywhere on a Details row selects it. Defaults to <see langword="true"/>.</summary>
    public bool FullRowSelect
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

    /// <summary>Whether the user can select more than one item (Ctrl/Shift click and keyboard, like a
    /// <see cref="SelectionMode.MultiExtended"/> list box). Defaults to <see langword="true"/>, like
    /// the classic control; turning it off collapses the selection to its first item.</summary>
    public bool MultiSelect
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value && _selectedIndices.Count > 1)
                this.FinishSelectionGesture(this.SelectOnlyCore(_selectedIndices[0]));
        }
    } = true;

    /// <summary>Whether every item shows a themed check box; see <see cref="ListViewItem.Checked"/>.</summary>
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

    /// <summary>Whether the user can edit item labels; see <see cref="BeginEdit"/>.</summary>
    public bool LabelEdit { get; set; }

    /// <summary>The icon store for <see cref="ListViewView.LargeIcon"/> and <see cref="ListViewView.Tile"/>
    /// (via <see cref="ListViewItem.ImageIndex"/>); its image size drives those views' cell size.</summary>
    public ImageList? LargeImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            _flatDirty = true;
            this.Invalidate();
        }
    }

    /// <summary>The icon store for the remaining views (via <see cref="ListViewItem.ImageIndex"/>);
    /// its image size drives the <see cref="ListViewView.SmallIcon"/> cell size.</summary>
    public ImageList? SmallImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            _flatDirty = true;
            this.Invalidate();
        }
    }

    /// <summary>
    /// The automatic sort direction over the item text (or the last clicked column's text).
    /// Assigning <see cref="SortOrder.Ascending"/>/<see cref="SortOrder.Descending"/> sorts
    /// immediately; <see cref="SortOrder.None"/> stops sorting without restoring the old order.
    /// </summary>
    public SortOrder Sorting
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (value == SortOrder.None)
            {
                this.Invalidate();
                return;
            }

            this.Sort();
        }
    }

    /// <summary>
    /// An optional custom item ordering — the delegate-shaped stand-in for the classic
    /// <c>ListViewItemSorter</c>. When set it wins over <see cref="Sorting"/>; assigning a
    /// non-<see langword="null"/> comparison sorts immediately.
    /// </summary>
    public Comparison<ListViewItem>? ItemSorter
    {
        get => field;
        set
        {
            field = value;
            if (value is not null)
                this.Sort();
        }
    }

    /// <summary>The pixel height of a row (and of the header). Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            _flatDirty = true;
            this.Invalidate();
        }
    }

    /// <summary>The first selected index, or -1 for none. Setting it replaces the whole selection.</summary>
    public int SelectedIndex
    {
        get => _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (clamped >= 0)
            {
                _focusedIndex = clamped;
                _anchorIndex = clamped;
                this.EnsureVisible(clamped);
            }

            this.FinishSelectionGesture(clamped < 0 ? this.ClearSelectionCore() : this.SelectOnlyCore(clamped));
        }
    }

    /// <summary>The selected row indices, sorted ascending. Empty for none.</summary>
    public IReadOnlyList<int> SelectedIndices => _selectedIndices;

    /// <summary>The selected items, in index order. A live view over <see cref="SelectedIndices"/>.</summary>
    public IReadOnlyList<ListViewItem> SelectedItems => field ??= new SelectedItemList(this);

    /// <summary>The first selected item, or <see langword="null"/>.</summary>
    public ListViewItem? SelectedItem
    {
        get
        {
            var index = this.SelectedIndex;
            return index >= 0 ? this.Items[index] : null;
        }
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>The caret item keyboard navigation operates on, or -1 before any interaction.</summary>
    public int FocusedIndex => _focusedIndex;

    /// <summary>The index of the first visible flattened row (scroll position). Group header rows
    /// count as rows; in the icon views a row spans a whole rank of cells.</summary>
    public int TopIndex => _topIndex;

    /// <summary>Whether a label edit is currently in progress.</summary>
    public bool IsEditing => _editIndex >= 0;

    /// <summary>Raised once per gesture when the set of selected indices changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a Details column header is clicked.</summary>
    public event EventHandler<ColumnClickEventArgs>? ColumnClick;

    /// <summary>Raised before an item's check state flips; see <see cref="ItemCheckEventArgs"/>.</summary>
    public event EventHandler<ItemCheckEventArgs>? ItemCheck;

    /// <summary>Raised after an item's check state flipped.</summary>
    public event EventHandler<ItemCheckedEventArgs>? ItemChecked;

    /// <summary>Raised after a label edit finished; see <see cref="LabelEditEventArgs"/>.</summary>
    public event EventHandler<LabelEditEventArgs>? AfterLabelEdit;

    /// <summary>
    /// Replaces the rows from a model sequence (one-way binding convenience, the
    /// <see cref="ListBox.DataSource"/> parity for this control). Because a row here is a structured
    /// <see cref="ListViewItem"/> rather than a single display string, the mapping is a
    /// reflection-free item factory instead of a display selector. Each produced item whose
    /// <see cref="ListViewItem.Tag"/> the factory left <see langword="null"/> gets its source model
    /// stored there, so the selection maps back to the model. A <see langword="null"/> sequence
    /// just clears.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="items">The models to show, or <see langword="null"/> to clear.</param>
    /// <param name="itemFactory">Builds the row for one model.</param>
    public void SetDataSource<T>(IEnumerable<T>? items, Func<T, ListViewItem> itemFactory)
    {
        ArgumentNullException.ThrowIfNull(itemFactory);

        this.Items.Clear();
        if (items is null)
            return;

        foreach (var model in items)
        {
            var item = itemFactory(model);
            item.Tag ??= model;
            this.Items.Add(item);
        }
    }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>A running label edit claims Enter (commit) and Escape (cancel) ahead of the form.</summary>
    protected override bool IsInputKey(Keys keyData)
        => this.IsEditing && keyData is Keys.Enter or Keys.Escape;

    /// <summary>The pixel height reserved for the header row (0 unless Details with headers shown).</summary>
    protected int HeaderHeight => this.View == ListViewView.Details && this.ShowColumnHeaders ? this.ItemHeight : 0;

    /// <summary>The number of fully visible rows (of cells, in the icon views) in the item area.</summary>
    protected int VisibleRowCount => Math.Max(1, (this.Height - this.HeaderHeight) / this.CellSize.Height);

    /// <summary>Whether the current view lays cells out in a left-to-right grid.</summary>
    private bool IsGridView => this.View is ListViewView.LargeIcon or ListViewView.SmallIcon or ListViewView.Tile;

    /// <summary>Whether items are rendered under group headers right now. The List view never
    /// groups, matching <c>System.Windows.Forms.ListView</c>.</summary>
    private bool GroupingActive => this.ShowGroups && this.Groups.Count > 0 && this.View != ListViewView.List;

    /// <summary>The icon size of the large-icon views: the large image list's size, or 32×32.</summary>
    private Size LargeIconSize => this.LargeImageList?.ImageSize ?? new Size(32, 32);

    /// <summary>The icon size of the small-icon views: the small image list's size, or 16×16.</summary>
    private Size SmallIconSize => this.SmallImageList?.ImageSize ?? new Size(16, 16);

    /// <summary>
    /// The pixel size of one item cell in the current view. Details and List cells span the full
    /// width at <see cref="ItemHeight"/>; the icon views derive their cell from the icon size plus
    /// the label: LargeIcon centers the icon above one text line, SmallIcon and Tile put a fixed
    /// label block beside the icon (Tile two lines tall).
    /// </summary>
    private Size CellSize
    {
        get
        {
            switch (this.View)
            {
                case ListViewView.LargeIcon:
                {
                    var icon = this.LargeIconSize;
                    return new(Math.Max(icon.Width + (2 * _LargeIconCellPad), _MinLargeIconCellWidth), icon.Height + _LargeIconLabelGap + this.ItemHeight);
                }

                case ListViewView.SmallIcon:
                {
                    var icon = this.SmallIconSize;
                    return new(icon.Width + _IconGap + _SmallIconLabelWidth + (2 * _CellPad), this.ItemHeight);
                }

                case ListViewView.Tile:
                {
                    var icon = this.LargeIconSize;
                    return new(icon.Width + _IconGap + _TileLabelWidth + (2 * _CellPad), Math.Max(icon.Height + 4, 2 * this.ItemHeight));
                }

                default:
                    return new(this.Width, this.ItemHeight);
            }
        }
    }

    /// <summary>The number of cells per row: 1 outside the grid views.</summary>
    private int ItemsPerRow => this.IsGridView ? Math.Max(1, this.Width / this.CellSize.Width) : 1;

    /// <summary>The check-glyph indent of the inline (non-overlay) views, 0 while checks are off.</summary>
    private int CheckIndent => this.CheckBoxes ? GlyphRenderer.CheckBoxSize + _CheckGap : 0;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="ColumnClick"/>.</summary>
    protected virtual void OnColumnClick(ColumnClickEventArgs e) => this.ColumnClick?.Invoke(this, e);

    /// <summary>Raises <see cref="ItemCheck"/>.</summary>
    protected virtual void OnItemCheck(ItemCheckEventArgs e) => this.ItemCheck?.Invoke(this, e);

    /// <summary>Raises <see cref="ItemChecked"/>.</summary>
    protected virtual void OnItemChecked(ItemCheckedEventArgs e) => this.ItemChecked?.Invoke(this, e);

    /// <summary>Raises <see cref="AfterLabelEdit"/>.</summary>
    protected virtual void OnAfterLabelEdit(LabelEditEventArgs e) => this.AfterLabelEdit?.Invoke(this, e);

    // --- Flattened presentation ------------------------------------------------------------------
    //
    // Every view paints "flat rows": group header rows interleaved with runs of items (one item per
    // row in Details/List, up to ItemsPerRow in the grid views). While no grouping is active the
    // rows are pure arithmetic over the item indices — nothing is materialized, so huge ungrouped
    // lists cost nothing. With grouping, the display order and rows are flattened lazily into lists
    // (the TreeRowList idea, localized): structural changes only mark them dirty and the next access
    // rebuilds, never the paint loop itself.

    /// <summary>One visual row: a group header (<see cref="Count"/> &lt; 0) or a run of display positions.</summary>
    private readonly struct FlatRow(int groupIndex, int start, int count)
    {
        /// <summary>The header's index into <see cref="Groups"/>, or -1 for the default section.</summary>
        public readonly int GroupIndex = groupIndex;

        /// <summary>The first display position of an item row.</summary>
        public readonly int Start = start;

        /// <summary>The number of items in the row, or -1 for a header row.</summary>
        public readonly int Count = count;
    }

    /// <summary>Rebuilds the display order and flat rows when grouping is active and stale.</summary>
    private void EnsureFlat()
    {
        if (!_flatDirty)
            return;

        _flatDirty = false;
        if (!this.GroupingActive)
        {
            _displayItems = null;
            _flatRows = null;
            this.ClampScroll();
            return;
        }

        var display = _displayItems ??= [];
        var rows = _flatRows ??= [];
        display.Clear();
        rows.Clear();

        var groupCount = this.Groups.Count;
        var buckets = new List<int>?[groupCount + 1];
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var slot = this.IndexOfGroup(this.Items[i].Group);
            if (slot < 0)
                slot = groupCount;

            (buckets[slot] ??= []).Add(i);
        }

        var itemsPerRow = this.ItemsPerRow;
        for (var slot = 0; slot <= groupCount; ++slot)
        {
            var bucket = buckets[slot];
            if (bucket is null || bucket.Count == 0)
                continue;

            rows.Add(new FlatRow(slot == groupCount ? -1 : slot, 0, -1));
            var start = display.Count;
            display.AddRange(bucket);
            for (var offset = 0; offset < bucket.Count; offset += itemsPerRow)
                rows.Add(new FlatRow(-1, start + offset, Math.Min(itemsPerRow, bucket.Count - offset)));
        }

        this.ClampScroll();
    }

    /// <summary>The index of the group in <see cref="Groups"/>, or -1 (ungrouped/unlisted).</summary>
    private int IndexOfGroup(ListViewGroup? group)
    {
        if (group is null)
            return -1;

        for (var i = 0; i < this.Groups.Count; ++i)
            if (ReferenceEquals(this.Groups[i], group))
                return i;

        return -1;
    }

    /// <summary>The number of flat rows the current presentation occupies.</summary>
    private int FlatRowCount
    {
        get
        {
            this.EnsureFlat();
            if (_flatRows is { } rows)
                return rows.Count;

            var itemsPerRow = this.ItemsPerRow;
            return (this.Items.Count + itemsPerRow - 1) / itemsPerRow;
        }
    }

    /// <summary>The flat row at the given index.</summary>
    private FlatRow GetFlatRow(int rowIndex)
    {
        if (_flatRows is { } rows)
            return rows[rowIndex];

        var itemsPerRow = this.ItemsPerRow;
        var start = rowIndex * itemsPerRow;
        return new(-1, start, Math.Min(itemsPerRow, this.Items.Count - start));
    }

    /// <summary>The pixel height of a flat row: headers at <see cref="ItemHeight"/>, item rows at the cell height.</summary>
    private int RowHeightOf(in FlatRow row) => row.Count < 0 ? this.ItemHeight : this.CellSize.Height;

    /// <summary>The model item index at the given display position.</summary>
    private int DisplayItem(int position) => _displayItems is { } display ? display[position] : position;

    /// <summary>The display position of the given model item index.</summary>
    private int DisplayPosOf(int itemIndex)
    {
        this.EnsureFlat();
        return _displayItems is { } display ? display.IndexOf(itemIndex) : itemIndex;
    }

    /// <summary>The flat row containing the given display position, or -1.</summary>
    private int RowOfDisplayPos(int position)
    {
        this.EnsureFlat();
        if (position < 0)
            return -1;

        if (_flatRows is not { } rows)
            return position / this.ItemsPerRow;

        for (var i = 0; i < rows.Count; ++i)
        {
            var row = rows[i];
            if (row.Count > 0 && position >= row.Start && position < row.Start + row.Count)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The cell rectangle of the item at the given index, in client coordinates for the current
    /// scroll position (possibly outside the visible area). Details and List cells span the full
    /// control width.
    /// </summary>
    public Rectangle GetItemBounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, this.Items.Count);

        var position = this.DisplayPosOf(index);
        var row = this.RowOfDisplayPos(position);
        var y = this.HeaderHeight;
        if (row >= _topIndex)
            for (var r = _topIndex; r < row; ++r)
                y += this.RowHeightOf(this.GetFlatRow(r));
        else
            for (var r = row; r < _topIndex; ++r)
                y -= this.RowHeightOf(this.GetFlatRow(r));

        var flatRow = this.GetFlatRow(row);
        var cell = this.CellSize;
        return new((position - flatRow.Start) * cell.Width, y, cell.Width, this.RowHeightOf(flatRow));
    }

    /// <summary>Scrolls so the given item index is visible.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= this.Items.Count)
            return;

        var row = this.RowOfDisplayPos(this.DisplayPosOf(index));
        if (row < 0)
            return;

        if (row < _topIndex)
            _topIndex = row;
        else if (row >= _topIndex + this.VisibleRowCount)
            _topIndex = row - this.VisibleRowCount + 1;

        this.ClampScroll();
    }

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.FlatRowCount - this.VisibleRowCount);
        _topIndex = Math.Clamp(_topIndex, 0, maxTop);
    }

    /// <inheritdoc/>
    private protected override void OnBoundsChanged() => _flatDirty = true;

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        this.AbandonEdit();
    }

    /// <summary>Drops a pending label edit without committing or raising events — the edited item
    /// vanished (or the control unrealized), so there is nothing left to commit to.</summary>
    private void AbandonEdit()
    {
        _editIndex = -1;
        if (_labelEditor is { } editor)
            editor.Visible = false;
    }

    // --- Collection bookkeeping ------------------------------------------------------------------

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        var count = this.Items.Count;
        var changed = false;
        switch (e.ChangeType)
        {
            case ListChangeType.Added:
            {
                var item = this.Items[e.Index];
                item.Owner = this;
                item.SetSelectedCore(false);
                _attachedItems.Insert(e.Index, item);

                var pos = _selectedIndices.BinarySearch(e.Index);
                for (var i = pos >= 0 ? pos : ~pos; i < _selectedIndices.Count; ++i)
                    ++_selectedIndices[i];

                if (_focusedIndex >= e.Index)
                    ++_focusedIndex;
                if (_anchorIndex >= e.Index)
                    ++_anchorIndex;
                if (_editIndex >= e.Index)
                    ++_editIndex;
                break;
            }

            case ListChangeType.Removed:
            {
                var removed = _attachedItems[e.Index];
                removed.Owner = null;
                removed.SetSelectedCore(false);
                _attachedItems.RemoveAt(e.Index);

                var pos = _selectedIndices.BinarySearch(e.Index);
                var wasSelected = pos >= 0;
                if (wasSelected)
                {
                    _selectedIndices.RemoveAt(pos);
                    changed = true;
                }

                for (var i = wasSelected ? pos : ~pos; i < _selectedIndices.Count; ++i)
                    --_selectedIndices[i];

                if (_focusedIndex > e.Index)
                    --_focusedIndex;
                else if (_focusedIndex >= count)
                    _focusedIndex = count - 1;

                if (_anchorIndex > e.Index)
                    --_anchorIndex;
                else if (_anchorIndex >= count)
                    _anchorIndex = count - 1;

                if (_editIndex == e.Index)
                    this.AbandonEdit();
                else if (_editIndex > e.Index)
                    --_editIndex;
                break;
            }

            case ListChangeType.Replaced:
            {
                var old = _attachedItems[e.Index];
                old.Owner = null;
                old.SetSelectedCore(false);

                var item = this.Items[e.Index];
                item.Owner = this;
                item.SetSelectedCore(false);
                _attachedItems[e.Index] = item;

                var pos = _selectedIndices.BinarySearch(e.Index);
                if (pos >= 0)
                {
                    _selectedIndices.RemoveAt(pos);
                    changed = true;
                }

                break;
            }

            case ListChangeType.Reset:
            {
                // Covers Clear and the in-place Sort alike: re-attach whatever the list now holds
                // and rebuild the selection from the per-item flags, which survive a reorder.
                var oldCount = _selectedIndices.Count;
                for (var i = 0; i < _attachedItems.Count; ++i)
                    _attachedItems[i].Owner = null;

                _attachedItems.Clear();
                _selectedIndices.Clear();
                for (var i = 0; i < count; ++i)
                {
                    var item = this.Items[i];
                    item.Owner = this;
                    _attachedItems.Add(item);
                    if (item.Selected)
                        _selectedIndices.Add(i);
                }

                changed = _selectedIndices.Count != oldCount;
                if (_focusedIndex >= count)
                    _focusedIndex = count - 1;
                if (_anchorIndex >= count)
                    _anchorIndex = count - 1;
                if (_editIndex >= count)
                    this.AbandonEdit();
                break;
            }
        }

        _flatDirty = true;
        this.Invalidate();
        if (changed)
            this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    private void OnColumnsChanged(object? sender, ListChangedEventArgs e) => this.Invalidate();

    private void OnGroupsChanged(object? sender, ListChangedEventArgs e)
    {
        _flatDirty = true;
        this.Invalidate();
    }

    /// <summary>Called by an attached item after its <see cref="ListViewItem.Group"/> changed.</summary>
    internal void OnItemGroupChanged()
    {
        _flatDirty = true;
        this.Invalidate();
    }

    // --- Selection core: mutate the sorted index list, keep the item flags aligned ---------------

    /// <summary>Whether the row at the given index is selected.</summary>
    private bool IsSelected(int index) => _selectedIndices.BinarySearch(index) >= 0;

    private bool ClearSelectionCore()
    {
        if (_selectedIndices.Count == 0)
            return false;

        for (var i = 0; i < _selectedIndices.Count; ++i)
            this.Items[_selectedIndices[i]].SetSelectedCore(false);

        _selectedIndices.Clear();
        return true;
    }

    private bool SelectOnlyCore(int index)
    {
        if (_selectedIndices.Count == 1 && _selectedIndices[0] == index)
            return false;

        this.ClearSelectionCore();
        _selectedIndices.Add(index);
        this.Items[index].SetSelectedCore(true);
        return true;
    }

    private bool ToggleCore(int index)
    {
        var pos = _selectedIndices.BinarySearch(index);
        if (pos >= 0)
        {
            _selectedIndices.RemoveAt(pos);
            this.Items[index].SetSelectedCore(false);
        }
        else
        {
            _selectedIndices.Insert(~pos, index);
            this.Items[index].SetSelectedCore(true);
        }

        return true;
    }

    private bool SelectRangeCore(int from, int to)
    {
        var low = Math.Min(from, to);
        var high = Math.Max(from, to);
        if (_selectedIndices.Count == high - low + 1 && _selectedIndices[0] == low && _selectedIndices[^1] == high)
            return false; // sorted and contiguous, so endpoints + count identify the range

        this.ClearSelectionCore();
        for (var i = low; i <= high; ++i)
        {
            _selectedIndices.Add(i);
            this.Items[i].SetSelectedCore(true);
        }

        return true;
    }

    /// <summary>Ends a user gesture: one repaint and at most one <see cref="SelectedIndexChanged"/>.</summary>
    private void FinishSelectionGesture(bool changed)
    {
        if (!changed)
            return;

        this.Invalidate();
        this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>Applies an attached item's <see cref="ListViewItem.Selected"/> write to the selection.</summary>
    internal void SetItemSelected(ListViewItem item, bool value)
    {
        var index = this.Items.IndexOf(item);
        if (index < 0)
        {
            item.SetSelectedCore(value);
            return;
        }

        if (value)
        {
            _focusedIndex = index;
            _anchorIndex = index;
            if (!this.MultiSelect)
                this.FinishSelectionGesture(this.SelectOnlyCore(index));
            else if (!this.IsSelected(index))
                this.FinishSelectionGesture(this.ToggleCore(index));
        }
        else if (this.IsSelected(index))
            this.FinishSelectionGesture(this.ToggleCore(index));
    }

    // --- Checkboxes ------------------------------------------------------------------------------

    /// <summary>Runs an attached item's <see cref="ListViewItem.Checked"/> write through the vetoable
    /// <see cref="ItemCheck"/>/<see cref="ItemChecked"/> pipeline.</summary>
    internal void RequestItemCheck(ListViewItem item, bool value)
        => this.RequestItemCheckAt(this.Items.IndexOf(item), item, value);

    private void RequestItemCheckAt(int index, ListViewItem item, bool value)
    {
        var current = item.Checked;
        if (current == value)
            return;

        var args = new ItemCheckEventArgs(index, current, value);
        this.OnItemCheck(args);
        if (args.NewValue == current)
            return;

        item.SetCheckedCore(args.NewValue);
        this.Invalidate();
        this.OnItemChecked(new(item));
    }

    /// <summary>Flips the check state of every selected item (or of the caret item with no selection).</summary>
    private void ToggleSelectionChecks()
    {
        if (_selectedIndices.Count == 0)
        {
            if (_focusedIndex >= 0 && _focusedIndex < this.Items.Count)
                this.RequestItemCheckAt(_focusedIndex, this.Items[_focusedIndex], !this.Items[_focusedIndex].Checked);

            return;
        }

        for (var i = 0; i < _selectedIndices.Count; ++i)
        {
            var index = _selectedIndices[i];
            this.RequestItemCheckAt(index, this.Items[index], !this.Items[index].Checked);
        }
    }

    // --- Sorting ---------------------------------------------------------------------------------

    /// <summary>
    /// Sorts <see cref="Items"/> in place — stably — by <see cref="ItemSorter"/> when set, else by
    /// the active column's text in the <see cref="Sorting"/> direction; a no-op while neither is
    /// active. Selected items stay selected and the caret follows its item. See the class remarks
    /// for why this mutates the collection where <see cref="DataGridView"/> sorts by indirection.
    /// </summary>
    public void Sort()
    {
        var comparison = this.ItemSorter;
        if (comparison is null)
        {
            if (this.Sorting == SortOrder.None)
                return;

            var column = Math.Max(0, _sortColumn);
            var descending = this.Sorting == SortOrder.Descending;
            comparison = (a, b) =>
            {
                var result = string.CompareOrdinal(GetColumnText(a, column), GetColumnText(b, column));
                return descending ? -result : result;
            };
        }

        if (this.Items.Count > 1)
        {
            var focusedItem = _focusedIndex >= 0 && _focusedIndex < this.Items.Count ? this.Items[_focusedIndex] : null;
            var anchorItem = _anchorIndex >= 0 && _anchorIndex < this.Items.Count ? this.Items[_anchorIndex] : null;

            this.Items.Sort(comparison); // one Reset; the handler re-derives the selection from the item flags

            if (focusedItem is not null)
                _focusedIndex = this.Items.IndexOf(focusedItem);
            if (anchorItem is not null)
                _anchorIndex = this.Items.IndexOf(anchorItem);
        }

        this.Invalidate();
    }

    /// <summary>The text the given column shows for an item: the label, or the mapped sub-item.</summary>
    private static string GetColumnText(ListViewItem item, int column)
        => column <= 0 ? item.Text : column - 1 < item.SubItems.Count ? item.SubItems[column - 1] : string.Empty;

    /// <summary>Handles a click in the Details header band: <see cref="ColumnClick"/>, then the
    /// automatic sort when one is active (repeat clicks on the sorted column flip the direction).</summary>
    private void HandleHeaderClick(int x)
    {
        var columnIndex = -1;
        var left = 0;
        for (var c = 0; c < this.Columns.Count; ++c)
        {
            var width = this.Columns[c].Width;
            if (x >= left && x < left + width)
            {
                columnIndex = c;
                break;
            }

            left += width;
        }

        if (columnIndex < 0)
            return;

        this.OnColumnClick(new(columnIndex));

        var hasSorter = this.ItemSorter is not null;
        if (!hasSorter && this.Sorting == SortOrder.None)
            return;

        if (!hasSorter && columnIndex == _sortColumn)
        {
            this.Sorting = this.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            return;
        }

        _sortColumn = columnIndex;
        this.Sort();
    }

    // --- Label editing ---------------------------------------------------------------------------

    /// <summary>
    /// Starts editing the given item's label: a hosted native text box appears over the label,
    /// pre-filled and fully selected. See the class remarks for the commit points.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="LabelEdit"/> is disabled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    public void BeginEdit(int index)
    {
        if (!this.LabelEdit)
            throw new InvalidOperationException("LabelEdit must be enabled to edit item labels.");

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, this.Items.Count);

        this.EndEdit(cancel: false);
        this.EnsureVisible(index);

        var editor = _labelEditor;
        if (editor is null)
        {
            _labelEditor = editor = new() { Visible = false, TabStop = false };
            this.Controls.Add(editor);
        }

        _editIndex = index;
        var text = this.Items[index].Text;
        editor.Bounds = this.GetLabelBounds(index);
        editor.Text = text;
        editor.SelectionStart = 0;
        editor.SelectionLength = text.Length;
        editor.Visible = true;
        this.Invalidate();
    }

    /// <summary>
    /// Ends a pending label edit, committing the editor's text (or discarding it when
    /// <paramref name="cancel"/> is set), and raises <see cref="AfterLabelEdit"/> — whose handler
    /// may still veto the commit. A no-op while no edit is in progress.
    /// </summary>
    /// <param name="cancel">Whether to discard the entered text.</param>
    public void EndEdit(bool cancel)
    {
        var index = _editIndex;
        if (index < 0)
            return;

        _editIndex = -1;
        var editor = _labelEditor!;
        var text = editor.Text;
        editor.Visible = false;

        var args = new LabelEditEventArgs(index, cancel ? null : text);
        this.OnAfterLabelEdit(args);
        if (!cancel && !args.CancelEdit && index < this.Items.Count)
            this.Items[index].Text = text;

        this.Invalidate();
    }

    /// <summary>The rectangle the label (and its hosted editor) occupies inside the item's cell.</summary>
    private Rectangle GetLabelBounds(int index)
    {
        var cell = this.GetItemBounds(index);
        switch (this.View)
        {
            case ListViewView.LargeIcon:
            {
                var icon = this.LargeIconSize;
                return new(cell.X + _CellPad, cell.Y + icon.Height + _LargeIconLabelGap, cell.Width - (2 * _CellPad), this.ItemHeight);
            }

            case ListViewView.Tile:
            {
                var left = cell.X + _CellPad + this.LargeIconSize.Width + _IconGap;
                return new(left, cell.Y, Math.Max(0, cell.Right - left - _CellPad), cell.Height / 2);
            }

            default:
            {
                var item = this.Items[index];
                var left = cell.X + _CellPad + this.CheckIndent;
                if (this.GetIcon(item) is not null)
                    left += (this.View == ListViewView.SmallIcon ? this.SmallIconSize.Width : cell.Height - 4) + _IconGap;

                var right = this.View == ListViewView.Details && this.Columns.Count > 0 ? cell.X + this.Columns[0].Width : cell.Right;
                return new(left, cell.Y, Math.Max(0, right - left - _CellPad), cell.Height);
            }
        }
    }

    /// <summary>The icon to draw for an item: its explicit image, or its index into the view's image list.</summary>
    private IImage? GetIcon(ListViewItem item)
    {
        if (item.Image is { } image)
            return image;

        var list = this.View is ListViewView.LargeIcon or ListViewView.Tile ? this.LargeImageList : this.SmallImageList;
        var backend = this.Backend;
        if (list is null || backend is null)
            return null;

        var index = item.ImageIndex;
        return index >= 0 && index < list.Count ? list.GetImage(index, backend) : null;
    }

    // --- Input -----------------------------------------------------------------------------------

    /// <summary>Finds the item cell at the given client coordinates by walking the visible flat rows.</summary>
    private int HitTest(int x, int y, out Rectangle cellBounds)
    {
        cellBounds = default;
        if (x < 0 || x >= this.Width)
            return -1;

        var yAccum = this.HeaderHeight;
        if (y < yAccum)
            return -1;

        var rowCount = this.FlatRowCount;
        var height = this.Height;
        var cell = this.CellSize;
        var grid = this.IsGridView;
        for (var r = _topIndex; r < rowCount && yAccum < height; ++r)
        {
            var row = this.GetFlatRow(r);
            var rowHeight = this.RowHeightOf(row);
            if (y < yAccum + rowHeight)
            {
                if (row.Count < 0)
                    return -1; // group header row

                var column = grid ? x / cell.Width : 0;
                if (column >= row.Count)
                    return -1;

                cellBounds = new(column * cell.Width, yAccum, cell.Width, rowHeight);
                return this.DisplayItem(row.Start + column);
            }

            yAccum += rowHeight;
        }

        return -1;
    }

    /// <summary>Whether the given point hits the check glyph of the cell (overlay corner in the
    /// LargeIcon/Tile views, inline leading glyph elsewhere).</summary>
    private bool IsInCheckGlyph(int x, int y, Rectangle cellBounds)
    {
        if (this.View is ListViewView.LargeIcon or ListViewView.Tile)
            return x >= cellBounds.X + _CellPad && x < cellBounds.X + _CellPad + GlyphRenderer.CheckBoxSize
                && y >= cellBounds.Y + _CellPad && y < cellBounds.Y + _CellPad + GlyphRenderer.CheckBoxSize;

        return x >= cellBounds.X + _CellPad && x < cellBounds.X + _CellPad + GlyphRenderer.CheckBoxSize;
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        this.EndEdit(cancel: false); // a click anywhere is a commit point for a pending label edit

        if (this.View == ListViewView.Details && this.ShowColumnHeaders && e.Y < this.HeaderHeight)
        {
            this.HandleHeaderClick(e.X);
            return;
        }

        var index = this.HitTest(e.X, e.Y, out var cellBounds);
        if (index < 0)
            return;

        if (this.CheckBoxes && this.IsInCheckGlyph(e.X, e.Y, cellBounds))
        {
            this.RequestItemCheckAt(index, this.Items[index], !this.Items[index].Checked);
            return;
        }

        _focusedIndex = index;
        if (this.MultiSelect && e.Shift)
        {
            if (_anchorIndex < 0)
                _anchorIndex = index;

            this.FinishSelectionGesture(this.SelectRangeCore(_anchorIndex, index));
            return;
        }

        _anchorIndex = index;
        this.FinishSelectionGesture(this.MultiSelect && e.Control ? this.ToggleCore(index) : this.SelectOnlyCore(index));
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _topIndex -= Math.Sign(e.Delta) * 3;
        this.ClampScroll();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editIndex >= 0)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.EndEdit(cancel: false);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.EndEdit(cancel: true);
                e.Handled = true;
            }

            return;
        }

        var count = this.Items.Count;
        if (e.KeyCode == Keys.Space)
        {
            if (this.CheckBoxes)
            {
                this.ToggleSelectionChecks();
                e.Handled = true;
            }
            else if (this.MultiSelect && _focusedIndex >= 0 && _focusedIndex < count)
            {
                _anchorIndex = _focusedIndex;
                this.FinishSelectionGesture(this.ToggleCore(_focusedIndex));
                e.Handled = true;
            }

            return;
        }

        if (e.KeyCode == Keys.F2 && this.LabelEdit && _focusedIndex >= 0 && _focusedIndex < count)
        {
            this.BeginEdit(_focusedIndex);
            e.Handled = true;
            return;
        }

        this.EnsureFlat(); // navigation maps through the display order
        var grid = this.IsGridView;
        var columns = this.ItemsPerRow;
        var position = _focusedIndex >= 0 ? this.DisplayPosOf(_focusedIndex) : -1;
        int target;
        switch (e.KeyCode)
        {
            case Keys.Down: target = position < 0 ? 0 : Math.Min(count - 1, position + columns); break;
            case Keys.Up: target = position < 0 ? 0 : Math.Max(0, position - columns); break;
            case Keys.Right when grid: target = position < 0 ? 0 : Math.Min(count - 1, position + 1); break;
            case Keys.Left when grid: target = position < 0 ? 0 : Math.Max(0, position - 1); break;
            case Keys.Home: target = 0; break;
            case Keys.End: target = count - 1; break;
            case Keys.PageDown: target = position < 0 ? 0 : Math.Min(count - 1, position + (columns * this.VisibleRowCount)); break;
            case Keys.PageUp: target = position < 0 ? 0 : Math.Max(0, position - (columns * this.VisibleRowCount)); break;
            default: return;
        }

        e.Handled = true;
        if (target < 0 || target >= count)
            return;

        var itemIndex = this.DisplayItem(target);
        _focusedIndex = itemIndex;
        this.EnsureVisible(itemIndex);
        if (this.MultiSelect && e.Shift)
        {
            if (_anchorIndex < 0)
                _anchorIndex = itemIndex;

            this.FinishSelectionGesture(this.SelectRangeCore(_anchorIndex, itemIndex));
            return;
        }

        _anchorIndex = itemIndex;
        this.FinishSelectionGesture(this.SelectOnlyCore(itemIndex));
    }

    // --- Painting --------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        var headerHeight = this.HeaderHeight;
        if (headerHeight > 0)
        {
            HeaderRowPainter.Draw(g, theme, this.Columns, width, headerHeight);
            this.PaintSortArrow(g, theme, headerHeight);
        }

        var rowCount = this.FlatRowCount;
        var cell = this.CellSize;
        var y = headerHeight;
        for (var r = _topIndex; r < rowCount && y < height; ++r)
        {
            var row = this.GetFlatRow(r);
            var rowHeight = this.RowHeightOf(row);
            if (row.Count < 0)
                this.PaintGroupHeader(g, theme, row.GroupIndex, y, rowHeight);
            else
                for (var k = 0; k < row.Count; ++k)
                    this.PaintItem(g, theme, this.DisplayItem(row.Start + k), k * cell.Width, y, cell.Width, rowHeight);

            y += rowHeight;
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <summary>Draws the themed direction triangle on the active sort column's header.</summary>
    private void PaintSortArrow(IGraphics g, ITheme theme, int headerHeight)
    {
        if (_sortColumn < 0 || _sortColumn >= this.Columns.Count)
            return;

        if (this.ItemSorter is null && this.Sorting == SortOrder.None)
            return;

        var left = 0;
        for (var c = 0; c < _sortColumn; ++c)
            left += this.Columns[c].Width;

        var ascending = this.ItemSorter is not null || this.Sorting == SortOrder.Ascending;
        GlyphRenderer.DrawSortArrow(g, theme.HeaderText, new Rectangle(left + this.Columns[_sortColumn].Width - 14, 0, _SortArrowWidth, headerHeight), ascending);
    }

    /// <summary>Draws a group's header row: accent caption plus the accent separator rule.</summary>
    private void PaintGroupHeader(IGraphics g, ITheme theme, int groupIndex, int y, int rowHeight)
    {
        var text = groupIndex >= 0 ? this.Groups[groupIndex].Header : _DefaultGroupHeader;
        g.DrawText(text, theme.DefaultFont, theme.Accent, new Rectangle(_CellPad + 2, y, this.Width - 8, rowHeight), ContentAlignment.MiddleLeft);
        g.DrawLine(theme.Accent, _CellPad, y + rowHeight - 2, this.Width - _CellPad, y + rowHeight - 2);
    }

    /// <summary>Draws one item cell in the current view.</summary>
    private void PaintItem(IGraphics g, ITheme theme, int index, int cellX, int y, int cellWidth, int rowHeight)
    {
        var item = this.Items[index];
        var selected = this.IsSelected(index);
        switch (this.View)
        {
            case ListViewView.Details:
                this.PaintDetailsRow(g, theme, item, selected, y, rowHeight);
                break;

            case ListViewView.LargeIcon:
                this.PaintLargeIconCell(g, theme, item, selected, cellX, y, cellWidth, rowHeight);
                break;

            case ListViewView.Tile:
                this.PaintTileCell(g, theme, item, selected, cellX, y, cellWidth, rowHeight);
                break;

            default: // List and SmallIcon: one leading icon plus label
                this.PaintListCell(g, theme, item, selected, cellX, y, cellWidth, rowHeight);
                break;
        }
    }

    private void PaintDetailsRow(IGraphics g, ITheme theme, ListViewItem item, bool selected, int y, int rowHeight)
    {
        if (selected)
        {
            var selWidth = this.FullRowSelect || this.Columns.Count == 0 ? this.Width : this.Columns[0].Width;
            g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, selWidth, rowHeight));
        }

        var textColor = selected ? theme.SelectionText : theme.ControlText;
        if (this.Columns.Count == 0)
        {
            this.PaintPrimaryCell(g, theme, item, textColor, 0, y, this.Width, rowHeight, ContentAlignment.MiddleLeft);
            return;
        }

        var x = 0;
        for (var c = 0; c < this.Columns.Count; ++c)
        {
            var col = this.Columns[c];
            g.PushClip(new Rectangle(x, y, col.Width, rowHeight));
            if (c == 0)
                this.PaintPrimaryCell(g, theme, item, textColor, x, y, col.Width, rowHeight, col.TextAlign);
            else
            {
                var subIndex = c - 1;
                var text = subIndex < item.SubItems.Count ? item.SubItems[subIndex] : string.Empty;
                var textRect = new Rectangle(x + _CellPad, y, col.Width - (2 * _CellPad), rowHeight);
                g.DrawText(text, theme.DefaultFont, textColor, textRect, col.TextAlign);
            }

            g.PopClip();
            x += col.Width;
        }
    }

    /// <summary>Paints the leading cell shared by Details and List: check glyph, icon, then the label.</summary>
    private void PaintPrimaryCell(
        IGraphics g,
        ITheme theme,
        ListViewItem item,
        Color textColor,
        int x,
        int y,
        int width,
        int rowHeight,
        ContentAlignment alignment)
    {
        var left = x + _CellPad;
        if (this.CheckBoxes)
        {
            var boxTop = y + Math.Max(0, (rowHeight - GlyphRenderer.CheckBoxSize) / 2);
            GlyphRenderer.DrawCheckBox(g, theme, new(left, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), item.Checked);
            left += GlyphRenderer.CheckBoxSize + _CheckGap;
        }

        if (this.GetIcon(item) is { } icon)
        {
            var iconSize = rowHeight - 4;
            g.DrawImage(icon, new Rectangle(left, y + 2, iconSize, iconSize));
            left += iconSize + _IconGap;
        }

        var textRect = new Rectangle(left, y, x + width - left - _CellPad, rowHeight);
        g.DrawText(item.Text, theme.DefaultFont, textColor, textRect, alignment);
    }

    /// <summary>Paints a List or SmallIcon cell: selection fill, check glyph, icon and label.</summary>
    private void PaintListCell(IGraphics g, ITheme theme, ListViewItem item, bool selected, int cellX, int y, int cellWidth, int rowHeight)
    {
        var smallIcon = this.View == ListViewView.SmallIcon;
        if (selected)
            g.FillRectangle(theme.SelectionBackground, new Rectangle(cellX, y, smallIcon ? cellWidth : this.Width, rowHeight));

        if (smallIcon)
        {
            g.PushClip(new Rectangle(cellX, y, cellWidth, rowHeight));
            var left = cellX + _CellPad;
            if (this.CheckBoxes)
            {
                var boxTop = y + Math.Max(0, (rowHeight - GlyphRenderer.CheckBoxSize) / 2);
                GlyphRenderer.DrawCheckBox(g, theme, new(left, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), item.Checked);
                left += GlyphRenderer.CheckBoxSize + _CheckGap;
            }

            var iconSize = this.SmallIconSize;
            if (this.GetIcon(item) is { } icon)
            {
                g.DrawImage(icon, new Rectangle(left, y + Math.Max(0, (rowHeight - iconSize.Height) / 2), iconSize.Width, iconSize.Height));
                left += iconSize.Width + _IconGap;
            }

            var textColor = selected ? theme.SelectionText : theme.ControlText;
            g.DrawText(item.Text, theme.DefaultFont, textColor, new Rectangle(left, y, cellX + cellWidth - left - _CellPad, rowHeight), ContentAlignment.MiddleLeft);
            g.PopClip();
            return;
        }

        this.PaintPrimaryCell(g, theme, item, selected ? theme.SelectionText : theme.ControlText, cellX, y, this.Width, rowHeight, ContentAlignment.MiddleLeft);
    }

    /// <summary>Paints a LargeIcon cell: icon centered above the (clipped) centered label, check overlay top-left.</summary>
    private void PaintLargeIconCell(IGraphics g, ITheme theme, ListViewItem item, bool selected, int cellX, int y, int cellWidth, int rowHeight)
    {
        if (selected)
            g.FillRectangle(theme.SelectionBackground, new Rectangle(cellX, y, cellWidth, rowHeight));

        var iconSize = this.LargeIconSize;
        if (this.GetIcon(item) is { } icon)
            g.DrawImage(icon, new Rectangle(cellX + ((cellWidth - iconSize.Width) / 2), y + 2, iconSize.Width, iconSize.Height));

        var labelRect = new Rectangle(cellX + _CellPad, y + iconSize.Height + _LargeIconLabelGap, cellWidth - (2 * _CellPad), this.ItemHeight);
        g.PushClip(labelRect);
        g.DrawText(item.Text, theme.DefaultFont, selected ? theme.SelectionText : theme.ControlText, labelRect, ContentAlignment.MiddleCenter);
        g.PopClip();

        if (this.CheckBoxes)
            GlyphRenderer.DrawCheckBox(g, theme, new(cellX + _CellPad, y + _CellPad, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), item.Checked);
    }

    /// <summary>Paints a Tile cell: icon at the left, the label above the greyed first sub-item, check overlay top-left.</summary>
    private void PaintTileCell(IGraphics g, ITheme theme, ListViewItem item, bool selected, int cellX, int y, int cellWidth, int rowHeight)
    {
        if (selected)
            g.FillRectangle(theme.SelectionBackground, new Rectangle(cellX, y, cellWidth, rowHeight));

        var iconSize = this.LargeIconSize;
        if (this.GetIcon(item) is { } icon)
            g.DrawImage(icon, new Rectangle(cellX + _CellPad, y + Math.Max(0, (rowHeight - iconSize.Height) / 2), iconSize.Width, iconSize.Height));

        var left = cellX + _CellPad + iconSize.Width + _IconGap;
        var labelWidth = Math.Max(0, cellX + cellWidth - left - _CellPad);
        var lineHeight = rowHeight / 2;
        g.PushClip(new Rectangle(left, y, labelWidth, rowHeight));
        g.DrawText(item.Text, theme.DefaultFont, selected ? theme.SelectionText : theme.ControlText, new Rectangle(left, y, labelWidth, lineHeight), ContentAlignment.MiddleLeft);
        if (item.SubItems.Count > 0)
            g.DrawText(item.SubItems[0], theme.DefaultFont, selected ? theme.SelectionText : theme.DisabledText, new Rectangle(left, y + lineHeight, labelWidth, rowHeight - lineHeight), ContentAlignment.MiddleLeft);

        g.PopClip();

        if (this.CheckBoxes)
            GlyphRenderer.DrawCheckBox(g, theme, new(cellX + _CellPad, y + _CellPad, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), item.Checked);
    }

    /// <summary>A live, allocation-free mapping of the selected indices onto their items.</summary>
    private sealed class SelectedItemList(ListView owner) : IReadOnlyList<ListViewItem>
    {
        public int Count => owner._selectedIndices.Count;

        public ListViewItem this[int index] => owner.Items[owner._selectedIndices[index]];

        public IEnumerator<ListViewItem> GetEnumerator()
        {
            for (var i = 0; i < owner._selectedIndices.Count; ++i)
                yield return owner.Items[owner._selectedIndices[i]];
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
