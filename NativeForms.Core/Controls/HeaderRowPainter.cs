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
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, width, headerHeight));

        var x = 0;
        for (var c = 0; c < columns.Count; ++c)
        {
            var col = columns[c];
            g.PushClip(new Rectangle(x, 0, col.Width, headerHeight));
            var textRect = new Rectangle(x + _CellPad, 0, col.Width - (2 * _CellPad), headerHeight);
            g.DrawText(col.Text, theme.DefaultFont, theme.HeaderText, textRect, col.TextAlign);
            g.PopClip();

            x += col.Width;
            g.DrawLine(theme.Border, x, 0, x, headerHeight);
        }

        g.DrawLine(theme.Border, 0, headerHeight - 1, width, headerHeight - 1);
    }
}
