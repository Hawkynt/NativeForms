using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TrackBarTests
{
    private static HeadlessCanvasPeer Realize(TrackBar bar)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    /// <summary>An 8px end margin leaves a 100px track in a 116px-wide bar: 10px per unit.</summary>
    private static TrackBar CreateHundredPixelTrack() => new() { Bounds = new(0, 0, 116, 30) };

    [Test]
    public void Defaults_match_the_classic_control()
    {
        var bar = new TrackBar();

        Assert.Multiple(() =>
        {
            Assert.That(bar.Minimum, Is.Zero);
            Assert.That(bar.Maximum, Is.EqualTo(10));
            Assert.That(bar.Value, Is.Zero);
            Assert.That(bar.SmallChange, Is.EqualTo(1));
            Assert.That(bar.LargeChange, Is.EqualTo(5));
            Assert.That(bar.TickFrequency, Is.EqualTo(1));
            Assert.That(bar.Orientation, Is.EqualTo(Orientation.Horizontal));
        });
    }

    [Test]
    public void Value_clamps_to_the_range_and_raises_ValueChanged()
    {
        var bar = new TrackBar { Minimum = 2, Maximum = 8 };
        var changes = 0;
        bar.ValueChanged += (_, _) => ++changes;

        bar.Value = 100;
        Assert.That(bar.Value, Is.EqualTo(8));

        bar.Value = -100;
        Assert.That(bar.Value, Is.EqualTo(2));

        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void Arrow_keys_step_by_SmallChange_in_the_native_directions()
    {
        var bar = new TrackBar { Value = 5 };
        var canvas = Realize(bar);

        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(bar.Value, Is.EqualTo(7), "Right/Down increment");

        canvas.RaiseKeyDown(Keys.Left);
        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(bar.Value, Is.EqualTo(5), "Left/Up decrement");
    }

    [Test]
    public void Page_and_home_end_keys_jump()
    {
        var bar = new TrackBar { Value = 5 };
        var canvas = Realize(bar);

        canvas.RaiseKeyDown(Keys.PageDown);
        Assert.That(bar.Value, Is.EqualTo(10), "PageDown adds LargeChange");

        canvas.RaiseKeyDown(Keys.PageUp);
        Assert.That(bar.Value, Is.EqualTo(5), "PageUp subtracts LargeChange");

        canvas.RaiseKeyDown(Keys.Home);
        Assert.That(bar.Value, Is.Zero);

        canvas.RaiseKeyDown(Keys.End);
        Assert.That(bar.Value, Is.EqualTo(10));
    }

    [Test]
    public void Clicking_the_track_pages_toward_the_click()
    {
        var bar = CreateHundredPixelTrack();
        bar.Value = 5; // thumb center at x = 58
        var canvas = Realize(bar);

        canvas.RaiseMouseDown(90, 15); // right of the thumb
        Assert.That(bar.Value, Is.EqualTo(10));

        canvas.RaiseMouseUp(90, 15);
        canvas.RaiseMouseDown(20, 15); // left of the thumb
        Assert.That(bar.Value, Is.EqualTo(5));
    }

    [Test]
    public void Dragging_the_thumb_scrubs_with_live_ValueChanged()
    {
        var bar = CreateHundredPixelTrack();
        bar.Value = 5;
        var changes = 0;
        bar.ValueChanged += (_, _) => ++changes;
        var canvas = Realize(bar);

        canvas.RaiseMouseDown(58, 15); // grab the thumb at its center
        canvas.RaiseMouseMove(78, 15); // 20px right = 2 units
        Assert.That(bar.Value, Is.EqualTo(7));

        canvas.RaiseMouseMove(8, 15); // all the way left
        Assert.That(bar.Value, Is.Zero);

        canvas.RaiseMouseUp(8, 15);
        canvas.RaiseMouseMove(58, 15); // after release, moving no longer scrubs
        Assert.Multiple(() =>
        {
            Assert.That(bar.Value, Is.Zero);
            Assert.That(changes, Is.EqualTo(2), "one event per live value change");
        });
    }

    [Test]
    public void Paints_groove_accent_fill_and_thumb()
    {
        var bar = CreateHundredPixelTrack();
        bar.Value = 5;
        var canvas = Realize(bar);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // Groove interior spans the track at the vertical center.
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 8,13,100,4"));
            // Accent fill covers the track up to the thumb center (x = 58).
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 8,13,50,4"));
            // The thumb is an accent block centered on the value.
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 53,5,10,20"));
        });
    }

    [Test]
    public void Paints_one_tick_per_TickFrequency_step()
    {
        var bar = CreateHundredPixelTrack();
        bar.TickFrequency = 5;
        var canvas = Realize(bar);

        var g = canvas.RaisePaint();

        // Ticks at 0, 5 and 10 — drawn as lines in the text color.
        Assert.That(g.Operations.Count(o => o.StartsWith("line #FF1A1A1A")), Is.EqualTo(3));
    }

    [Test]
    public void Vertical_bar_paints_and_drags_along_the_y_axis()
    {
        var bar = new TrackBar { Bounds = new(0, 0, 30, 116), Orientation = Orientation.Vertical, Value = 5 };
        var canvas = Realize(bar);

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 13,8,4,100"), "groove runs vertically");
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 13,8,4,50"), "accent fills from the top");
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 5,53,20,10"), "thumb centered on the value");
        });

        canvas.RaiseMouseDown(15, 58); // grab the thumb
        canvas.RaiseMouseMove(15, 78);
        Assert.That(bar.Value, Is.EqualTo(7));
    }
}
