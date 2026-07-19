using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn list painted in the native theme, with optional per-item icons, wheel/keyboard
/// scrolling and the full set of WinForms selection modes (<see cref="SelectionMode"/>). Items are
/// arbitrary objects; their text and icon are produced by reflection-free selector delegates, so
/// binding stays trim/AOT-safe.
/// </summary>
public class ListBox : OwnerDrawnControl
{
    private const int _IconGap = 4;

    /// <summary>The selected row indices, always kept sorted ascending.</summary>
    private readonly List<int> _selectedIndices = [];

    private int _focusedIndex = -1;
    private int _anchorIndex = -1;
    private int _topIndex;
    private int? _itemHeight;

    /// <summary>Creates a list box.</summary>
    public ListBox()
    {
        this.Items = new();
        this.Items.ListChanged += this.OnItemsListChanged;
    }

    /// <summary>The items shown. Mutating this collection repaints the control.</summary>
    public ObservableList<object?> Items { get; }

    /// <summary>Produces the display text for an item. Defaults to <c>ToString()</c>.</summary>
    public Func<object?, string> DisplaySelector
    {
        get => field;
        set
        {
            field = value ?? (static item => item?.ToString() ?? string.Empty);
            this.Invalidate();
        }
    } = static item => item?.ToString() ?? string.Empty;

    /// <summary>Optional selector producing an icon for an item; <see langword="null"/> for none.</summary>
    public Func<object?, IImage?>? ImageSelector { get; set; }

