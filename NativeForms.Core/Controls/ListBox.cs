using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn single-selection list painted in the native theme, with optional per-item icons and
/// wheel/keyboard scrolling. Items are arbitrary objects; their text and icon are produced by
/// reflection-free selector delegates, so binding stays trim/AOT-safe.
/// </summary>
public class ListBox : OwnerDrawnControl
{
    private const int _IconGap = 4;

    private int _selectedIndex = -1;
    private int _topIndex;
    private int? _itemHeight;

    /// <summary>Creates a list box.</summary>
    public ListBox()
    {
        this.Items = new();
        this.Items.ListChanged += this.OnItemsChanged;
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

    /// <summary>The selected item index, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (clamped == _selectedIndex)
                return;

            _selectedIndex = clamped;
            this.EnsureVisible(clamped);
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected item, or <see langword="null"/>.</summary>
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < this.Items.Count ? this.Items[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>The index of the first visible row (scroll position).</summary>
    public int TopIndex => _topIndex;

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
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

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedIndex >= this.Items.Count)
            _selectedIndex = this.Items.Count - 1;

        this.ClampScroll();
        this.Invalidate();
    }

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

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var row = _topIndex + (e.Y / this.ItemHeight);
        if (row >= 0 && row < this.Items.Count)
            this.SelectedIndex = row;
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
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Down: this.SelectedIndex = Math.Min(this.Items.Count - 1, _selectedIndex + 1); break;
            case Keys.Up: this.SelectedIndex = Math.Max(0, _selectedIndex - 1); break;
            case Keys.Home when this.Items.Count > 0: this.SelectedIndex = 0; break;
            case Keys.End: this.SelectedIndex = this.Items.Count - 1; break;
            case Keys.PageDown: this.SelectedIndex = Math.Min(this.Items.Count - 1, _selectedIndex + this.VisibleRowCount); break;
            case Keys.PageUp: this.SelectedIndex = Math.Max(0, _selectedIndex - this.VisibleRowCount); break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, this.Height));

        var rowHeight = this.ItemHeight;
        var displaySelector = this.DisplaySelector;
        var last = Math.Min(this.Items.Count, _topIndex + this.VisibleRowCount + 1);
        for (var i = _topIndex; i < last; ++i)
        {
            var y = (i - _topIndex) * rowHeight;
            var rowRect = new Rectangle(0, y, this.Width, rowHeight);
            var selected = i == _selectedIndex;
            if (selected)
                g.FillRectangle(theme.SelectionBackground, rowRect);

            var textLeft = 2;
            var icon = this.ImageSelector?.Invoke(this.Items[i]);
            if (icon is not null)
            {
                var iconSize = rowHeight - 4;
                g.DrawImage(icon, new Rectangle(2, y + 2, iconSize, iconSize));
                textLeft = iconSize + _IconGap + 2;
            }

            var textColor = selected ? theme.SelectionText : theme.ControlText;
            var textRect = new Rectangle(textLeft, y, this.Width - textLeft, rowHeight);
            g.DrawText(displaySelector(this.Items[i]), theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }
}
