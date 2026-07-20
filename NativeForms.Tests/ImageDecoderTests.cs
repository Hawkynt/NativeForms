using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The pure-managed PNG/ICO decoder, exercised through spec-valid fixtures produced by the
/// test-only <see cref="TestImageEncoder"/>: every supported color type, all five scanline filters,
/// ICO entry selection, and the <see cref="ImageList"/> conveniences including nearest-neighbor
/// resampling.
/// </summary>
[TestFixture]
internal sealed class ImageDecoderTests
{
    /// <summary>A 4×4 gradient with varying alpha — distinct bytes in every channel and row.</summary>
    private static int[] SamplePixels()
    {
        var pixels = new int[16];
        for (var i = 0; i < pixels.Length; ++i)
            pixels[i] = ((0x10 + (i * 14)) << 24) | ((i * 16) << 16) | ((255 - (i * 13)) << 8) | (i * 7);

        return pixels;
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    [TestCase((byte)2)]
    [TestCase((byte)3)]
    [TestCase((byte)4)]
    public void Png_rgba_roundtrips_through_every_filter(byte filter)
    {
        var pixels = SamplePixels();

        var (width, height, argb) = ImageDecoder.DecodePng(TestImageEncoder.EncodeRgba(4, 4, pixels, filter));

        Assert.Multiple(() =>
        {
            Assert.That((width, height), Is.EqualTo((4, 4)));
            Assert.That(argb, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void Png_rgb_decodes_as_opaque()
    {
        var pixels = SamplePixels();

        var (_, _, argb) = ImageDecoder.DecodePng(TestImageEncoder.EncodeRgb(4, 4, pixels, filter: 4));

        var expected = new int[pixels.Length];
        for (var i = 0; i < pixels.Length; ++i)
            expected[i] = pixels[i] | unchecked((int)0xFF000000);
        Assert.That(argb, Is.EqualTo(expected));
    }

    [Test]
    public void Png_grayscale_decodes_to_gray_pixels()
    {
        byte[] gray = [0, 64, 128, 255];

        var (_, _, argb) = ImageDecoder.DecodePng(TestImageEncoder.EncodeGrayscale(2, 2, gray, filter: 1));

        Assert.That(argb, Is.EqualTo(new[]
        {
            unchecked((int)0xFF000000), unchecked((int)0xFF404040),
            unchecked((int)0xFF808080), unchecked((int)0xFFFFFFFF),
        }));
    }

    [Test]
    public void Png_grayscale_alpha_decodes_both_channels()
    {
        byte[] grayAlpha = [10, 255, 20, 128, 30, 0, 40, 64];

        var (_, _, argb) = ImageDecoder.DecodePng(TestImageEncoder.EncodeGrayscaleAlpha(2, 2, grayAlpha, filter: 2));

        Assert.That(argb, Is.EqualTo(new[]
        {
            unchecked((int)0xFF0A0A0A), unchecked((int)0x80141414),
            0x001E1E1E, 0x40282828,
        }));
    }

    [Test]
    public void Png_palette_decodes_through_the_palette()
    {
        byte[] indices = [0, 1, 2, 1];
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];

        var (_, _, argb) = ImageDecoder.DecodePng(TestImageEncoder.EncodePalette(2, 2, indices, palette, filter: 3));

        Assert.That(argb, Is.EqualTo(new[]
        {
            unchecked((int)0xFFFF0000), unchecked((int)0xFF00FF00),
            unchecked((int)0xFF0000FF), unchecked((int)0xFF00FF00),
        }));
    }

    [Test]
    public void Png_rejects_a_missing_signature()
        => Assert.Throws<FormatException>(() => ImageDecoder.DecodePng(new byte[64]));

    [Test]
    public void Png_rejects_16_bit_channels()
    {
        var png = TestImageEncoder.EncodeRgba(4, 4, SamplePixels());
        png[24] = 16; // the IHDR bit-depth byte

        Assert.Throws<FormatException>(() => ImageDecoder.DecodePng(png));
    }

    [Test]
    public void Png_rejects_interlaced_images()
    {
        var png = TestImageEncoder.EncodeRgba(4, 4, SamplePixels());
        png[28] = 1; // the IHDR interlace byte (Adam7)

        Assert.Throws<FormatException>(() => ImageDecoder.DecodePng(png));
    }

    [Test]
    public void Ico_decodes_an_embedded_png_entry()
    {
        var pixels = SamplePixels();
        var ico = TestImageEncoder.EncodeIco((4, 4, TestImageEncoder.EncodeRgba(4, 4, pixels)));

        var (width, height, argb) = ImageDecoder.DecodeIco(ico);

        Assert.Multiple(() =>
        {
            Assert.That((width, height), Is.EqualTo((4, 4)));
            Assert.That(argb, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void Ico_decodes_a_32_bit_bitmap_entry_with_its_alpha()
    {
        var pixels = SamplePixels();
        var ico = TestImageEncoder.EncodeIco((4, 4, TestImageEncoder.EncodeIcoBmp32(4, 4, pixels)));

        var (_, _, argb) = ImageDecoder.DecodeIco(ico);

        Assert.That(argb, Is.EqualTo(pixels));
    }

    [Test]
    public void Ico_decodes_a_24_bit_bitmap_entry_using_the_and_mask()
    {
        int[] pixels =
        [
            unchecked((int)0xFF102030), 0x00000000,
            unchecked((int)0xFFFFFFFF), unchecked((int)0xFF0000FF),
        ];
        var ico = TestImageEncoder.EncodeIco((2, 2, TestImageEncoder.EncodeIcoBmp24(2, 2, pixels)));

        var (_, _, argb) = ImageDecoder.DecodeIco(ico);

        Assert.That(argb, Is.EqualTo(pixels), "opaque pixels come back opaque, masked pixels fully transparent");
    }

    [Test]
    public void Ico_picks_the_entry_closest_to_the_preferred_size()
    {
        var small = new int[16];
        var large = new int[64];
        Array.Fill(small, unchecked((int)0xFF111111));
        Array.Fill(large, unchecked((int)0xFF222222));
        var ico = TestImageEncoder.EncodeIco(
            (4, 4, TestImageEncoder.EncodeIcoBmp32(4, 4, small)),
            (8, 8, TestImageEncoder.EncodeIcoBmp32(8, 8, large)));

        var (width, _, _) = ImageDecoder.DecodeIco(ico, preferredSize: 5);

        Assert.That(width, Is.EqualTo(4));
    }

    [Test]
    public void Ico_picks_the_largest_entry_without_a_preference()
    {
        var small = new int[16];
        var large = new int[64];
        var ico = TestImageEncoder.EncodeIco(
            (4, 4, TestImageEncoder.EncodeIcoBmp32(4, 4, small)),
            (8, 8, TestImageEncoder.EncodeIcoBmp32(8, 8, large)));

        var (width, _, _) = ImageDecoder.DecodeIco(ico);

        Assert.That(width, Is.EqualTo(8));
    }

    [Test]
    public void Ico_rejects_non_icon_data()
        => Assert.Throws<FormatException>(() => ImageDecoder.DecodeIco(new byte[64]));

    [Test]
    public void ImageList_AddPng_stores_the_decoded_pixels()
    {
        var pixels = SamplePixels();
        var list = new ImageList(new Size(4, 4));

        var index = list.AddPng(TestImageEncoder.EncodeRgba(4, 4, pixels));

        Assert.Multiple(() =>
        {
            Assert.That(index, Is.Zero);
            Assert.That(list.GetPixels(0), Is.EqualTo(pixels));
        });
    }

    [Test]
    public void ImageList_AddPng_resamples_to_the_image_size_with_nearest_neighbor()
    {
        // A 2×2 checkerboard blown up to 4×4: nearest-neighbor duplicates each source pixel into a
        // 2×2 block, with no blended in-between colors.
        int[] board =
        [
            unchecked((int)0xFF000000), unchecked((int)0xFFFFFFFF),
            unchecked((int)0xFFFFFFFF), unchecked((int)0xFF000000),
        ];
        var list = new ImageList(new Size(4, 4));

        list.AddPng(TestImageEncoder.EncodeRgba(2, 2, board));

        var black = unchecked((int)0xFF000000);
        var white = unchecked((int)0xFFFFFFFF);
        Assert.That(list.GetPixels(0), Is.EqualTo(new[]
        {
            black, black, white, white,
            black, black, white, white,
            white, white, black, black,
            white, white, black, black,
        }));
    }

    [Test]
    public void ImageList_AddIco_picks_the_matching_entry()
    {
        var small = new int[16];
        var large = new int[64];
        Array.Fill(small, unchecked((int)0xFF111111));
        Array.Fill(large, unchecked((int)0xFF222222));
        var list = new ImageList(new Size(4, 4));

        list.AddIco(TestImageEncoder.EncodeIco(
            (8, 8, TestImageEncoder.EncodeIcoBmp32(8, 8, large)),
            (4, 4, TestImageEncoder.EncodeIcoBmp32(4, 4, small))));

        Assert.That(list.GetPixels(0), Is.EqualTo(small), "the 4×4 entry matches ImageSize exactly");
    }
}
