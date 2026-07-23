using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Decoding a cursor image and building a custom <see cref="Cursor"/> from it: the pixels round-trip,
/// a CUR's hotspot comes back, and the cursor assigned to a control is a <see cref="CursorKind.Custom"/>
/// carrying that data.
/// </summary>
[TestFixture]
internal sealed class CustomCursorTests
{
    private const int Red = unchecked((int)0xFFFF0000);
    private const int Blue = unchecked((int)0xFF0000FF);

    [Test]
    public void DecodeCursor_returns_a_curs_hotspot()
    {
        var cur = TestImageEncoder.EncodeCurWithHotspot(3, 5, (8, 8, TestImageEncoder.EncodeIcoBmp32(8, 8, new int[64])));

        var (width, height, _, hotspotX, hotspotY) = ImageDecoder.DecodeCursor(cur);

        Assert.Multiple(() =>
        {
            Assert.That((width, height), Is.EqualTo((8, 8)));
            Assert.That((hotspotX, hotspotY), Is.EqualTo((3, 5)), "the CUR hotspot is read from the directory entry");
        });
    }

    [Test]
    public void FromBytes_builds_a_custom_cursor_with_the_pixels_and_hotspot()
    {
        int[] pixels = [Red, Blue, Blue, Red];
        var cur = TestImageEncoder.EncodeCurWithHotspot(1, 1, (2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, pixels)));

        var cursor = Cursor.FromBytes(cur);

        Assert.Multiple(() =>
        {
            Assert.That(cursor.Kind, Is.EqualTo(CursorKind.Custom));
            Assert.That((cursor.Width, cursor.Height), Is.EqualTo((2, 2)));
            Assert.That((cursor.HotspotX, cursor.HotspotY), Is.EqualTo((1, 1)));
            Assert.That(cursor.Pixels, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void A_still_image_becomes_a_top_left_hotspot_cursor()
    {
        var png = TestImageEncoder.EncodeRgba(2, 2, [Red, Red, Red, Red]);
        var cursor = Cursor.FromBytes(png);

        Assert.Multiple(() =>
        {
            Assert.That(cursor.Kind, Is.EqualTo(CursorKind.Custom));
            Assert.That((cursor.HotspotX, cursor.HotspotY), Is.EqualTo((0, 0)), "a still image pins the hotspot top-left");
        });
    }

    [Test]
    public void Assigning_a_custom_cursor_reaches_the_peer()
    {
        int[] pixels = [Red, Blue, Blue, Red];
        var cur = TestImageEncoder.EncodeCur((2, 2, TestImageEncoder.EncodeIcoBmp32(2, 2, pixels)));
        var button = new Button { Bounds = new(0, 0, 80, 24), Cursor = Cursor.FromBytes(cur) };

        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);

        Assert.That(button.Cursor.Kind, Is.EqualTo(CursorKind.Custom), "the custom cursor is set on the control");
    }
}
