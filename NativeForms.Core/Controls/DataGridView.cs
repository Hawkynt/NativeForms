using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn, vertically virtualized data grid painted in the native theme. Rows are arbitrary
/// objects bound through an <see cref="ObservableList{T}"/>; each <see cref="DataGridViewColumn"/>
/// maps a row to a cell value and optional icon via reflection-free selector delegates, so binding
/// stays trim/AOT-safe. Only the visible row range is painted, so very large row counts stay cheap.
/// </summary>
public class DataGridView : OwnerDrawnControl
{
    private const int _CellPadding = 4;
    private const int _IconGap = 4;
    private const int _WheelRows = 3;

    private readonly List<DataGridViewColumn> _columns = [];

    private int _selectedRowIndex = -1;
    private int _topRow;
    private int? _rowHeight;
    private int? _columnHeaderHeight;

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

    /// <summary>The horizontal scroll offset in pixels; columns are shifted left by this amount.</summary>
    public int HorizontalOffset
    {
        get => field;
        set
        {
            field = Math.Max(0, value);
            this.Invalidate();
        }
    }

    /// <summary>The selected row index, or -1 for none.</summary>
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

    /// <summary>The index of the first visible data row (vertical scroll position).</summary>
    public int TopRow => _topRow;

    /// <summary>Raised when <see cref="SelectedRowIndex"/> changes.</summary>
    public event EventHandler? SelectionChanged;

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

    /// <summary>The number of fully visible data rows.</summary>
    protected int VisibleRowCount => Math.Max(1, (this.Height - this.HeaderHeight) / this.RowHeight);

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged(EventArgs e) => this.SelectionChanged?.Invoke(this, e);

    /// <summary>Scrolls so the given data row is visible.</summary>
    public void EnsureVisible(int rowIndex)
    {
        if (rowIndex < 0)
            return;

        if (rowIndex < _topRow)
            _topRow = rowIndex;
        else if (rowIndex >= _topRow + this.VisibleRowCount)
            _topRow = rowIndex - this.VisibleRowCount + 1;

        this.ClampScroll();
    }

    private void OnItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedRowIndex >= this.Items.Count)
            _selectedRowIndex = this.Items.Count - 1;

        this.ClampScroll();
        this.Invalidate();
    }

    private void ClampScroll()
    {
        var maxTop = Math.Max(0, this.Items.Count - this.VisibleRowCount);
        _topRow = Math.Clamp(_topRow, 0, maxTop);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var header = this.HeaderHeight;
        if (e.Y < header)
            return;

        var row = _topRow + ((e.Y - header) / this.RowHeight);
        if (row >= 0 && row < this.Items.Count)
            this.SelectedRowIndex = row;
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _topRow -= Math.Sign(e.Delta) * _WheelRows;
        this.ClampScroll();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Down: this.SelectedRowIndex = Math.Min(this.Items.Count - 1, _selectedRowIndex + 1); break;
            case Keys.Up: this.SelectedRowIndex = Math.Max(0, _selectedRowIndex - 1); break;
            case Keys.Home when this.Items.Count > 0: this.SelectedRowIndex = 0; break;
            case Keys.End: this.SelectedRowIndex = this.Items.Count - 1; break;
            case Keys.PageDown: this.SelectedRowIndex = Math.Min(this.Items.Count - 1, _selectedRowIndex + this.VisibleRowCount); break;
            case Keys.PageUp: this.SelectedRowIndex = Math.Max(0, _selectedRowIndex - this.VisibleRowCount); break;
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

        var columns = _columns;
        var columnCount = columns.Count;
        var rowHeight = this.RowHeight;
        var header = this.HeaderHeight;
        var horizontalOffset = this.HorizontalOffset;
        var showGridLines = this.ShowGridLines;

        if (this.ShowColumnHeaders)
        {
            var headerRect = new Rectangle(0, 0, width, header);
            g.FillRectangle(theme.HeaderBackground, headerRect);

            var hx = -horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                var column = columns[c];
                var cellRect = new Rectangle(hx + _CellPadding, 0, Math.Max(0, column.Width - (_CellPadding * 2)), header);
                g.DrawText(column.HeaderText, theme.DefaultFont, theme.HeaderText, cellRect, column.Alignment);
                hx += column.Width;
            }

            g.DrawLine(theme.Border, 0, header - 1, width, header - 1);
        }

        var count = this.Items.Count;
        var last = Math.Min(count, _topRow + this.VisibleRowCount + 1);
        for (var i = _topRow; i < last; ++i)
        {
            var y = header + ((i - _topRow) * rowHeight);
            var rowRect = new Rectangle(0, y, width, rowHeight);
            var selected = i == _selectedRowIndex;
            if (selected)
                g.FillRectangle(theme.SelectionBackground, rowRect);
            else if (this.AlternatingRows && (i & 1) == 1)
                g.FillRectangle(this.AlternatingRowColor, rowRect);

            var item = this.Items[i];
            var textColor = selected ? theme.SelectionText : theme.ControlText;
            var cx = -horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                var column = columns[c];
                var textLeft = cx + _CellPadding;
                var icon = column.ImageSelector?.Invoke(item);
                if (icon is not null)
                {
                    var iconSize = rowHeight - 4;
                    g.DrawImage(icon, new Rectangle(cx + _CellPadding, y + 2, iconSize, iconSize));
                    textLeft = cx + _CellPadding + iconSize + _IconGap;
                }

                var cellWidth = Math.Max(0, (cx + column.Width) - textLeft);
                var cellRect = new Rectangle(textLeft, y, cellWidth, rowHeight);
                g.DrawText(column.ValueSelector(item)?.ToString() ?? string.Empty, theme.DefaultFont, textColor, cellRect, column.Alignment);
                cx += column.Width;
            }

            if (showGridLines)
                g.DrawLine(theme.GridLine, 0, y + rowHeight - 1, width, y + rowHeight - 1);
        }

        if (showGridLines)
        {
            var gx = -horizontalOffset;
            for (var c = 0; c < columnCount; ++c)
            {
                gx += columns[c].Width;
                if (gx > 0 && gx < width)
                    g.DrawLine(theme.GridLine, gx - 1, header, gx - 1, height);
            }
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }
}
