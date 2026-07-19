using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A column definition for a <see cref="DataGridView"/>. Cell values are produced by reflection-free
/// selector delegates (row item → value / icon / state), so binding stays trim/AOT-safe. One class
/// covers every <see cref="DataGridViewColumnKind"/> — the kind-specific selectors are simply unused
/// by the other kinds — which keeps the grid's paint switch free of per-cell objects.
/// </summary>
public sealed class DataGridViewColumn
{
    /// <summary>Creates a column bound to the given value selector.</summary>
    /// <param name="headerText">The text painted in the column header.</param>
    /// <param name="valueSelector">Maps a row item to the value shown in this cell (rendered via <c>ToString()</c>).</param>
    public DataGridViewColumn(string headerText, Func<object?, object?> valueSelector)
    {
        this.HeaderText = headerText ?? string.Empty;
        this.ValueSelector = valueSelector ?? (static _ => null);
    }

    /// <summary>The text painted in the column header.</summary>
    public string HeaderText { get; set; }

    /// <summary>How this column renders and reacts to clicks. Defaults to
    /// <see cref="DataGridViewColumnKind.Text"/>.</summary>
    public DataGridViewColumnKind Kind { get; set; }

    /// <summary>The column width in pixels.</summary>
    public int Width { get; set; } = 100;

    /// <summary>Alignment of the cell content within the column.</summary>
    public ContentAlignment Alignment { get; set; } = ContentAlignment.MiddleLeft;

    /// <summary>Maps a row item to the value shown in this cell (rendered via <c>ToString()</c>).</summary>
    public Func<object?, object?> ValueSelector { get; set; }

    /// <summary>Optional selector producing a per-cell icon painted before the text;
    /// <see langword="null"/> for none.</summary>
    public Func<object?, IImage?>? ImageSelector { get; set; }

    /// <summary>Whether every cell in this column refuses edits and check toggling. Combined with the
    /// grid and per-cell levels by <see cref="DataGridView.IsCellReadOnly"/>.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional per-cell read-only predicate over the row item; <see langword="null"/> means
    /// not read-only at the cell level.</summary>
    public Func<object?, bool>? ReadOnlyCellSelector { get; set; }

    /// <summary>Maps a row item to the check state of a <see cref="DataGridViewColumnKind.Check"/>
    /// cell; unset cells render unchecked.</summary>
    public Func<object?, bool>? CheckedSelector { get; set; }

    /// <summary>Writes the toggled check state back to the row item when a
    /// <see cref="DataGridViewColumnKind.Check"/> cell is clicked and the cell is not read-only;
    /// <see langword="null"/> makes the glyph display-only.</summary>
    public Action<object?, bool>? CheckedSetter { get; set; }

    /// <summary>Maps a row item to whether a <see cref="DataGridViewColumnKind.Button"/> cell is
    /// enabled; <see langword="null"/> means always enabled. Disabled buttons grey their text and
    /// raise no <see cref="DataGridView.CellContentClick"/>.</summary>
    public Func<object?, bool>? EnabledSelector { get; set; }

    /// <summary>Maps a row item to the icons of a <see cref="DataGridViewColumnKind.MultiImage"/>
    /// cell, painted side by side. Return a cached list — the selector runs on the paint path.</summary>
    public Func<object?, IReadOnlyList<IImage>>? ImagesSelector { get; set; }

    /// <summary>Maps a row item to the 0..100 fill of a <see cref="DataGridViewColumnKind.Progress"/>
    /// cell; values outside the range are clamped.</summary>
    public Func<object?, int>? ProgressSelector { get; set; }

    /// <summary>Optional per-cell style overrides (colors, alignment); <see langword="null"/> keeps the
    /// defaults. Returns a value type, so it is safe on the paint path.</summary>
    public Func<object?, DataGridViewCellStyle>? CellStyleSelector { get; set; }

    /// <summary>Optional override of the displayed cell text; return <see langword="null"/> to fall
    /// back to <see cref="ValueSelector"/>.</summary>
    public Func<object?, string?>? DisplayTextSelector { get; set; }

    /// <summary>Optional per-cell tooltip text, surfaced through
    /// <see cref="DataGridView.GetCellTooltip"/>; <see langword="null"/> for none.</summary>
    public Func<object?, string?>? TooltipSelector { get; set; }

    /// <summary>Whether clicking this column's header sorts the grid. Defaults to
    /// <see cref="DataGridViewColumnSortMode.NotSortable"/>.</summary>
    public DataGridViewColumnSortMode SortMode { get; set; }

    /// <summary>Optional row-item comparison used when this column sorts; <see langword="null"/> falls
    /// back to comparing the values <see cref="ValueSelector"/> produces.</summary>
    public Comparison<object?>? SortComparison { get; set; }

    /// <summary>How the column width is computed. Defaults to
    /// <see cref="DataGridViewAutoSizeColumnMode.None"/>.</summary>
    public DataGridViewAutoSizeColumnMode AutoSizeMode { get; set; }
}
