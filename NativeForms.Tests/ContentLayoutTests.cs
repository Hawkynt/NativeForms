using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ContentLayout"/> is the single source of icon+text geometry (PRD §5): image and text
/// stack per <see cref="TextImageRelation"/>, the pair anchors per <see cref="ContentAlignment"/>,
/// a fixed gap separates them only when both exist, and both rectangles clip to the bounds.
/// </summary>
[TestFixture]
internal sealed class ContentLayoutTests
{
    private static readonly Rectangle _Bounds = new(0, 0, 100, 40);
    private static readonly Size _ImageSize = new(16, 16);
    private static readonly Size _TextSize = new(30, 10);

    // Horizontal relations swept over the left/center/right anchors (block: 50×16 at y = 12)…
    [TestCase(TextImageRelation.ImageBeforeText, ContentAlignment.MiddleLeft, 0, 12, 20, 15)]
    [TestCase(TextImageRelation.ImageBeforeText, ContentAlignment.MiddleCenter, 25, 12, 45, 15)]
    [TestCase(TextImageRelation.ImageBeforeText, ContentAlignment.MiddleRight, 50, 12, 70, 15)]
    [TestCase(TextImageRelation.TextBeforeImage, ContentAlignment.MiddleLeft, 34, 12, 0, 15)]
    [TestCase(TextImageRelation.TextBeforeImage, ContentAlignment.MiddleCenter, 59, 12, 25, 15)]
    [TestCase(TextImageRelation.TextBeforeImage, ContentAlignment.MiddleRight, 84, 12, 50, 15)]
    // …vertical relations over the top/middle/bottom anchors (block: 30×30 at x = 35).
    [TestCase(TextImageRelation.ImageAboveText, ContentAlignment.TopCenter, 42, 0, 35, 20)]
    [TestCase(TextImageRelation.ImageAboveText, ContentAlignment.MiddleCenter, 42, 5, 35, 25)]
    [TestCase(TextImageRelation.ImageAboveText, ContentAlignment.BottomCenter, 42, 10, 35, 30)]
    [TestCase(TextImageRelation.TextAboveImage, ContentAlignment.TopCenter, 42, 14, 35, 0)]
    [TestCase(TextImageRelation.TextAboveImage, ContentAlignment.MiddleCenter, 42, 19, 35, 5)]
    [TestCase(TextImageRelation.TextAboveImage, ContentAlignment.BottomCenter, 42, 24, 35, 10)]
    // …and the vertical relations against the left/right anchors, completing the 4×3 matrix.
    [TestCase(TextImageRelation.ImageAboveText, ContentAlignment.MiddleLeft, 7, 5, 0, 25)]
    [TestCase(TextImageRelation.ImageAboveText, ContentAlignment.MiddleRight, 77, 5, 70, 25)]
    [TestCase(TextImageRelation.TextAboveImage, ContentAlignment.MiddleLeft, 7, 19, 0, 5)]
    [TestCase(TextImageRelation.TextAboveImage, ContentAlignment.MiddleRight, 77, 19, 70, 5)]
    public void Relation_and_alignment_place_both_rectangles(
        TextImageRelation relation,
        ContentAlignment alignment,
        int imageX,
        int imageY,
        int textX,
        int textY)
    {
        ContentLayout.Arrange(_Bounds, _ImageSize, _TextSize, relation, alignment, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(new Rectangle(imageX, imageY, _ImageSize.Width, _ImageSize.Height)));
            Assert.That(textRect, Is.EqualTo(new Rectangle(textX, textY, _TextSize.Width, _TextSize.Height)));
        });
    }

    [Test]
    public void Overlay_anchors_image_and_text_independently()
    {
        ContentLayout.Arrange(_Bounds, _ImageSize, _TextSize, TextImageRelation.Overlay, ContentAlignment.MiddleCenter, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(new Rectangle(42, 12, 16, 16)));
            Assert.That(textRect, Is.EqualTo(new Rectangle(35, 15, 30, 10)));
        });
    }

    [Test]
    public void Image_only_anchors_without_a_gap()
    {
        ContentLayout.Arrange(_Bounds, _ImageSize, Size.Empty, TextImageRelation.ImageBeforeText, ContentAlignment.BottomRight, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(new Rectangle(84, 24, 16, 16)));
            Assert.That(textRect, Is.EqualTo(Rectangle.Empty));
        });
    }

    [Test]
    public void Text_only_anchors_without_a_gap()
    {
        ContentLayout.Arrange(_Bounds, Size.Empty, _TextSize, TextImageRelation.ImageBeforeText, ContentAlignment.TopLeft, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(Rectangle.Empty));
            Assert.That(textRect, Is.EqualTo(new Rectangle(0, 0, 30, 10)));
        });
    }

    [Test]
    public void Offset_bounds_shift_the_whole_block()
    {
        ContentLayout.Arrange(new(10, 20, 100, 40), _ImageSize, _TextSize, TextImageRelation.ImageBeforeText, ContentAlignment.MiddleLeft, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(new Rectangle(10, 32, 16, 16)));
            Assert.That(textRect, Is.EqualTo(new Rectangle(30, 35, 30, 10)));
        });
    }

    [Test]
    public void Overflowing_parts_are_clipped_to_the_bounds()
    {
        ContentLayout.Arrange(new(0, 0, 20, 20), _ImageSize, _TextSize, TextImageRelation.ImageBeforeText, ContentAlignment.MiddleLeft, out var imageRect, out var textRect);

        Assert.Multiple(() =>
        {
            Assert.That(imageRect, Is.EqualTo(new Rectangle(0, 2, 16, 16)), "the image fits and stays whole");
            Assert.That(textRect.Width, Is.Zero, "the text starts past the right edge and clips away");
        });
    }
}
