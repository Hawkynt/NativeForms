using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ScrollBarTests
{
    private static HeadlessBackend Realize(ScrollBar bar)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().Single();

    /// <summary>16px arrows at both ends of a 132px bar leave a 100px track; LargeChange 20 over the
    /// 100-value range makes a 20px thumb with 80px of travel.</summary>
    private static VScrollBar CreateVertical() => new()
    {
        Bounds = new(0, 0, 16, 132),
        Maximum = 99,
        SmallChange = 5,
        LargeChange = 20,
    };

    [Test]
    public void Defaults_match_the_classic_control()
    {
        var bar = new VScrollBar();

        Assert.Multiple(() =>
        {
            Assert.That(bar.Minimum, Is.Zero);
            Assert.That(bar.Maximum, Is.EqualTo(100));
            Assert.That(bar.Value, Is.Zero);
            Assert.That(bar.SmallChange, Is.EqualTo(1));
            Assert.That(bar.LargeChange, Is.EqualTo(10));
        });
    }

    [Test]
    public void Value_clamps_to_the_scrollable_range()
    {
        var bar = CreateVertical();

        bar.Value = 200;
        Assert.That(bar.Value, Is.EqualTo(80), "maximum reachable value is Maximum - LargeChange + 1");

        bar.Value = -5;
        Assert.That(bar.Value, Is.Zero);
    }

    [Test]
    public void Thumb_is_proportional_to_LargeChange_over_the_range()
    {
        var bar = CreateVertical();
        var canvas = CanvasOf(Realize(bar));

        var g = canvas.RaisePaint();

        // 100px track * 20/100 values = 20px thumb, sitting at the track start for Value 0.
        Assert.That(g.Operations, Does.Contain("fill #FFC8C8C8 0,16,16,20"));
    }

    [Test]
    public void Arrow_click_steps_by_SmallChange_and_autorepeats()
    {
        var bar = CreateVertical();
        bar.Value = 50;
        var backend = Realize(bar);
        var canvas = CanvasOf(backend);
        var scrolls = 0;
        bar.Scroll += (_, e) =>
        {
            ++scrolls;
            Assert.That(e.Type, Is.EqualTo(ScrollEventType.SmallDecrement));
            Assert.That(e.NewValue, Is.EqualTo(bar.Value));
        };

        canvas.RaiseMouseDown(8, 4); // decrease arrow
        Assert.That(bar.Value, Is.EqualTo(45));
        var timer = backend.Timers.Single();
        Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500 }));

        timer.FireTick();
        Assert.Multiple(() =>
        {
            Assert.That(bar.Value, Is.EqualTo(40));
            Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500, 50 }));
        });

        timer.FireTick();
        Assert.That(bar.Value, Is.EqualTo(35));

        canvas.RaiseMouseUp(8, 4);
        Assert.Multiple(() =>
        {
            Assert.That(timer.IsRunning, Is.False, "release stops the autorepeat");
            Assert.That(scrolls, Is.EqualTo(3));
        });
    }

    [Test]
    public void Increase_arrow_steps_up_and_clamps()
    {
        var bar = CreateVertical();
        bar.Value = 78;
        var canvas = CanvasOf(Realize(bar));

        canvas.RaiseMouseDown(8, 128); // increase arrow at the bottom
        Assert.That(bar.Value, Is.EqualTo(80), "clamps at the scrollable maximum");
        canvas.RaiseMouseUp(8, 128);
    }

    [Test]
    public void Channel_click_pages_toward_the_click()
    {
        var bar = CreateVertical();
        var canvas = CanvasOf(Realize(bar));
        var types = new List<ScrollEventType>();
        bar.Scroll += (_, e) => types.Add(e.Type);

        canvas.RaiseMouseDown(8, 100); // below the thumb (16..36)
        Assert.That(bar.Value, Is.EqualTo(20));

        canvas.RaiseMouseUp(8, 100);
        canvas.RaiseMouseDown(8, 20); // thumb moved to 36..56; click above it
        Assert.That(bar.Value, Is.Zero);

        canvas.RaiseMouseUp(8, 20);
        Assert.That(types, Is.EqualTo(new[] { ScrollEventType.LargeIncrement, ScrollEventType.LargeDecrement }));
    }

    [Test]
    public void Thumb_drag_scrubs_and_finishes_with_EndScroll()
    {
        var bar = CreateVertical();
        var canvas = CanvasOf(Realize(bar));
        var types = new List<ScrollEventType>();
        var valueChanges = 0;
        bar.Scroll += (_, e) => types.Add(e.Type);
        bar.ValueChanged += (_, _) => ++valueChanges;

        canvas.RaiseMouseDown(8, 20); // grab the thumb (16..36) 4px in
        canvas.RaiseMouseMove(8, 60); // thumb start 56 => 40px of 80px travel => value 40
        Assert.That(bar.Value, Is.EqualTo(40));

        canvas.RaiseMouseMove(8, 120); // past the end clamps
        Assert.That(bar.Value, Is.EqualTo(80));

        canvas.RaiseMouseUp(8, 120);
        Assert.Multiple(() =>
        {
            Assert.That(types, Is.EqualTo(new[] { ScrollEventType.ThumbTrack, ScrollEventType.ThumbTrack, ScrollEventType.EndScroll }));
            Assert.That(valueChanges, Is.EqualTo(2));
        });
    }

    [Test]
    public void Horizontal_bar_lays_out_along_the_x_axis()
    {
        var bar = new HScrollBar
        {
            Bounds = new(0, 0, 132, 16),
            Maximum = 99,
            SmallChange = 5,
            LargeChange = 20,
        };
        var canvas = CanvasOf(Realize(bar));

        var g = canvas.RaisePaint();
        Assert.That(g.Operations, Does.Contain("fill #FFC8C8C8 16,0,20,16"), "thumb at the track start");

        canvas.RaiseMouseDown(128, 8); // increase arrow at the right
        Assert.That(bar.Value, Is.EqualTo(5));
        canvas.RaiseMouseUp(128, 8);

        canvas.RaiseMouseDown(100, 8); // channel right of the thumb
        Assert.That(bar.Value, Is.EqualTo(25));
        canvas.RaiseMouseUp(100, 8);
    }

    [Test]
    public void Programmatic_Value_raises_ValueChanged_but_not_Scroll()
    {
        var bar = CreateVertical();
        var scrolls = 0;
        var changes = 0;
        bar.Scroll += (_, _) => ++scrolls;
        bar.ValueChanged += (_, _) => ++changes;

        bar.Value = 30;

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(scrolls, Is.Zero);
        });
    }
}
