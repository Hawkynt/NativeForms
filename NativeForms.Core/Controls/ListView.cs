using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn list view painted in the native theme. Supports the <see cref="ListViewView.Details"/>
/// (multi-column grid with a header row) and <see cref="ListViewView.List"/> (single vertical column)
/// layouts, per-item icons, single selection and wheel/keyboard scrolling. Painting is virtualized to
/// the visible row range so it stays cheap for very large <see cref="Items"/> collections.
/// </summary>
/// <remarks>
/// v1 supports single selection only. TODO: multi-selection, checkboxes, groups, label editing,
/// sorting, a virtual-mode item API and the large-icon/small-icon/tile layouts.
/// </remarks>
public class ListView : OwnerDrawnControl
{
    private const int _IconGap = 4;
    private const int _CellPad = 2;

    private int _selectedIndex = -1;
    private int _topIndex;
    private int? _itemHeight;

    /// <summary>Creates a list view.</summary>
    public ListView()
    {
        this.Columns = new();
        this.Items = new();
        this.Columns.ListChanged += this.OnColumnsChanged;
        this.Items.ListChanged += this.OnItemsChanged;
    }

    /// <summary>The columns shown in Details view. Mutating this collection repaints the control.</summary>
    public ObservableList<ColumnHeader> Columns { get; }

    /// <summary>The rows shown. Mutating this collection repaints the control.</summary>
    public ObservableList<ListViewItem> Items { get; }

    /// <summary>How items are arranged. Defaults to <see cref="ListViewView.Details"/>.</summary>
    public ListViewView View
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ClampScroll();
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
            this.ClampScroll();
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

    /// <summary>The pixel height of a row (and of the header). Defaults to the theme row height.</summary>
    public int ItemHeight
    {
        get => _itemHeight ?? this.Theme.RowHeight;
        set
        {
            _itemHeight = Math.Max(1, value);
            this.ClampScroll();
            this.Invalidate();
        }
    }

    /// <summary>The selected row index, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (clamped == _selectedIndex)
                return;

            if (_selectedIndex >= 0 && _selectedIndex < this.Items.Count)
                this.Items[_selectedIndex].Selected = false;

            _selectedIndex = clamped;
            if (clamped >= 0)
                this.Items[clamped].Selected = true;

            this.EnsureVisible(clamped);
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected item, or <see langword="null"/>.</summary>
    public ListViewItem? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < this.Items.Count ? this.Items[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>The index of the first visible row (scroll position).</summary>
    public int TopIndex => _topIndex;

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The pixel height reserved for the header row (0 unless Details with headers shown).</summary>
    protected int HeaderHeight => this.View == ListViewView.Details && this.ShowColumnHeaders ? this.ItemHeight : 0;

    /// <summary>The number of fully visible rows in the item area.</summary>
    protected int VisibleRowCount => Math.Max(1, (this.Height - this.HeaderHeight) / this.ItemHeight);

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

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

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedIndex >= this.Items.Count)
            _selectedIndex = this.Items.Count - 1;

        this.ClampScroll();
        this.Invalidate();
    }

    private void OnColumnsChanged(object? sender, ListChangedEventArgs e) => this.Invalidate();

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.Items.Count - this.VisibleRowCount);
        _topIndex = Math.Clamp(_topIndex, 0, maxTop);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var contentY = e.Y - this.HeaderHeight;
        if (contentY < 0)
            return;

        var row = _topIndex + (contentY / this.ItemHeight);
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

        if (this.View == ListViewView.Details)
            this.PaintDetails(g, theme);
        else
            this.PaintList(g, theme);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintDetails(IGraphics g, ITheme theme)
    {
        var rowHeight = this.ItemHeight;
        var headerHeight = this.HeaderHeight;
        if (headerHeight > 0)
            HeaderRowPainter.Draw(g, theme, this.Columns, this.Width, headerHeight);

        var last = Math.Min(this.Items.Count, _topIndex + this.VisibleRowCount + 1);
        for (var i = _topIndex; i < last; ++i)
        {
            var item = this.Items[i];
            var y = headerHeight + ((i - _topIndex) * rowHeight);
            var selected = i == _selectedIndex;
            if (selected)
            {
                var selWidth = this.FullRowSelect || this.Columns.Count == 0 ? this.Width : this.Columns[0].Width;
                g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, selWidth, rowHeight));
            }

            var textColor = selected ? theme.SelectionText : theme.ControlText;
            if (this.Columns.Count == 0)
            {
                this.PaintPrimaryCell(g, theme, item, textColor, 0, y, this.Width, rowHeight, ContentAlignment.MiddleLeft);
                continue;
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
    }

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
        var textLeft = x + _CellPad;
        if (item.Image is not null)
        {
            var iconSize = rowHeight - 4;
            g.DrawImage(item.Image, new Rectangle(x + _CellPad, y + 2, iconSize, iconSize));
            textLeft = x + iconSize + _IconGap + _CellPad;
        }

        var textRect = new Rectangle(textLeft, y, x + width - textLeft - _CellPad, rowHeight);
        g.DrawText(item.Text, theme.DefaultFont, textColor, textRect, alignment);
    }

    private void PaintList(IGraphics g, ITheme theme)
    {
        var rowHeight = this.ItemHeight;
        var last = Math.Min(this.Items.Count, _topIndex + this.VisibleRowCount + 1);
        for (var i = _topIndex; i < last; ++i)
        {
            var item = this.Items[i];
            var y = (i - _topIndex) * rowHeight;
            var selected = i == _selectedIndex;
            if (selected)
                g.FillRectangle(theme.SelectionBackground, new Rectangle(0, y, this.Width, rowHeight));

            var textColor = selected ? theme.SelectionText : theme.ControlText;
            this.PaintPrimaryCell(g, theme, item, textColor, 0, y, this.Width, rowHeight, ContentAlignment.MiddleLeft);
        }
    }
}
