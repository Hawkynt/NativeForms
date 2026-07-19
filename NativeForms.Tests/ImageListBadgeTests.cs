using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ImageList.AddBadged"/> must compose a badge into a copy of a base image with a simple
/// alpha-over blend — opaque badge pixels overwrite, transparent ones preserve the base,
/// semi-transparent ones mix — placed at the requested corner (bottom-right by default), leaving the
/// base entry untouched.
/// </summary>
[TestFixture]
internal sealed class ImageListBadgeTests
{
    private const int _Blue = unchecked((int)0xFF0000FF);
    private const int _Red = unchecked((int)0xFFFF0000);

    /// <summary>A 4×4 list holding one solid blue base image at index 0.</summary>
    private static ImageList CreateList()
    {
        var list = new ImageList(4);
        var pixels = new int[16];
        Array.Fill(pixels, _Blue);
        list.Add(pixels);
        return list;
    }

    [Test]
    public void Opaque_badge_overwrites_the_bottom_right_corner_by_default()
    {
        using var list = CreateList();

        var index = list.AddBadged(0, [_Red, _Red, _Red, _Red], 2, 2);

        Assert.That(index, Is.EqualTo(1));
        var pixels = list.GetPixels(index);
        Assert.Multiple(() =>
        {
            Assert.That(pixels[0], Is.EqualTo(_Blue), "top-left stays base");
            Assert.That(pixels[(2 * 4) + 2], Is.EqualTo(_Red), "badge origin");
            Assert.That(pixels[(3 * 4) + 3], Is.EqualTo(_Red), "badge corner");
            Assert.That(pixels[(3 * 4) + 1], Is.EqualTo(_Blue), "left of the badge stays base");
        });
    }

    [Test]
    public void Transparent_badge_pixels_preserve_the_base()
    {
        using var list = CreateList();

        var index = list.AddBadged(0, [0, _Red, 0, 0], 2, 2);

        var pixels = list.GetPixels(index);
        Assert.Multiple(() =>
        {
            Assert.That(pixels[(2 * 4) + 2], Is.EqualTo(_Blue), "fully transparent pixel keeps the base");
            Assert.That(pixels[(2 * 4) + 3], Is.EqualTo(_Red), "opaque neighbor overwrites");
            Assert.That(pixels[(3 * 4) + 2], Is.EqualTo(_Blue));
            Assert.That(pixels[(3 * 4) + 3], Is.EqualTo(_Blue));
        });
    }

    [Test]
    public void Semi_transparent_badge_blends_alpha_over()
    {
        using var list = CreateList();

        var index = list.AddBadged(0, [unchecked((int)0x80FF0000)], 1, 1);

        // 50% red over opaque blue: A = 128 + 255·127/255 = 255, R = 255·128/255 = 128,
        // B = 255·255·127/255/255 = 127.
        Assert.That(list.GetPixels(index)[(3 * 4) + 3], Is.EqualTo(unchecked((int)0xFF80007F)));
    }

    [Test]
    public void Corner_placement_honors_the_alignment()
    {
        using var list = CreateList();

        var topLeft = list.AddBadged(0, [_Red], 1, 1, ContentAlignment.TopLeft);
        var topRight = list.AddBadged(0, [_Red], 1, 1, ContentAlignment.TopRight);
        var bottomLeft = list.AddBadged(0, [_Red], 1, 1, ContentAlignment.BottomLeft);
        var center = list.AddBadged(0, [_Red], 1, 1, ContentAlignment.MiddleCenter);

        Assert.Multiple(() =>
        {
            Assert.That(list.GetPixels(topLeft)[0], Is.EqualTo(_Red));
            Assert.That(list.GetPixels(topRight)[3], Is.EqualTo(_Red));
            Assert.That(list.GetPixels(bottomLeft)[3 * 4], Is.EqualTo(_Red));
            Assert.That(list.GetPixels(center)[(1 * 4) + 1], Is.EqualTo(_Red), "a 1×1 badge centers at (1,1) on a 4×4 image");
        });
    }

    [Test]
    public void Base_image_stays_untouched()
    {
        using var list = CreateList();

        list.AddBadged(0, [_Red, _Red, _Red, _Red], 2, 2);

        var basePixels = list.GetPixels(0);
        for (var i = 0; i < 16; ++i)
            Assert.That(basePixels[i], Is.EqualTo(_Blue));
    }

    [Test]
    public void Rejects_invalid_arguments()
    {
        using var list = CreateList();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => list.AddBadged(1, [_Red], 1, 1), "unknown base index");
            Assert.Throws<ArgumentOutOfRangeException>(() => list.AddBadged(0, [], 0, 1), "empty badge");
            Assert.Throws<ArgumentOutOfRangeException>(() => list.AddBadged(0, new int[5 * 4], 5, 4), "badge wider than the image");
            Assert.Throws<ArgumentException>(() => list.AddBadged(0, [_Red], 2, 2), "pixel count mismatch");
        });
    }
}
