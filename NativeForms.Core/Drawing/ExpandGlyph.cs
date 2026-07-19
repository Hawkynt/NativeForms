using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Draws the themed expand/collapse square the tree-shaped controls share (<see cref="TreeView"/>,
/// the tree column of <see cref="TreeListView"/>): the classic field-colored box with a − (expanded)
/// or + (collapsed) inside. One drawing lives here so the glyph stays pixel-identical wherever it
/// appears.
/// </summary>
internal static class ExpandGlyph
{
    /// <summary>The edge length of the square in pixels.</summary>
    public const int BoxSize = 9;

    /// <summary>Draws the glyph centered in the cell at (<paramref name="cellLeft"/>, <paramref name="y"/>).</summary>
    public static void Draw(IGraphics g, ITheme theme, int cellLeft, int y, int cellWidth, int cellHeight, bool expanded)
    {
        var box = new Rectangle(cellLeft + ((cellWidth - BoxSize) / 2), y + ((cellHeight - BoxSize) / 2), BoxSize, BoxSize);
        g.FillRectangle(theme.FieldBackground, box);
        g.DrawRectangle(theme.Border, box);

        var midX = box.X + (BoxSize / 2);
        var midY = box.Y + (BoxSize / 2);
        g.DrawLine(theme.ControlText, box.X + 2, midY, box.X + BoxSize - 2, midY);
        if (!expanded)
            g.DrawLine(theme.ControlText, midX, box.Y + 2, midX, box.Y + BoxSize - 2);
    }
}
