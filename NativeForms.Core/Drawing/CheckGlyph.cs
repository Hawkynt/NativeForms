using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Draws the themed check square every checkable control shares (<see cref="CheckBox"/>, the rows of
/// <see cref="CheckedListBox"/>): field-colored box, accent border when checked, accent checkmark.
/// One drawing lives here so the glyph stays pixel-identical wherever it appears.
/// </summary>
internal static class CheckGlyph
{
    /// <summary>The edge length of the square in pixels.</summary>
    public const int BoxSize = 14;

    /// <summary>Draws the glyph with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void Draw(IGraphics g, ITheme theme, int x, int y, bool isChecked)
    {
        var box = new Rectangle(x, y, BoxSize, BoxSize);
        g.FillRectangle(theme.FieldBackground, box);
        g.DrawRectangle(isChecked ? theme.Accent : theme.Border, box);

        if (!isChecked)
            return;

        // A check mark: two strokes from the lower-left to the upper-right.
        g.DrawLine(theme.Accent, x + 3, y + 7, x + 6, y + 10, 2);
        g.DrawLine(theme.Accent, x + 6, y + 10, x + 11, y + 3, 2);
    }
}
