using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Optional per-cell presentation overrides returned by
/// <see cref="DataGridViewColumn.CellStyleSelector"/>. A small value type so style selectors run on
/// the paint path without allocating; every member is optional — <see langword="null"/> keeps the
/// column/theme default.
/// </summary>
public readonly struct DataGridViewCellStyle(
    Color? foreColor = null,
    Color? backColor = null,
    ContentAlignment? alignment = null)
{
    /// <summary>The text color, or <see langword="null"/> for the themed default.</summary>
    public Color? ForeColor { get; } = foreColor;

    /// <summary>The cell background, or <see langword="null"/> for the row background.</summary>
    public Color? BackColor { get; } = backColor;

    /// <summary>The content alignment, or <see langword="null"/> for the column's alignment.</summary>
    public ContentAlignment? Alignment { get; } = alignment;
}
