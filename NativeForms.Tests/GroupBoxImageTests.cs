using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="GroupBox"/> with an <see cref="GroupBox.Image"/> renders the icon before the caption
/// inside the widened frame gap; the caption shifts right past it, and without an image the classic
/// caption placement stays untouched.
/// </summary>
[TestFixture]
internal sealed class GroupBoxImageTests
{
    private static HeadlessCanvasPeer Realize(GroupBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Image_paints_in_the_gap_and_shifts_the_caption()
    {
        var box = new GroupBox { Text = "Cap", Bounds = new(0, 0, 200, 100), Image = new HeadlessImage(16, 16) };
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill ") && o.EndsWith(" 8,0,49,16")), Is.True, "the border gap widens for icon + gap + caption");
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16 @12,0,16,16")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Cap\"") && o.EndsWith("@32,0")), Is.True, "caption shifts right past the image");
        });
    }

    [Test]
    public void Image_without_a_caption_still_punches_the_gap()
    {
        var box = new GroupBox { Bounds = new(0, 0, 200, 100), Image = new HeadlessImage(16, 16) };
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill ") && o.EndsWith(" 8,0,24,16")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16 @12,0,16,16")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text ")), Is.False);
        });
    }

    [Test]
    public void Without_an_image_the_caption_keeps_its_place()
    {
        var box = new GroupBox { Text = "Cap", Bounds = new(0, 0, 200, 100) };
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image ")), Is.False);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Cap\"") && o.EndsWith("@12,0")), Is.True);
        });
    }

    private static int AtX(RecordingGraphics g, string prefix)
    {
        var op = g.Operations.First(o => o.StartsWith(prefix));
        return int.Parse(op[(op.IndexOf('@') + 1)..].Split(',')[0]);
    }

    [Test]
    public void TextBeforeImage_puts_the_icon_after_the_caption()
    {
        var box = new GroupBox
        {
            Text = "Cap",
            Bounds = new(0, 0, 200, 100),
            Image = new HeadlessImage(16, 16),
            TextImageRelation = TextImageRelation.TextBeforeImage,
        };
        var g = Realize(box).RaisePaint();

        Assert.That(AtX(g, "text \"Cap\""), Is.LessThan(AtX(g, "image 16x16")), "the caption comes first, the icon after it");
    }

    [Test]
    public void ImageBeforeText_is_the_default_order()
    {
        var box = new GroupBox { Text = "Cap", Bounds = new(0, 0, 200, 100), Image = new HeadlessImage(16, 16) };
        var g = Realize(box).RaisePaint();

        Assert.That(AtX(g, "image 16x16"), Is.LessThan(AtX(g, "text \"Cap\"")), "the icon precedes the caption by default");
    }

    [Test]
    public void Image_change_invalidates()
    {
        var box = new GroupBox { Bounds = new(0, 0, 200, 100) };
        var canvas = Realize(box);
        var before = canvas.InvalidateCount;

        box.Image = new HeadlessImage(16, 16);

        Assert.That(canvas.InvalidateCount, Is.EqualTo(before + 1));
    }
}
