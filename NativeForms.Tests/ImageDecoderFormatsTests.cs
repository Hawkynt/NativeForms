using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The extended decoders — BMP, PCX, CUR, animated GIF and ANI — plus the format-sniffing
/// <see cref="ImageDecoder.Decode(System.ReadOnlySpan{byte})"/> dispatcher, all exercised through
/// spec-valid fixtures from <see cref="TestImageEncoder"/> and asserted on the decoded ARGB.
/// </summary>
[TestFixture]
internal sealed class ImageDecoderFormatsTests
{
    private const int Black = unchecked((int)0xFF000000);
    private const int Red = unchecked((int)0xFFFF0000);
    private const int Green = unchecked((int)0xFF00FF00);
    private const int Blue = unchecked((int)0xFF0000FF);

    // Index 0 = black, 1 = red, 2 = green, 3 = blue.
    private static byte[] Palette4() => [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];

    [Test]
    public void Bmp_24bit_roundtrips()
    {
        int[] pixels = [Red, Green, Blue, Black];
        var (width, height, argb) = ImageDecoder.DecodeBmp(TestImageEncoder.EncodeBmp24(2, 2, pixels));

        Assert.Multiple(() =>
        {
            Assert.That((width, height), Is.EqualTo((2, 2)));
            Assert.That(argb, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void Pcx_8bit_palette_roundtrips()
    {
        var palette = new byte[768];
        Palette4().CopyTo(palette, 0); // first four entries populated
        byte[] indices = [1, 2, 3, 0]; // red, green, blue, black
        var (width, height, argb) = ImageDecoder.DecodePcx(TestImageEncoder.EncodePcx8(2, 2, indices, palette));

        Assert.Multiple(() =>
        {
            Assert.That((width, height), Is.EqualTo((2, 2)));
            Assert.That(argb, Is.EqualTo(new[] { Red, Green, Blue, Black }));
        });
    }

    [Test]
    public void Cur_decodes_its_best_entry()
    {
        int[] pixels = [Red, Green, Blue, Black];
        var cur = TestImageEncoder.EncodeCur((2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, pixels)));
        var (_, _, argb) = ImageDecoder.DecodeCur(cur);

        Assert.That(argb, Is.EqualTo(pixels));
    }

    [Test]
    public void Gif_still_maps_indices_through_the_palette()
    {
        var gif = TestImageEncoder.EncodeGif(2, 2, [[0, 1, 2, 3]], [10], Palette4());
        var image = ImageDecoder.DecodeGif(gif);

        Assert.Multiple(() =>
        {
            Assert.That(image.Frames, Has.Count.EqualTo(1));
            Assert.That(image.IsAnimated, Is.False);
            Assert.That(image.Frames[0].Argb, Is.EqualTo(new[] { Black, Red, Green, Blue }));
        });
    }

    [Test]
    public void Gif_animation_carries_frames_delays_and_loop_count()
    {
        var frames = new byte[][] { [1, 1, 1, 1], [2, 2, 2, 2] }; // all-red, then all-green
        var gif = TestImageEncoder.EncodeGif(2, 2, frames, [5, 8], Palette4(), loopCount: 3);
        var image = ImageDecoder.DecodeGif(gif);

        Assert.Multiple(() =>
        {
            Assert.That(image.IsAnimated, Is.True);
            Assert.That(image.LoopCount, Is.EqualTo(3));
            Assert.That(image.Frames[0].DelayMilliseconds, Is.EqualTo(50), "5 centiseconds → 50 ms");
            Assert.That(image.Frames[1].DelayMilliseconds, Is.EqualTo(80));
            Assert.That(image.Frames[0].Argb, Is.EqualTo(new[] { Red, Red, Red, Red }));
            Assert.That(image.Frames[1].Argb, Is.EqualTo(new[] { Green, Green, Green, Green }));
        });
    }

    [Test]
    public void Gif_transparency_composites_over_the_previous_frame()
    {
        // Frame 1 fills red; frame 2 paints green only where its index is not the transparent 0.
        var frames = new byte[][] { [1, 1, 1, 1], [0, 2, 0, 2] };
        var gif = TestImageEncoder.EncodeGif(2, 2, frames, [10, 10], Palette4(), transparentIndex: 0);
        var image = ImageDecoder.DecodeGif(gif);

        Assert.That(image.Frames[1].Argb, Is.EqualTo(new[] { Red, Green, Red, Green }), "transparent pixels keep the frame beneath");
    }

    [Test]
    public void Ani_builds_frames_from_icon_steps_with_rate_delays_and_loops_forever()
    {
        var ico1 = TestImageEncoder.EncodeIco((2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, [Red, Red, Red, Red])));
        var ico2 = TestImageEncoder.EncodeIco((2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, [Blue, Blue, Blue, Blue])));
        var ani = TestImageEncoder.EncodeAni([ico1, ico2], rates: [6, 12]);
        var image = ImageDecoder.DecodeAni(ani);

        Assert.Multiple(() =>
        {
            Assert.That(image.Frames, Has.Count.EqualTo(2));
            Assert.That(image.LoopCount, Is.EqualTo(0), "cursor animations loop forever");
            Assert.That(image.Frames[0].DelayMilliseconds, Is.EqualTo(100), "6 jiffies (1/60 s) → 100 ms");
            Assert.That(image.Frames[1].DelayMilliseconds, Is.EqualTo(200));
            Assert.That(image.Frames[0].Argb[0], Is.EqualTo(Red));
            Assert.That(image.Frames[1].Argb[0], Is.EqualTo(Blue));
        });
    }

    [Test]
    public void Decode_dispatches_on_the_magic_bytes()
    {
        var bmp = TestImageEncoder.EncodeBmp24(2, 2, [Red, Green, Blue, Black]);
        var gif = TestImageEncoder.EncodeGif(2, 2, [[1, 1, 1, 1], [2, 2, 2, 2]], [10, 10], Palette4());
        var cur = TestImageEncoder.EncodeCur((2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, [Red, Green, Blue, Black])));

        Assert.Multiple(() =>
        {
            Assert.That(ImageDecoder.Decode(bmp).Frames, Has.Count.EqualTo(1), "BMP is one still frame");
            Assert.That(ImageDecoder.Decode(gif).IsAnimated, Is.True, "the GIF is animated");
            Assert.That(ImageDecoder.Decode(cur).Frames[0].Argb[0], Is.EqualTo(Red), "CUR routed by the type-2 header");
        });
    }
}
