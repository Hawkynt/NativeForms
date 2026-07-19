using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn container that frames its children with a themed border and paints its
/// <see cref="Control.Text"/> as a caption over the top-left of that frame. Purely decorative — it
/// takes no focus and handles no input; children are added to <see cref="Control.Controls"/> as usual.
/// </summary>
public class GroupBox : OwnerDrawnControl
{
    private const int _CaptionInset = 8;
    private const int _CaptionPadding = 4;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        var caption = this.Text;
        var captionSize = string.IsNullOrEmpty(caption) ? Size.Empty : g.MeasureText(caption, theme.DefaultFont);

        // Drop the frame so its top edge runs through the caption's vertical middle.
        var frameTop = captionSize.Height / 2;
        g.DrawRectangle(theme.Border, new Rectangle(0, frameTop, this.Width - 1, this.Height - 1 - frameTop));

        if (string.IsNullOrEmpty(caption))
            return;

        // Punch a gap in the top border for the caption, then paint the text over it.
        var gap = new Rectangle(_CaptionInset, 0, captionSize.Width + 2 * _CaptionPadding, captionSize.Height);
        g.FillRectangle(theme.ControlBackground, gap);

        var textRect = new Rectangle(_CaptionInset + _CaptionPadding, 0, captionSize.Width, captionSize.Height);
        g.DrawText(caption, theme.DefaultFont, theme.ControlText, textRect, ContentAlignment.TopLeft);
    }
}
