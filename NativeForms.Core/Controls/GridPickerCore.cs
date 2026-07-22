using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The cell-grid engine <see cref="GridPicker"/> and the ribbon's Table drop-down share: grid
/// geometry, painting, hit-testing, the hovered <see cref="Rows"/>×<see cref="Columns"/> highlight,
/// keyboard navigation and the commit/cancel notifications live here once, so the standalone control
/// and the popup stay pixel- and behavior-identical. The engine is surface-agnostic — the host passes
/// its theme and size into every call and receives repaint, commit and cancel notifications through
/// the callback slots, which stay <see langword="null"/> until assigned, like unsubscribed events.
/// </summary>
/// <remarks>
/// The Office "Insert Table" affordance: a grid of empty cells the pointer sweeps to size a table,
/// the top-left <see cref="Columns"/>×<see cref="Rows"/> block highlighted in the accent, a caption
/// reading "C × R Table" (or "Cancel" while nothing is hovered), and a click that commits the
/// dimensions. The caption string is cached and rebuilt only when the hovered block changes, so a
/// steady-state repaint allocates nothing.
/// </remarks>
internal sealed class GridPickerCore
{
    /// <summary>The edge length of one cell in pixels.</summary>
    private const int _CellSize = 18;

    /// <summary>The padding around the grid and the caption.</summary>
    private const int _Pad = 6;

    /// <summary>The gap between the grid and the caption strip.</summary>
    private const int _Gap = 4;

    /// <summary>The caption shown while no cell is hovered.</summary>
    private const string _CancelCaption = "Cancel";

    private int _maxColumns = 10;
    private int _maxRows = 8;
    private int _columns;
    private int _rows;
    private string? _caption;

    /// <summary>Requests a repaint of the host surface.</summary>
    public Action? Invalidated { get; set; }

    /// <summary>Fires when a valid block is committed, with the chosen (rows, columns).</summary>
    public Action<int, int>? RangeSelected { get; set; }

    /// <summary>Fires when the pick is cancelled (Escape).</summary>
    public Action? Canceled { get; set; }

    /// <summary>The greatest number of columns the grid offers; at least one.</summary>
    public int MaxColumns
    {
        get => _maxColumns;
        set
        {
            value = Math.Max(1, value);
            if (_maxColumns == value)
                return;

            _maxColumns = value;
            if (_columns > value)
                this.SetHover(value, _rows);

            this.Invalidated?.Invoke();
        }
    }

    /// <summary>The greatest number of rows the grid offers; at least one.</summary>
    public int MaxRows
    {
        get => _maxRows;
        set
        {
            value = Math.Max(1, value);
            if (_maxRows == value)
                return;

            _maxRows = value;
            if (_rows > value)
                this.SetHover(_columns, value);

            this.Invalidated?.Invoke();
        }
    }

    /// <summary>The hovered column count, or zero while nothing is hovered.</summary>
    public int Columns => _columns;

    /// <summary>The hovered row count, or zero while nothing is hovered.</summary>
    public int Rows => _rows;

    /// <summary>The natural pixel size of the grid plus its caption strip, under a theme.</summary>
    public Size PreferredSize(Drawing.ITheme theme)
        => new(
            (2 * _Pad) + (_maxColumns * _CellSize),
            _Pad + (_maxRows * _CellSize) + _Gap + theme.RowHeight + _Pad);

    /// <summary>Sets the hovered block to a specific size, clamped into the grid, without committing.</summary>
    public void SetSelection(int rows, int columns)
        => this.SetHover(Math.Clamp(columns, 0, _maxColumns), Math.Clamp(rows, 0, _maxRows));

    /// <summary>Clears the hovered block back to nothing.</summary>
    public void ClearHover() => this.SetHover(0, 0);

    /// <summary>Paints the cell grid and the caption strip into a surface of the given size.</summary>
    public void Paint(Drawing.IGraphics g, Drawing.ITheme theme, Size size)
    {
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, size.Width, size.Height));

        for (var r = 0; r < _maxRows; ++r)
            for (var c = 0; c < _maxColumns; ++c)
            {
                var x = _Pad + (c * _CellSize);
                var y = _Pad + (r * _CellSize);
                var selected = c < _columns && r < _rows;
                g.FillRectangle(selected ? theme.Accent : theme.FieldBackground, new Rectangle(x + 1, y + 1, _CellSize - 1, _CellSize - 1));
                g.DrawRectangle(theme.Border, new Rectangle(x, y, _CellSize, _CellSize));
            }

        var captionTop = _Pad + (_maxRows * _CellSize) + _Gap;
        var captionRect = new Rectangle(_Pad, captionTop, size.Width - (2 * _Pad), theme.RowHeight);
        g.DrawText(this.Caption(), theme.DefaultFont, theme.ControlText, captionRect, ContentAlignment.MiddleCenter);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
    }

    /// <summary>Tracks the pointer: highlights the block under it.</summary>
    public void HandleMouseMove(Drawing.ITheme theme, Size size, MouseEventArgs e)
    {
        if (this.CellAt(e.X, e.Y, out var column, out var row))
            this.SetHover(column, row);
    }

    /// <summary>A left click on a cell commits that block.</summary>
    public void HandleMouseDown(Drawing.ITheme theme, Size size, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !this.CellAt(e.X, e.Y, out var column, out var row))
            return;

        this.SetHover(column, row);
        this.Commit();
    }

    /// <summary>Arrows move the hovered block, Enter commits it, Escape cancels.</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Right or Keys.Up or Keys.Down when _columns == 0 || _rows == 0:
                this.SetHover(1, 1); // the first arrow press lands on the first cell
                break;
            case Keys.Left:
                this.SetHover(Math.Max(1, _columns - 1), _rows);
                break;
            case Keys.Right:
                this.SetHover(Math.Min(_maxColumns, _columns + 1), _rows);
                break;
            case Keys.Up:
                this.SetHover(_columns, Math.Max(1, _rows - 1));
                break;
            case Keys.Down:
                this.SetHover(_columns, Math.Min(_maxRows, _rows + 1));
                break;
            case Keys.Enter:
                this.Commit();
                break;
            case Keys.Escape:
                this.Canceled?.Invoke();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The 1-based cell under a client point, or <see langword="false"/> when outside the grid.</summary>
    private bool CellAt(int x, int y, out int column, out int row)
    {
        column = row = 0;
        var gridX = x - _Pad;
        var gridY = y - _Pad;
        if (gridX < 0 || gridY < 0)
            return false;

        var c = gridX / _CellSize;
        var r = gridY / _CellSize;
        if (c >= _maxColumns || r >= _maxRows)
            return false;

        column = c + 1;
        row = r + 1;
        return true;
    }

    /// <summary>Sets the hovered block, dropping the cached caption and repainting on a change.</summary>
    private void SetHover(int column, int row)
    {
        if (column == _columns && row == _rows)
            return;

        _columns = column;
        _rows = row;
        _caption = null;
        this.Invalidated?.Invoke();
    }

    /// <summary>Reports the hovered block when it is a real table.</summary>
    private void Commit()
    {
        if (_columns >= 1 && _rows >= 1)
            this.RangeSelected?.Invoke(_rows, _columns);
    }

    /// <summary>The caption for the current block, cached so painting never formats a string.</summary>
    private string Caption()
        => _caption ??= _columns >= 1 && _rows >= 1 ? $"{_columns} × {_rows} Table" : _CancelCaption;
}
