using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class RadioGroupTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static IReadOnlyList<HeadlessCanvasPeer> RealizeAll(params OwnerDrawnControl[] controls)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        foreach (var control in controls)
            form.Controls.Add(control);

        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().ToList();
    }

    [Test]
    public void RadioButton_selecting_one_unchecks_its_sibling()
    {
        var first = new RadioButton { Text = "A", Bounds = new(0, 0, 120, 20) };
        var second = new RadioButton { Text = "B", Bounds = new(0, 20, 120, 20) };
        var canvases = RealizeAll(first, second);

        canvases[0].RaiseMouseUp(5, 10);
        Assert.Multiple(() =>
        {
            Assert.That(first.Checked, Is.True);
            Assert.That(second.Checked, Is.False);
        });

        canvases[1].RaiseMouseUp(5, 10);
        Assert.Multiple(() =>
        {
            Assert.That(first.Checked, Is.False, "selecting the second radio unchecks the first");
            Assert.That(second.Checked, Is.True);
        });
    }

    [Test]
    public void RadioButton_selection_raises_click_and_checked_changed()
    {
        var radio = new RadioButton { Bounds = new(0, 0, 120, 20) };
        var checkedChanges = 0;
        var clicks = 0;
        radio.CheckedChanged += (_, _) => ++checkedChanges;
        radio.Click += (_, _) => ++clicks;
        var canvas = RealizeAll(radio)[0];

        canvas.RaiseMouseUp(5, 10);

        Assert.Multiple(() =>
        {
            Assert.That(radio.Checked, Is.True);
            Assert.That(checkedChanges, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void RadioButton_space_key_selects()
    {
        var radio = new RadioButton { Bounds = new(0, 0, 120, 20) };
        var canvas = Realize(radio);

        canvas.RaiseKeyDown(Keys.Space);

        Assert.That(radio.Checked, Is.True);
    }

    [Test]
    public void RadioButton_paints_indicator_and_label()
    {
        var radio = new RadioButton { Text = "Option", Bounds = new(0, 0, 120, 20), Checked = true };
        var canvas = Realize(radio);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("ellipse ")), Is.True, "draws the ring");
            Assert.That(g.Operations.Exists(o => o.StartsWith("fillellipse ")), Is.True, "fills the dot");
            Assert.That(g.DrewText("Option"), Is.True);
        });
    }

    [Test]
    public void GroupBox_paints_caption_and_border()
    {
        var group = new GroupBox { Text = "Settings", Bounds = new(0, 0, 200, 120) };
        var canvas = Realize(group);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Settings"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ")), Is.True, "draws the frame");
        });
    }

    [Test]
    public void ProgressBar_clamps_value_to_range()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100 };

        bar.Value = 200;
        Assert.That(bar.Value, Is.EqualTo(100));

        bar.Value = -50;
        Assert.That(bar.Value, Is.EqualTo(0));
    }

    [Test]
    public void ProgressBar_paints_fill_proportional_to_value()
    {
        // Track inset by 1px on each side => 100px available at width 102.
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20), Minimum = 0, Maximum = 100, Value = 50 };
        var canvas = Realize(bar);

        var g = canvas.RaisePaint();

        // Accent fill covers half of the 100px track: 50px wide, inset at (1, 1), 18px tall.
        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 1,1,50,18"));
    }
}
