using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
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
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.List;

    private const int _IconGap = 4;

    /// <summary>Painted only while there are more rows than fit; null-object until then.</summary>
    private readonly RowScrollBar _scrollBar = new();

    /// <summary>The selected row indices, always kept sorted ascending.</summary>
    private readonly List<int> _selectedIndices = [];

    private int _focusedIndex = -1;
    private int _anchorIndex = -1;
    private int _topIndex;
    private int? _itemHeight;

    /// <summary>An unset <see cref="Control.BackColor"/> resolves to the theme's field background —
    /// the list is an editable-field surface, like the WinForms per-control default.</summary>
    private protected override Color FallbackBackColor => this.Theme.FieldBackground;

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
    public Func<object?, IImage?>? ImageSelector
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ReconsiderPromotion(); // per-item icons are what a stock list cannot show
            this.Invalidate();
        }
    }

    /// <summary>The pixel height of a row. Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            this.ReconsiderPromotion(); // a stock list lays rows out at its own height
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
            this.ReconsiderPromotion(); // only the single-selection mode maps onto a stock list
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
    /// <remarks>A promoted list has one selection and one caret, and the platform keeps them together.</remarks>
    public int FocusedIndex => _native is null ? _focusedIndex : this.SelectedIndex;

    /// <summary>The index of the first visible row (scroll position).</summary>
    public int TopIndex => _native is { } peer ? peer.GetTopIndex() : _topIndex;

    private IListBoxPeer? _native;
    private bool? _nativeOffered;


    /// <summary>Whether this list is currently rendered by a real platform widget.</summary>
    public override bool IsNativeWidget => _native is not null;

    /// <summary>
    /// Whether the current property values are all expressible by a platform list. A stock list shows
    /// rows of plain text at its own row height and carries one selection, so per-item icons, a custom
    /// <see cref="ItemHeight"/> and every multi-selection mode keep the painter.
    /// </summary>
    /// <remarks>
    /// A subclass that paints anything of its own into a row — a check box, a badge — must override this
    /// to <see langword="false"/>: a platform list has no idea the extra content exists and would drop it
    /// silently. <see cref="CheckedListBox"/> is the one in this library.
    /// </remarks>
    private protected virtual bool IsNativeEligible
        => this.SelectionMode == SelectionMode.One
        && this.ImageSelector is null
        && _itemHeight is null;

    /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
    private bool WouldBeNative
        => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible && (_nativeOffered ?? true);

    /// <inheritdoc/>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible)
        {
            var offered = backend.CreateListBox();
            _nativeOffered = offered is not null;
            if (offered is { } peer)
            {
                _native = peer;
                this.PushNativeItems(peer);
                peer.SelectionChanged += this.OnNativeSelectionChanged;
                peer.ItemActivated += this.OnNativeItemActivated;
                return peer;
            }
        }

        return base.CreatePeer(backend);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        if (_native is { } peer)
        {
            peer.SelectionChanged -= this.OnNativeSelectionChanged;
            peer.ItemActivated -= this.OnNativeItemActivated;
            _native = null;
        }

        base.OnUnrealized();
    }

    /// <summary>Re-realizes the control when a property change crossed the eligibility line.</summary>
    private void ReconsiderPromotion()
    {
        if (this.IsNativeWidget != this.WouldBeNative)
            this.RerealizePeer();
    }

    /// <summary>Renders every item through <see cref="DisplaySelector"/> and hands the list over whole.</summary>
    private void PushNativeItems(IListBoxPeer peer)
    {
        var count = this.Items.Count;
        var texts = count == 0 ? [] : new string[count];
        for (var i = 0; i < count; ++i)
            texts[i] = this.DisplaySelector(this.Items[i]) ?? string.Empty;

        peer.SetItems(texts, this.SelectedIndex);
    }

    /// <summary>The widget's selection moved; mirror it through the same path a click takes.</summary>
    private void OnNativeSelectionChanged(object? sender, EventArgs e)
    {
        if (_native is not { } peer)
            return;

        var index = peer.GetSelectedIndex();
        this.FinishSelectionGesture(index < 0 ? this.ClearSelectionCore() : this.SelectOnlyCore(index));
    }

    /// <summary>
    /// A row was double-clicked or activated with Enter. Both platforms fold those into one activation,
    /// and the owner-drawn list reports the same thing as a double click, so that is what is raised.
    /// </summary>
    private void OnNativeItemActivated(object? sender, EventArgs e)
        => this.RaiseMouseDoubleClick(new(MouseButtons.Left, 2, 0, 0, 0));

    /// <summary>Raised once per gesture when the set of selected indices changes.</summary>
    public event EventHandler? SelectedIndexChanged;


    /// <summary>
    /// Replaces the items from a sequence and resolves <paramref name="displayMember"/> to an accessor at
    /// compile time, so the Windows Forms shape — a data source plus a member <em>name</em> — works
    /// without reflection.
    /// </summary>
    /// <remarks>
    /// The name goes through the lookup the <c>[Bindable]</c> generator emitted on <typeparamref name="T"/>.
    /// A name the type does not have throws here, at the call, rather than yielding blank rows later.
    /// </remarks>
    /// <typeparam name="T">The item type, which must carry <c>[Bindable]</c>.</typeparam>
    /// <param name="items">The items to show.</param>
    /// <param name="displayMember">The property whose value is displayed, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">The named member is not a public readable property of <typeparamref name="T"/>.</exception>
    public void SetDataSource<T>(IEnumerable<T> items, string? displayMember = null)
        where T : IBindableMembers
    {
        ArgumentNullException.ThrowIfNull(items);

        if (displayMember is not null)
        {
            var accessor = BindableMembers.Require<T>(displayMember, nameof(displayMember));
            this.DisplaySelector = item => accessor(item)?.ToString() ?? string.Empty;
        }

        this.Items.Clear();
        foreach (var item in items)
            this.Items.Add(item);
    }

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

    /// <summary>Whether the list is showing a scrollbar of its own, which only an unpromoted one does.</summary>
    private bool HasScrollBar => _native is null && RowScrollBar.IsNeeded(this.Items.Count, this.VisibleRowCount);

    /// <summary>The width the rows have, which is the control's less whatever the bar takes.</summary>
    protected int ContentWidth
        => this.Width - (this.HasScrollBar ? this.Theme.ScrollBarSize : 0);

    private Rectangle ScrollBarStrip => RowScrollBar.StripOf(this.Theme, this.Width, 0, this.Height);

    /// <summary>Whether the row at the given index is selected.</summary>
    public bool GetSelected(int index) => _selectedIndices.BinarySearch(index) >= 0;

    /// <summary>
    /// Selects or deselects the row at the given index without touching the rest of the selection —
    /// the programmatic sibling of the multi-selection gestures, raising
    /// <see cref="SelectedIndexChanged"/> at most once per call. In <see cref="SelectionMode.One"/>
    /// selecting a row replaces the previous selection.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    /// <exception cref="ArgumentException"><see cref="SelectionMode"/> is <see cref="SelectionMode.None"/>.</exception>
    public void SetSelected(int index, bool value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, this.Items.Count);
        if (this.SelectionMode == SelectionMode.None)
            throw new ArgumentException("SetSelected cannot be used while SelectionMode is None.");

        var selected = this.GetSelected(index);
        var changed = value
            ? this.SelectionMode == SelectionMode.One ? this.SelectOnlyCore(index) : !selected && this.ToggleCore(index)
            : selected && this.ToggleCore(index);
        this.FinishSelectionGesture(changed);
    }

    /// <summary>Deselects every row, raising <see cref="SelectedIndexChanged"/> once when anything
    /// was selected.</summary>
    public void ClearSelected() => this.FinishSelectionGesture(this.ClearSelectionCore());

    /// <summary>The index of the row at the given client coordinates, or -1 for none.</summary>
    public int IndexFromPoint(int x, int y)
    {
        if (x < 0 || x >= this.Width || y < 0 || y >= this.Height)
            return -1;

        // The widget lays its own rows out, so it is the only honest source of this once promoted.
        if (_native is { } peer)
            return peer.IndexFromPoint(x, y);

        // A press on the bar is not a press on the row behind it.
        if (this.HasScrollBar && x >= this.ContentWidth)
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

        // The widget holds its own copy of the list, so any structural change re-sends it whole.
        if (_native is { } peer)
            this.PushNativeItems(peer);

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

        if (_native is { } peer)
        {
            peer.ScrollIntoView(index);
            return;
        }

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

        _native?.SetSelectedIndex(this.SelectedIndex);
        this.Invalidate();
        this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.HasScrollBar)
        {
            var scrolled = _scrollBar.MouseDown(
                this.Theme, this.ScrollBarStrip, this.Items.Count, this.VisibleRowCount, _topIndex, e.Location);

            if (scrolled >= 0)
            {
                _topIndex = scrolled;
                this.ClampScroll();
                this.Invalidate();
                return;
            }
        }

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
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_scrollBar.IsDragging)
            return;

        _topIndex = _scrollBar.Drag(this.ScrollBarStrip, this.Items.Count, this.VisibleRowCount, e.Y);
        this.ClampScroll();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _scrollBar.Release();

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
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var rowHeight = this.ItemHeight;
        var rowWidth = this.ContentWidth;
        var last = Math.Min(this.Items.Count, _topIndex + this.VisibleRowCount + 1);
        for (var i = _topIndex; i < last; ++i)
        {
            var y = (i - _topIndex) * rowHeight;
            var rowRect = new Rectangle(0, y, rowWidth, rowHeight);
            var selected = this.GetSelected(i);
            if (selected)
                GlyphRenderer.FillSelection(g, theme, rowRect);

            this.OnDrawRow(g, i, rowRect, selected);
        }

        if (this.HasScrollBar)
            _scrollBar.Paint(g, theme, this.ScrollBarStrip, this.Items.Count, this.VisibleRowCount, _topIndex);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>
    /// Draws one row's content (icon and text) inside the given bounds; the selection highlight is
    /// already painted. Subclasses override to add leading adornments and delegate to the base with
    /// the remaining, right-shifted bounds.
    /// </summary>
    protected virtual void OnDrawRow(IGraphics g, int index, Rectangle bounds, bool selected)
        => DrawRowContent(
            g,
            this.Theme,
            bounds,
            this.DisplaySelector(this.Items[index]),
            this.ImageSelector?.Invoke(this.Items[index]),
            selected,
            this.Font,
            this.ForeColor);

    /// <summary>
    /// Paints the icon-plus-text body of one list row — the single row renderer every list-shaped
    /// surface shares (list box rows, combo drop-down rows), so they stay pixel-identical. The
    /// optional font/color pair lets a hosting control apply its own appearance to unselected rows
    /// (selected rows keep the theme's selection text, like Windows Forms); callers without a
    /// hosting control (drop-down popups) omit them and get the plain theme rendering.
    /// </summary>
    internal static void DrawRowContent(
        IGraphics g,
        ITheme theme,
        Rectangle bounds,
        string text,
        IImage? icon,
        bool selected,
        Font? font = null,
        Color foreColor = default)
    {
        var textLeft = bounds.X + 2;
        if (icon is not null)
        {
            var iconSize = bounds.Height - 4;
            g.DrawImage(icon, new Rectangle(textLeft, bounds.Y + 2, iconSize, iconSize));
            textLeft += iconSize + _IconGap;
        }

        var textColor = selected ? theme.SelectionText : foreColor.IsEmpty ? theme.ControlText : foreColor;
        var textRect = new Rectangle(textLeft, bounds.Y, bounds.Right - textLeft, bounds.Height);
        g.DrawText(text, font ?? theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
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
