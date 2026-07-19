using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="RadioButton"/> with an <see cref="RadioButton.Image"/> paints the icon between the
/// ring and the caption through the shared content layout, shifting the text right; without one the
/// classic text placement stays untouched.
/// </summary>
[TestFixture]
internal sealed class RadioButtonImageTests
{
    private static HeadlessCanvasPeer Realize(RadioButton radio)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(radio);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Image_paints_after_the_ring_and_shifts_the_text()
    {
        var radio = new RadioButton { Text = "Go", Bounds = new(0, 0, 200, 30), Image = new HeadlessImage(16, 16) };
        var canvas = Realize(radio);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 16x16 @20,7,16,16"), "icon sits past the 14px ring + 6px gap, vertically centered");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Go\"") && o.EndsWith("@40,7")), Is.True, "text shifts right past the image");
        });
    }

    [Test]
    public void Without_an_image_the_text_keeps_its_place()
    {
        var radio = new RadioButton { Text = "Go", Bounds = new(0, 0, 200, 30) };
        var canvas = Realize(radio);

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
        var radio = new RadioButton { Bounds = new(0, 0, 200, 30) };
        var canvas = Realize(radio);
        var before = canvas.InvalidateCount;

        radio.Image = new HeadlessImage(16, 16);

        Assert.That(canvas.InvalidateCount, Is.EqualTo(before + 1));
    }
}
