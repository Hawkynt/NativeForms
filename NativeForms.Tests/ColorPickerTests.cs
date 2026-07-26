using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ColorPicker"/> shows a swatch, drops down a mixer on a click, and every edit — a basic
/// swatch, an SV or hue drag — sets the colour and raises the change while the mixer stays open.
/// </summary>
[TestFixture]
internal sealed class ColorPickerTests
{
    // Mixer layout mirrored from the control: the basic swatch grid sits below the SV square, the hex row
    // and its own caption, at 8-pixel padding and 16-pixel cells (RowHeight = 22 in the headless theme).
    private const int _BasicTop = 8 + 160 + 8 + 22 + 6; // _Pad + _SvH + 8 + RowHeight + 6

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
    public void Picking_a_basic_swatch_sets_the_colour_and_keeps_the_mixer_open()
    {
        var picker = Realize(out var backend, out var canvas);
        Color? changed = null;
        picker.SelectedColorChanged += (_, _) => changed = picker.SelectedColor;

        canvas.RaiseMouseDown(60, 13); // open
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(8 + 16 + 8, _BasicTop + 16 + 8); // basic cell index 9 (column 1, row 1) → Red

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.ToArgb(), Is.EqualTo(ColorPicker.Palette[9].ToArgb()));
            Assert.That(picker.SelectedColor.ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
            Assert.That(changed?.ToArgb(), Is.EqualTo(Color.Red.ToArgb()), "the change fired");
            Assert.That(picker.DroppedDown, Is.True, "the mixer stays open for further tuning");
        });
    }

    [Test]
    public void Dragging_the_saturation_value_square_changes_the_colour()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.Red; // hue 0, full S and V
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(8, 8); // top-left of the SV square = saturation 0, value 1 → white

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.R, Is.EqualTo(255));
            Assert.That(picker.SelectedColor.G, Is.EqualTo(255));
            Assert.That(picker.SelectedColor.B, Is.EqualTo(255), "top-left of the square is white");
        });
    }

    [Test]
    public void Dragging_the_hue_bar_sweeps_the_hue()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.Red;
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        // The hue bar runs 0→360 top→bottom; a third of the way down is ~120° (green).
        popup.RaiseMouseDown(8 + 180 + 6 + 9, 8 + (160 / 3));

        Assert.That(picker.SelectedColor.G, Is.GreaterThan(picker.SelectedColor.R), "the hue swept toward green");
    }

    // Numeric layout mirrored from the control (RowHeight = 22): the popup inner width is 280, tabs sit
    // at y=350 and the first channel track at y≈380 spanning x 34..248.
    private const int _TabsTop = 350;
    private const int _Channel0Y = 384;

    [Test]
    public void Clicking_a_numeric_tab_switches_the_channel_readout()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.RoyalBlue;
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(148 + 35, _TabsTop + 11); // the HSV tab (index 2 of 4, each 70 wide from x=8)
        var g = popup.RaisePaint();

        Assert.That(g.DrewText("V"), Is.True, "the HSV tab shows a value channel the RGB tab does not");
    }

    [Test]
    public void Dragging_a_numeric_channel_sets_that_component()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.RoyalBlue; // R = 65
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(247, _Channel0Y); // far right of the R channel track → R = 255

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.R, Is.EqualTo(255));
            Assert.That(picker.SelectedColor.G, Is.EqualTo(105), "green is untouched");
            Assert.That(picker.SelectedColor.B, Is.EqualTo(225), "blue is untouched");
        });
    }

    [Test]
    public void Selecting_the_HSV_tab_shows_a_hue_ring_whose_click_sets_the_hue()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.Red; // hue 0, full S and V
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(168, _TabsTop + 11); // the HSV tab → ring + inner-square view
        popup.RaiseMouseDown(26, 88);             // the ring left of centre (angle 180°) → cyan hue

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.R, Is.LessThan(40), "red is pulled out at 180°");
            Assert.That(picker.SelectedColor.G, Is.GreaterThan(200));
            Assert.That(picker.SelectedColor.B, Is.GreaterThan(200), "green and blue dominate a cyan hue");
        });
    }

    [Test]
    public void Selecting_the_CMYK_tab_shows_a_disc_whose_click_sets_hue_and_saturation()
    {
        var picker = Realize(out var backend, out var canvas);
        picker.SelectedColor = Color.White; // saturation 0
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(232, _TabsTop + 11); // the CMYK tab (index 3 of 4) → disc view
        popup.RaiseMouseDown(26, 88);             // out toward the rim on the left → a saturated cyan

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.R, Is.LessThan(60), "clicking near the rim raises saturation away from white");
            Assert.That(picker.SelectedColor.G, Is.GreaterThan(180));
            Assert.That(picker.SelectedColor.B, Is.GreaterThan(180));
        });
    }

    [Test]
    public void The_eyedropper_samples_a_screen_pixel_into_the_colour()
    {
        var picker = Realize(out var backend, out var canvas);
        backend.ScreenPixel = Color.FromArgb(255, 10, 200, 30);
        canvas.RaiseMouseDown(60, 13); // open
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.RaiseMouseDown(253, 187);              // arm the eyedropper button (top-right of the hex row)
        popup.FireOutsidePress(new Point(500, 500)); // a click anywhere on screen becomes a sample

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedColor.R, Is.EqualTo(10).Within(1));
            Assert.That(picker.SelectedColor.G, Is.EqualTo(200).Within(1));
            Assert.That(picker.SelectedColor.B, Is.EqualTo(30).Within(1));
            Assert.That(picker.DroppedDown, Is.True, "sampling keeps the mixer open");
        });
    }

    [Test]
    public void The_mixer_blits_the_saturation_value_gradient()
    {
        var picker = Realize(out var backend, out var canvas);
        canvas.RaiseMouseDown(60, 13);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        var g = popup.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 180x160")), Is.True, "the SV square is blitted as a bitmap");
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
