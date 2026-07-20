using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Paints the themed column-header band the columned controls share (<see cref="ListView"/>,
/// <see cref="TreeListView"/>): header background across the full width, per-column clipped captions
/// in the column's alignment, a separator after every column and the bottom rule. One painter lives
/// here so the band stays pixel-identical wherever it appears.
/// </summary>
internal static class HeaderRowPainter
{
    private const int _CellPad = 2;

    /// <summary>Draws the band across the top of a control that is <paramref name="width"/> pixels wide.</summary>
    public static void Draw(IGraphics g, ITheme theme, IReadOnlyList<ColumnHeader> columns, int width, int headerHeight)
    {
        // The band fill also covers the residual area beyond the last column; each cell face then
        // comes from the shared header-cell primitive so the band matches every other header.
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, width, headerHeight));

        var x = 0;
        for (var c = 0; c < columns.Count; ++c)
        {
            var col = columns[c];
            GlyphRenderer.DrawHeaderCell(g, theme, new Rectangle(x, 0, col.Width, headerHeight), col.Text, col.TextAlign, _CellPad, separator: true);
            x += col.Width;
        }

        g.DrawLine(theme.Border, 0, headerHeight - 1, width, headerHeight - 1);
    }
}
