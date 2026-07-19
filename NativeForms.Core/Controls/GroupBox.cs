using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn container that frames its children with a themed border and paints its
/// <see cref="Control.Text"/> as a caption over the top-left of that frame, optionally preceded by a
/// small <see cref="Image"/>. Purely decorative — it takes no focus and handles no input; children
/// are added to <see cref="Control.Controls"/> as usual.
/// </summary>
public class GroupBox : OwnerDrawnControl
{
    private const int _CaptionInset = 8;
    private const int _CaptionPadding = 4;

    /// <summary>
    /// An optional icon rendered before the caption in the frame gap through the shared content
    /// layout; the caption shifts right to make room.
    /// </summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        var caption = this.Text;
        var image = this.Image;
        var captionSize = string.IsNullOrEmpty(caption) ? Size.Empty : g.MeasureText(caption, theme.DefaultFont);
        var imageSize = image is null ? Size.Empty : new Size(image.Width, image.Height);
        var contentWidth = captionSize.Width + imageSize.Width + (captionSize.Width > 0 && imageSize.Width > 0 ? ContentLayout.Gap : 0);
        var contentHeight = Math.Max(captionSize.Height, imageSize.Height);

        // Drop the frame so its top edge runs through the caption strip's vertical middle.
        var frameTop = contentHeight / 2;
        g.DrawRectangle(theme.Border, new Rectangle(0, frameTop, this.Width - 1, this.Height - 1 - frameTop));

        if (contentWidth <= 0)
            return;

        // Punch a gap in the top border for icon + caption, then paint them over it.
        var gap = new Rectangle(_CaptionInset, 0, contentWidth + 2 * _CaptionPadding, contentHeight);
        g.FillRectangle(theme.ControlBackground, gap);

        ContentLayout.Arrange(
            new Rectangle(_CaptionInset + _CaptionPadding, 0, contentWidth, contentHeight),
            imageSize,
            captionSize,
            TextImageRelation.ImageBeforeText,
            ContentAlignment.MiddleLeft,
            out var imageRect,
            out var textRect);

        if (image is not null)
            g.DrawImage(image, imageRect);

        if (!string.IsNullOrEmpty(caption))
            g.DrawText(caption, theme.DefaultFont, theme.ControlText, textRect, ContentAlignment.TopLeft);
    }
}
