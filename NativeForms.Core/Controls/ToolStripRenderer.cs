using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The small paint helpers every strip surface shares: mnemonic-underlined captions and the check and
/// radio marks of menu rows. One drawing lives here so a caption looks the same in the menu bar, a
/// drop-down and a toolbar button.
/// </summary>
internal static class ToolStripRenderer
{
    /// <summary>
    /// Draws an item's caption middle-left in <paramref name="bounds"/> and underlines its mnemonic
    /// character. The item caches its parsed mnemonic strings, so this measures but never allocates.
    /// </summary>
    public static void PaintMnemonicText(IGraphics g, Font font, Color color, ToolStripItem item, Rectangle bounds)
    {
        var text = item.DisplayText;
        g.DrawText(text, font, color, bounds, ContentAlignment.MiddleLeft);
        if (item.MnemonicIndex < 0)
            return;

        var textSize = g.MeasureText(text, font);
        var prefixWidth = item.MnemonicPrefix.Length > 0 ? g.MeasureText(item.MnemonicPrefix, font).Width : 0;
        var charWidth = g.MeasureText(item.MnemonicCharText, font).Width;
        var x = bounds.X + prefixWidth;
        var y = bounds.Y + ((bounds.Height + textSize.Height) / 2) - 1;
        g.DrawLine(color, x, y, x + charWidth - 1, y);
    }

    /// <summary>Draws the two-stroke check mark of a checked menu item, centered in the leading column.</summary>
    public static void PaintCheckMark(IGraphics g, Color color, int x, int y, int columnWidth, int rowHeight)
    {
        var cx = x + (columnWidth / 2);
        var cy = y + (rowHeight / 2);
        g.DrawLine(color, cx - 4, cy, cx - 1, cy + 3, 2);
        g.DrawLine(color, cx - 1, cy + 3, cx + 4, cy - 4, 2);
    }

    /// <summary>Draws the filled radio bullet of a group-checked menu item, centered in the leading column.</summary>
    public static void PaintRadioMark(IGraphics g, Color color, int x, int y, int columnWidth, int rowHeight)
        => g.FillEllipse(color, new(x + ((columnWidth - 6) / 2), y + ((rowHeight - 6) / 2), 6, 6));
}
