using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A column definition for a <see cref="DataGridView"/>. Cell values are produced by reflection-free
/// selector delegates (row item → value / icon), so binding stays trim/AOT-safe.
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

    /// <summary>The column width in pixels.</summary>
    public int Width { get; set; } = 100;

    /// <summary>Alignment of the cell content within the column.</summary>
    public ContentAlignment Alignment { get; set; } = ContentAlignment.MiddleLeft;

    /// <summary>Maps a row item to the value shown in this cell (rendered via <c>ToString()</c>).</summary>
    public Func<object?, object?> ValueSelector { get; set; }

    /// <summary>Optional selector producing a per-cell icon; <see langword="null"/> for none.</summary>
    public Func<object?, IImage?>? ImageSelector { get; set; }
}
