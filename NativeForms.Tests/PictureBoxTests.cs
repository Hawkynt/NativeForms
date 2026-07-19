using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="PictureBox"/> must place its image per <see cref="PictureBoxSizeMode"/> — native size
/// top-left, stretched, centered, or aspect-fit zoomed — clip it to the client area, and frame itself
/// when <see cref="PictureBox.BorderStyle"/> asks for it.
/// </summary>
[TestFixture]
internal sealed class PictureBoxTests
{
    private static HeadlessCanvasPeer Realize(PictureBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    private static PictureBox Box(int imageWidth, int imageHeight, PictureBoxSizeMode mode) => new()
    {
        Bounds = new(0, 0, 100, 80),
        Image = new HeadlessImage(imageWidth, imageHeight),
        SizeMode = mode,
    };

    [Test]
    public void Normal_paints_the_image_at_native_size_in_the_top_left()
    {
        var canvas = Realize(Box(40, 30, PictureBoxSizeMode.Normal));

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 40x30 @0,0,40,30"));
    }

    [Test]
    public void StretchImage_fills_the_client_area()
    {
        var canvas = Realize(Box(40, 30, PictureBoxSizeMode.StretchImage));

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 40x30 @0,0,100,80"));
    }

    [Test]
    public void CenterImage_centers_at_native_size()
    {
        var canvas = Realize(Box(40, 30, PictureBoxSizeMode.CenterImage));

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 40x30 @30,25,40,30"));
    }

    [Test]
    public void Zoom_letterboxes_a_wide_image()
    {
        var canvas = Realize(Box(50, 25, PictureBoxSizeMode.Zoom));

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 50x25 @0,15,100,50"));
    }

    [Test]
    public void Zoom_pillarboxes_a_tall_image()
    {
        var canvas = Realize(Box(20, 40, PictureBoxSizeMode.Zoom));

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 20x40 @30,0,40,80"));
    }

    [Test]
    public void The_image_is_clipped_to_the_client_area()
    {
        var canvas = Realize(Box(200, 160, PictureBoxSizeMode.Normal));

        var g = canvas.RaisePaint();

        var clip = g.Operations.IndexOf("clip 0,0,100,80");
        var image = g.Operations.IndexOf("image 200x160 @0,0,200,160");
        var unclip = g.Operations.IndexOf("unclip");
        Assert.That(clip, Is.GreaterThanOrEqualTo(0).And.LessThan(image), "image draws inside the pushed clip");
        Assert.That(unclip, Is.GreaterThan(image), "the clip is popped afterwards");
    }

    [Test]
    public void FixedSingle_draws_the_themed_border()
    {
        var box = Box(40, 30, PictureBoxSizeMode.Normal);
        box.BorderStyle = BorderStyle.FixedSingle;
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("rect ") && o.EndsWith(" 0,0,99,79")), Is.True);
    }

    [Test]
    public void Without_an_image_only_the_background_paints()
    {
        var canvas = Realize(new PictureBox { Bounds = new(0, 0, 100, 80) });

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill ")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("image ")), Is.False);
        });
    }

    [Test]
    public void Property_changes_invalidate()
    {
        var box = Box(40, 30, PictureBoxSizeMode.Normal);
        var canvas = Realize(box);
        var before = canvas.InvalidateCount;

        box.Image = new HeadlessImage(8, 8);
        box.SizeMode = PictureBoxSizeMode.Zoom;
        box.BorderStyle = BorderStyle.FixedSingle;

        Assert.That(canvas.InvalidateCount, Is.EqualTo(before + 3));
    }
}
