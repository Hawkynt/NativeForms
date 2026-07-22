using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The chosen table size a <see cref="GridPicker"/> or a ribbon Table drop-down reports.</summary>
public sealed class GridRangeEventArgs(int rows, int columns) : EventArgs
{
    /// <summary>The number of rows in the chosen table.</summary>
    public int Rows { get; } = rows;

    /// <summary>The number of columns in the chosen table.</summary>
    public int Columns { get; } = columns;
}

/// <summary>
/// The Office-style table-size picker: a grid of cells the pointer sweeps to choose an N×M table,
/// with the hovered top-left block highlighted in the accent, a live "C × R Table" caption, and a
/// click (or Enter) that commits through <see cref="RangeSelected"/>. Arrow keys move the hover,
/// Escape cancels. Reusable and decoupled — it drops into a form directly, hosts inside a drop-down,
/// or backs the ribbon's Table button; every surface shares one <see cref="GridPickerCore"/> so they
/// stay pixel-identical.
/// </summary>
/// <remarks>
/// Painted with the platform <see cref="Drawing.ITheme"/> (accent block, field-colored cells, themed
/// borders) so it matches the host desktop, and the caption is cached, so a steady-state repaint
/// allocates nothing. Unlike Word the grid does not grow past <see cref="MaxColumns"/>/<see cref="MaxRows"/>
/// when the pointer is dragged beyond its edge; size those to the largest table the picker should offer.
/// </remarks>
public class GridPicker : OwnerDrawnControl
{
    private readonly GridPickerCore _core = new();

    /// <summary>Creates a table-size picker with the default 10×8 grid.</summary>
    public GridPicker()
    {
        _core.Invalidated = this.Invalidate;
        _core.RangeSelected = this.OnCoreRangeSelected;
        _core.Canceled = this.OnCoreCanceled;
    }

    /// <summary>The greatest number of columns the grid offers; at least one.</summary>
    public int MaxColumns
    {
        get => _core.MaxColumns;
        set => _core.MaxColumns = value;
    }

    /// <summary>The greatest number of rows the grid offers; at least one.</summary>
    public int MaxRows
    {
        get => _core.MaxRows;
        set => _core.MaxRows = value;
    }

    /// <summary>The hovered column count, or zero while nothing is hovered.</summary>
    public int Columns => _core.Columns;

    /// <summary>The hovered row count, or zero while nothing is hovered.</summary>
    public int Rows => _core.Rows;

    /// <summary>The natural pixel size of the grid plus its caption strip, under the current theme.</summary>
    public Size PreferredSize => _core.PreferredSize(this.Theme);

    /// <summary>Raised when a valid block is committed by click or Enter.</summary>
    public event EventHandler<GridRangeEventArgs>? RangeSelected;

    /// <summary>Raised when the pick is cancelled with Escape.</summary>
    public event EventHandler? Canceled;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Sets the hovered block programmatically, clamped into the grid.</summary>
    public void SetSelection(int rows, int columns) => _core.SetSelection(rows, columns);

    /// <summary>Raises <see cref="RangeSelected"/>.</summary>
    protected virtual void OnRangeSelected(GridRangeEventArgs e) => this.RangeSelected?.Invoke(this, e);

    /// <summary>Raises <see cref="Canceled"/>.</summary>
    protected virtual void OnCanceled(EventArgs e) => this.Canceled?.Invoke(this, e);

    private void OnCoreRangeSelected(int rows, int columns) => this.OnRangeSelected(new GridRangeEventArgs(rows, columns));

    private void OnCoreCanceled() => this.OnCanceled(EventArgs.Empty);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e) => _core.Paint(e.Graphics, this.Theme, this.Size);

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e) => _core.HandleMouseMove(this.Theme, this.Size, e);

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        _core.HandleMouseDown(this.Theme, this.Size, e);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e) => _core.ClearHover();

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Escape;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) => _core.HandleKeyDown(e);
}
