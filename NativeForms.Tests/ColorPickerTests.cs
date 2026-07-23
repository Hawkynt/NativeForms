using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ColorPicker"/> shows a swatch, drops down a palette on a click, and picking a cell sets
/// the colour, closes the drop-down and raises the change.
/// </summary>
[TestFixture]
internal sealed class ColorPickerTests
{
    private static ColorPicker Realize(out HeadlessBackend backend, out HeadlessCanvasPeer canvas)
    {
        var picker = new ColorPicker { Bounds = new(0, 0, 120, 26) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, backend);
        canvas = (HeadlessCanvasPeer)picker.Peer!;
        return picker;
    }

    [Test]
    public void Clicking_opens_the_palette_drop_down()
    {
        var picker = Realize(out var backend, out var canvas);

        canvas.RaiseMouseDown(60, 13);

        Assert.Multiple(() =>
        {
            Assert.That(picker.DroppedDown, Is.True);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.True, "a palette popup opened");
        });
    }

    [Test]
    public void Picking_a_swatch_sets_the_colour_closes_and_raises_the_change()
    {
        var picker = Realize(out var backend, out var canvas);
        Color? changed = null;
        picker.SelectedColorChanged += (_, _) => changed = picker.SelectedColor;

        canvas.RaiseMouseDown(60, 13); // open
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(28, 28); // cell index 9 (column 1, row 1) → Red

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor, Is.EqualTo(ColorPicker.Palette[9]));
            Assert.That(picker.SelectedColor, Is.EqualTo(Color.Red));
            Assert.That(changed, Is.EqualTo(Color.Red), "the change fired");
            Assert.That(picker.DroppedDown, Is.False, "the drop-down closed");
        });
    }

    [Test]
    public void The_face_paints_the_selected_colour_swatch()
    {
        var picker = Realize(out _, out var canvas);
        picker.SelectedColor = Color.Red;

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FFFF0000")), Is.True, "the swatch is the selected colour");
    }
}
