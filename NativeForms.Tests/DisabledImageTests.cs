using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A disabled control draws an image's <see cref="IImage.DisabledImage"/> (its greyed variant) rather
/// than computing the grey itself, so the disabled look lives with the image.
/// </summary>
[TestFixture]
internal sealed class DisabledImageTests
{
    /// <summary>A stand-in image whose <see cref="DisabledImage"/> is a distinct size, so the recorder
    /// tells which one was drawn.</summary>
    private sealed class FakeImage(int width, int height, IImage? disabled = null) : IImage
    {
        public int Width => width;
        public int Height => height;
        public IImage? DisabledImage => disabled;
        public void Dispose() { }
    }

    private static RecordingGraphics PaintCheckBox(IImage image, bool enabled)
    {
        var box = new CheckBox { Bounds = new(0, 0, 120, 24), Text = "x", Image = image, Enabled = enabled };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single().RaisePaint();
    }

    [Test]
    public void An_enabled_control_draws_the_image_and_a_disabled_one_draws_its_DisabledImage()
    {
        var disabled = new FakeImage(3, 3);
        var normal = new FakeImage(2, 2, disabled);

        var enabled = PaintCheckBox(normal, enabled: true);
        var greyed = PaintCheckBox(normal, enabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.Operations.Exists(o => o.StartsWith("image 2x2")), Is.True, "an enabled control draws the image itself");
            Assert.That(greyed.Operations.Exists(o => o.StartsWith("image 3x3")), Is.True, "a disabled control draws the image's DisabledImage");
        });
    }

    [Test]
    public void A_disabled_control_falls_back_to_the_image_when_it_has_no_DisabledImage()
    {
        var normal = new FakeImage(2, 2); // no DisabledImage

        var greyed = PaintCheckBox(normal, enabled: false);

        Assert.That(greyed.Operations.Exists(o => o.StartsWith("image 2x2")), Is.True, "with no DisabledImage the control draws the image unchanged");
    }
}
