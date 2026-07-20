using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ProgressBarTests
{
    private static HeadlessBackend Realize(ProgressBar bar)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().Single();

    [Test]
    public void Defaults_are_determinate_blocks()
    {
        var bar = new ProgressBar();

        Assert.Multiple(() =>
        {
            Assert.That(bar.Style, Is.EqualTo(ProgressBarStyle.Blocks));
            Assert.That(bar.MarqueeAnimationSpeed, Is.EqualTo(100), "the WinForms default");
            Assert.That(bar.Step, Is.EqualTo(10));
            Assert.That(bar.Orientation, Is.EqualTo(Orientation.Horizontal));
        });
    }

    [Test]
    public void PerformStep_advances_by_Step_and_clamps()
    {
        var bar = new ProgressBar { Step = 40 };

        bar.PerformStep();
        Assert.That(bar.Value, Is.EqualTo(40));

        bar.PerformStep();
        bar.PerformStep();
        Assert.That(bar.Value, Is.EqualTo(100), "clamps at Maximum");
    }

    [Test]
    public void Vertical_bar_fills_bottom_up()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 20, 102), Orientation = Orientation.Vertical, Value = 50 };
        var canvas = CanvasOf(Realize(bar));

        var g = canvas.RaisePaint();

        // Half of the 100px track, anchored at the bottom edge of the 1px inset.
        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 1,51,18,50"));
    }

    [Test]
    public void Marquee_style_starts_the_timer_at_the_animation_speed()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20) };
        var backend = Realize(bar);

        bar.Style = ProgressBarStyle.Marquee;

        var timer = backend.Timers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(timer.IsRunning, Is.True);
            Assert.That(timer.StartedIntervals, Does.Contain(100));
        });
    }

    [Test]
    public void Marquee_ticks_move_the_painted_segment()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20), Style = ProgressBarStyle.Marquee };
        var backend = Realize(bar);
        var canvas = CanvasOf(backend);
        var timer = backend.Timers.Single();

        // 100px track: a 25px segment sweeping a 125px period at 2px per tick.
        for (var i = 0; i < 25; ++i)
            timer.FireTick();

        var g = canvas.RaisePaint();
        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 26,1,25,18"));

        timer.FireTick();
        g = canvas.RaisePaint();
        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 28,1,25,18"), "the segment advanced");
    }

    [Test]
    public void Marquee_tick_invalidates_without_allocating()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20), Style = ProgressBarStyle.Marquee };
        var backend = Realize(bar);
        var canvas = CanvasOf(backend);
        var timer = backend.Timers.Single();
        var invalidationsBefore = canvas.InvalidateCount;

        timer.FireTick(); // warm up so first-call JIT compilation isn't counted
        var before = GC.GetAllocatedBytesForCurrentThread();
        timer.FireTick();
        timer.FireTick();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.Zero, $"{bytes} bytes for two ticks");
            Assert.That(canvas.InvalidateCount, Is.EqualTo(invalidationsBefore + 3));
        });
    }

    [Test]
    public void Switching_back_to_Blocks_stops_the_timer()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20), Style = ProgressBarStyle.Marquee };
        var backend = Realize(bar);

        bar.Style = ProgressBarStyle.Blocks;

        Assert.That(backend.Timers.Single().IsRunning, Is.False);
    }

    [Test]
    public void Animation_speed_zero_pauses_the_marquee()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 102, 20), Style = ProgressBarStyle.Marquee };
        var backend = Realize(bar);
        var timer = backend.Timers.Single();

        bar.MarqueeAnimationSpeed = 0;
        Assert.That(timer.IsRunning, Is.False);

        bar.MarqueeAnimationSpeed = 15;
        Assert.Multiple(() =>
        {
            Assert.That(timer.IsRunning, Is.True);
            Assert.That(timer.StartedIntervals, Does.Contain(15));
        });
    }

    [Test]
    public void Vertical_marquee_sweeps_along_the_y_axis()
    {
        var bar = new ProgressBar
        {
            Bounds = new(0, 0, 20, 102),
            Orientation = Orientation.Vertical,
            Style = ProgressBarStyle.Marquee,
        };
        var backend = Realize(bar);
        var timer = backend.Timers.Single();

        for (var i = 0; i < 25; ++i)
            timer.FireTick();

        var g = CanvasOf(backend).RaisePaint();
        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 1,51,18,25"));
    }
}