    /// <summary>The pixel height of a row. Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            this.Invalidate();
        }
    }

    /// <summary>How the user selects items. Changing the mode clears the selection.</summary>
    public SelectionMode SelectionMode
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.FinishSelectionGesture(this.ClearSelectionCore());
        }
    } = SelectionMode.One;

    /// <summary>
    /// The first selected index, or -1 for none. Setting it replaces the whole selection with the
    /// one item (in <see cref="SelectionMode.None"/> it only moves the caret).
    /// </summary>
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

            if (this.SelectionMode == SelectionMode.None)
                return;

            this.FinishSelectionGesture(clamped < 0 ? this.ClearSelectionCore() : this.SelectOnlyCore(clamped));
        }
    }

    /// <summary>The selected row indices, sorted ascending. Empty for none.</summary>
    public IReadOnlyList<int> SelectedIndices => _selectedIndices;

    /// <summary>The selected items, in index order. A live view over <see cref="SelectedIndices"/>.</summary>
    public IReadOnlyList<object?> SelectedItems => field ??= new SelectedItemList(this);

    /// <summary>The first selected item, or <see langword="null"/>.</summary>
    public object? SelectedItem
    {
        get
        {
            var index = this.SelectedIndex;
            return index >= 0 ? this.Items[index] : null;
        }
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>The caret row keyboard navigation operates on — independent of the selection in the
    /// multi modes — or -1 before any interaction.</summary>
    public int FocusedIndex => _focusedIndex;

    /// <summary>The index of the first visible row (scroll position).</summary>
    public int TopIndex => _topIndex;

    /// <summary>Raised once per gesture when the set of selected indices changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Replaces the items from any sequence (one-way binding convenience).</summary>
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

    /// <summary>The number of fully visible rows.</summary>
    protected int VisibleRowCount => Math.Max(1, this.Height / this.ItemHeight);

    /// <summary>Whether the row at the given index is selected.</summary>
    public bool GetSelected(int index) => _selectedIndices.BinarySearch(index) >= 0;

    /// <summary>The index of the row at the given client coordinates, or -1 for none.</summary>
    public int IndexFromPoint(int x, int y)
    {
        if (x < 0 || x >= this.Width || y < 0 || y >= this.Height)
            return -1;

        var row = _topIndex + (y / this.ItemHeight);
        return row >= 0 && row < this.Items.Count ? row : -1;
    }

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>
    /// Reacts to a mutation of <see cref="Items"/>: keeps the selection, caret and anchor pointing at
    /// the same items (pruning what vanished), clamps the scroll position and repaints. Subclasses
    /// override to keep parallel per-item state aligned, then call the base.
    /// </summary>
    protected virtual void OnItemsChanged(ListChangedEventArgs e)
    {
        var count = this.Items.Count;
        var changed = false;
        switch (e.ChangeType)
        {
            case ListChangeType.Added:
            {
                var pos = _selectedIndices.BinarySearch(e.Index);
                for (var i = pos >= 0 ? pos : ~pos; i < _selectedIndices.Count; ++i)
                    ++_selectedIndices[i];

                if (_focusedIndex >= e.Index)
                    ++_focusedIndex;
                if (_anchorIndex >= e.Index)
                    ++_anchorIndex;
                break;
            }

            case ListChangeType.Removed:
            {
                var pos = _selectedIndices.BinarySearch(e.Index);
                var wasSelected = pos >= 0;
                if (wasSelected)
                {
                    _selectedIndices.RemoveAt(pos);
                    changed = true;
                }

                for (var i = wasSelected ? pos : ~pos; i < _selectedIndices.Count; ++i)
                    --_selectedIndices[i];

                // Single-selection keeps a row selected, like the classic control: the neighbor
                // takes over when the selected row vanishes.
                if (wasSelected && this.SelectionMode == SelectionMode.One && count > 0)
                    _selectedIndices.Add(Math.Min(e.Index, count - 1));

                if (_focusedIndex > e.Index)
                    --_focusedIndex;
                else if (_focusedIndex >= count)
                    _focusedIndex = count - 1;

                if (_anchorIndex > e.Index)
                    --_anchorIndex;
                else if (_anchorIndex >= count)
                    _anchorIndex = count - 1;
                break;
            }

            case ListChangeType.Reset:
            {
                while (_selectedIndices.Count > 0 && _selectedIndices[^1] >= count)
                {
                    _selectedIndices.RemoveAt(_selectedIndices.Count - 1);
                    changed = true;
                }

                if (_focusedIndex >= count)
                    _focusedIndex = count - 1;
                if (_anchorIndex >= count)
                    _anchorIndex = count - 1;
                break;
            }
        }

        this.ClampScroll();
        this.Invalidate();
        if (changed)
            this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    private void OnItemsListChanged(object? sender, ListChangedEventArgs e) => this.OnItemsChanged(e);

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.Items.Count - this.VisibleRowCount);
        _topIndex = Math.Clamp(_topIndex, 0, maxTop);
    }

    /// <summary>Scrolls so the given index is visible.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0)
            return;

        if (index < _topIndex)
            _topIndex = index;
        else if (index >= _topIndex + this.VisibleRowCount)
            _topIndex = index - this.VisibleRowCount + 1;

        this.ClampScroll();
    }

    // --- Selection core: mutate the sorted index list, report whether anything changed ----------

    private bool ClearSelectionCore()
    {
        if (_selectedIndices.Count == 0)
            return false;

        _selectedIndices.Clear();
        return true;
    }

    private bool SelectOnlyCore(int index)
    {
        if (_selectedIndices.Count == 1 && _selectedIndices[0] == index)
            return false;

        _selectedIndices.Clear();
        _selectedIndices.Add(index);
        return true;
    }

    private bool ToggleCore(int index)
    {
        var pos = _selectedIndices.BinarySearch(index);
        if (pos >= 0)
            _selectedIndices.RemoveAt(pos);
        else
            _selectedIndices.Insert(~pos, index);

        return true;
    }

    private bool SelectRangeCore(int from, int to)
    {
        var low = Math.Min(from, to);
        var high = Math.Max(from, to);
        if (_selectedIndices.Count == high - low + 1 && _selectedIndices[0] == low && _selectedIndices[^1] == high)
            return false; // sorted and contiguous, so endpoints + count identify the range

        _selectedIndices.Clear();
        for (var i = low; i <= high; ++i)
            _selectedIndices.Add(i);

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

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var row = this.IndexFromPoint(e.X, e.Y);
        if (row < 0)
            return;

        _focusedIndex = row;
        switch (this.SelectionMode)
        {
            case SelectionMode.None:
                this.Invalidate();
                break;

            case SelectionMode.One:
                _anchorIndex = row;
                this.FinishSelectionGesture(this.SelectOnlyCore(row));
                break;

            case SelectionMode.MultiSimple:
                _anchorIndex = row;
                this.FinishSelectionGesture(this.ToggleCore(row));
                break;

            case SelectionMode.MultiExtended when e.Shift:
                if (_anchorIndex < 0)
                    _anchorIndex = row;

                this.FinishSelectionGesture(this.SelectRangeCore(_anchorIndex, row));
                break;

            case SelectionMode.MultiExtended when e.Control:
                _anchorIndex = row;
                this.FinishSelectionGesture(this.ToggleCore(row));
                break;

            case SelectionMode.MultiExtended:
                _anchorIndex = row;
                this.FinishSelectionGesture(this.SelectOnlyCore(row));
                break;
        }
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
        var mode = this.SelectionMode;
        var count = this.Items.Count;

        if (e.KeyCode == Keys.Space && mode is SelectionMode.MultiSimple or SelectionMode.MultiExtended)
        {
            if (_focusedIndex >= 0 && _focusedIndex < count)
            {
                _anchorIndex = _focusedIndex;
                this.FinishSelectionGesture(this.ToggleCore(_focusedIndex));
            }

            e.Handled = true;
            return;
        }

        var target = e.KeyCode switch
        {
            Keys.Down => Math.Min(count - 1, _focusedIndex + 1),
            Keys.Up => Math.Max(0, _focusedIndex - 1),
            Keys.Home => 0,
            Keys.End => count - 1,
            Keys.PageDown => Math.Min(count - 1, _focusedIndex + this.VisibleRowCount),
            Keys.PageUp => Math.Max(0, _focusedIndex - this.VisibleRowCount),
            _ => -2,
        };
        if (target == -2)
            return;

        e.Handled = true;
        if (target < 0 || target >= count)
            return;

        _focusedIndex = target;
        this.EnsureVisible(target);
        switch (mode)
        {
            case SelectionMode.None:
            case SelectionMode.MultiSimple:
                this.Invalidate(); // the caret moved; the selection stays put
                break;

            case SelectionMode.One:
                _anchorIndex = target;
                this.FinishSelectionGesture(this.SelectOnlyCore(target));
                break;

            case SelectionMode.MultiExtended when e.Shift:
                if (_anchorIndex < 0)
                    _anchorIndex = target;

                this.FinishSelectionGesture(this.SelectRangeCore(_anchorIndex, target));
                break;

            case SelectionMode.MultiExtended:
                _anchorIndex = target;
                this.FinishSelectionGesture(this.SelectOnlyCore(target));
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, this.Height));

        var rowHeight = this.ItemHeight;
        var last = Math.Min(this.Items.Count, _topIndex + this.VisibleRowCount + 1);
        for (var i = _topIndex; i < last; ++i)
        {
            var y = (i - _topIndex) * rowHeight;
            var rowRect = new Rectangle(0, y, this.Width, rowHeight);
            var selected = this.GetSelected(i);
            if (selected)
                g.FillRectangle(theme.SelectionBackground, rowRect);

            this.OnDrawRow(g, i, rowRect, selected);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>
    /// Draws one row's content (icon and text) inside the given bounds; the selection highlight is
    /// already painted. Subclasses override to add leading adornments and delegate to the base with
    /// the remaining, right-shifted bounds.
    /// </summary>
    protected virtual void OnDrawRow(IGraphics g, int index, Rectangle bounds, bool selected)
        => DrawRowContent(g, this.Theme, bounds, this.DisplaySelector(this.Items[index]), this.ImageSelector?.Invoke(this.Items[index]), selected);

    /// <summary>
    /// Paints the icon-plus-text body of one list row — the single row renderer every list-shaped
    /// surface shares (list box rows, combo drop-down rows), so they stay pixel-identical.
    /// </summary>
    internal static void DrawRowContent(IGraphics g, ITheme theme, Rectangle bounds, string text, IImage? icon, bool selected)
    {
        var textLeft = bounds.X + 2;
        if (icon is not null)
        {
            var iconSize = bounds.Height - 4;
            g.DrawImage(icon, new Rectangle(textLeft, bounds.Y + 2, iconSize, iconSize));
            textLeft += iconSize + _IconGap;
        }

        var textColor = selected ? theme.SelectionText : theme.ControlText;
        var textRect = new Rectangle(textLeft, bounds.Y, bounds.Right - textLeft, bounds.Height);
        g.DrawText(text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
    }

    /// <summary>A live, allocation-free mapping of the selected indices onto their items.</summary>
    private sealed class SelectedItemList(ListBox owner) : IReadOnlyList<object?>
    {
        public int Count => owner._selectedIndices.Count;

        public object? this[int index] => owner.Items[owner._selectedIndices[index]];

        public IEnumerator<object?> GetEnumerator()
        {
            for (var i = 0; i < owner._selectedIndices.Count; ++i)
                yield return owner.Items[owner._selectedIndices[i]];
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
