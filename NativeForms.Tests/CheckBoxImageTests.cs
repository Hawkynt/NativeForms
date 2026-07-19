using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="CheckBox"/> with an <see cref="CheckBox.Image"/> paints the icon between the check
/// square and the caption through the shared content layout, shifting the text right; without one the
/// classic text placement stays untouched.
/// </summary>
[TestFixture]
internal sealed class CheckBoxImageTests
{
    private static HeadlessCanvasPeer Realize(CheckBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Image_paints_after_the_glyph_and_shifts_the_text()
    {
        var box = new CheckBox { Text = "Go", Bounds = new(0, 0, 200, 30), Image = new HeadlessImage(16, 16) };
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 16x16 @20,7,16,16"), "icon sits past the 14px glyph + 6px gap, vertically centered");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Go\"") && o.EndsWith("@40,7")), Is.True, "text shifts right past the image");
        });
    }

    [Test]
    public void Without_an_image_the_text_keeps_its_place()
    {
        var box = new CheckBox { Text = "Go", Bounds = new(0, 0, 200, 30) };
        var canvas = Realize(box);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image ")), Is.False);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Go\"") && o.EndsWith("@20,0")), Is.True);
        });
    }

    [Test]
    public void Image_change_invalidates()
    {
        var box = new CheckBox { Bounds = new(0, 0, 200, 30) };
        var canvas = Realize(box);
        var before = canvas.InvalidateCount;

        box.Image = new HeadlessImage(16, 16);

        Assert.That(canvas.InvalidateCount, Is.EqualTo(before + 1));
    }
}
