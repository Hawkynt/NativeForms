using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The shared icon+text geometry used by every control that renders an image next to text (PRD §5):
/// image and text sizes are stacked per <see cref="TextImageRelation"/> into one block, the block is
/// anchored per <see cref="ContentAlignment"/>, and both rectangles are clipped to the given bounds.
/// Pure geometry — the caller measures the text and does the drawing — so image placement is
/// implemented once and behaves identically everywhere.
/// </summary>
internal static class ContentLayout
{
    /// <summary>The pixel gap between the image and the text when both are present.</summary>
    internal const int Gap = 4;

    /// <summary>
    /// Computes where the image and the text sit within <paramref name="bounds"/>. An empty
    /// <paramref name="imageSize"/> or <paramref name="textSize"/> yields an empty rectangle for that
    /// part (and no gap); with <see cref="TextImageRelation.Overlay"/> both parts anchor independently.
    /// </summary>
    public static void Arrange(
        Rectangle bounds,
        Size imageSize,
        Size textSize,
        TextImageRelation relation,
        ContentAlignment alignment,
        out Rectangle imageRect,
        out Rectangle textRect)
    {
        var hasImage = imageSize.Width > 0 && imageSize.Height > 0;
        var hasText = textSize.Width > 0 && textSize.Height > 0;

        if (!hasImage || !hasText || relation == TextImageRelation.Overlay)
        {
            imageRect = hasImage ? Rectangle.Intersect(Anchor(bounds, imageSize, alignment), bounds) : Rectangle.Empty;
            textRect = hasText ? Rectangle.Intersect(Anchor(bounds, textSize, alignment), bounds) : Rectangle.Empty;
            return;
        }

        if (relation is TextImageRelation.ImageBeforeText or TextImageRelation.TextBeforeImage)
        {
            var block = Anchor(
                bounds,
                new Size(imageSize.Width + Gap + textSize.Width, Math.Max(imageSize.Height, textSize.Height)),
                alignment);

            var (leading, trailing) = relation == TextImageRelation.ImageBeforeText
                ? (imageSize, textSize)
                : (textSize, imageSize);

            var first = new Rectangle(block.X, block.Y + (block.Height - leading.Height) / 2, leading.Width, leading.Height);
            var second = new Rectangle(block.X + leading.Width + Gap, block.Y + (block.Height - trailing.Height) / 2, trailing.Width, trailing.Height);
            (imageRect, textRect) = relation == TextImageRelation.ImageBeforeText ? (first, second) : (second, first);
        }
        else
        {
            var block = Anchor(
                bounds,
                new Size(Math.Max(imageSize.Width, textSize.Width), imageSize.Height + Gap + textSize.Height),
                alignment);

            var (upper, lower) = relation == TextImageRelation.ImageAboveText
                ? (imageSize, textSize)
                : (textSize, imageSize);

            var first = new Rectangle(block.X + (block.Width - upper.Width) / 2, block.Y, upper.Width, upper.Height);
            var second = new Rectangle(block.X + (block.Width - lower.Width) / 2, block.Y + upper.Height + Gap, lower.Width, lower.Height);
            (imageRect, textRect) = relation == TextImageRelation.ImageAboveText ? (first, second) : (second, first);
        }

        imageRect = Rectangle.Intersect(imageRect, bounds);
        textRect = Rectangle.Intersect(textRect, bounds);
    }

    /// <summary>Anchors a box of <paramref name="size"/> at one of the nine alignment points of <paramref name="bounds"/>.</summary>
    private static Rectangle Anchor(Rectangle bounds, Size size, ContentAlignment alignment)
    {
        var x = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => bounds.X + (bounds.Width - size.Width) / 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => bounds.Right - size.Width,
            _ => bounds.X,
        };

        var y = alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => bounds.Y + (bounds.Height - size.Height) / 2,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => bounds.Bottom - size.Height,
            _ => bounds.Y,
        };

        return new(x, y, size.Width, size.Height);
    }
}
