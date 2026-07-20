using System;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TimerTests
{
    [Test]
    public void Interval_defaults_to_100()
    {
        using var timer = new Timer(new HeadlessBackend());
        Assert.That(timer.Interval, Is.EqualTo(100));
    }

    [Test]
    public void Interval_below_one_throws()
    {
        using var timer = new Timer(new HeadlessBackend());
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = -50);
    }

    [Test]
    public void Enabling_starts_the_peer_at_the_current_interval()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend) { Interval = 250 };

        timer.Enabled = true;

        Assert.That(backend.Timers, Has.Count.EqualTo(1));
        var peer = backend.Timers[0];
        Assert.That(peer.StartedIntervals, Is.EqualTo(new[] { 250 }));
        Assert.That(peer.IsRunning, Is.True);
        Assert.That(timer.Enabled, Is.True);
    }

    [Test]
    public void Disabling_stops_the_peer()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);
        timer.Enabled = true;

        timer.Enabled = false;

        var peer = backend.Timers[0];
        Assert.That(peer.IsRunning, Is.False);
        Assert.That(peer.StopCount, Is.EqualTo(1));
        Assert.That(timer.Enabled, Is.False);
    }

    [Test]
    public void Start_and_Stop_mirror_Enabled()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);

        timer.Start();
        Assert.That(timer.Enabled, Is.True);
        Assert.That(backend.Timers[0].IsRunning, Is.True);

        timer.Stop();
        Assert.That(timer.Enabled, Is.False);
        Assert.That(backend.Timers[0].IsRunning, Is.False);
    }

    [Test]
    public void Tick_reaches_subscribers()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);
        var ticks = 0;
        timer.Tick += (sender, e) =>
        {
            ++ticks;
            Assert.That(sender, Is.SameAs(timer));
            Assert.That(e, Is.SameAs(EventArgs.Empty));
        };
        timer.Start();

        backend.Timers[0].FireTick();
        backend.Timers[0].FireTick();

        Assert.That(ticks, Is.EqualTo(2));
    }

    [Test]
    public void Changing_interval_while_running_restarts_the_peer()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);
        timer.Start();

        timer.Interval = 40;

        Assert.That(backend.Timers[0].StartedIntervals, Is.EqualTo(new[] { 100, 40 }));
    }

    [Test]
    public void Changing_interval_while_stopped_does_not_start()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);

        timer.Interval = 40;

        Assert.That(backend.Timers, Is.Empty);
    }

    [Test]
    public void Enabling_twice_does_not_restart_the_peer()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);
        timer.Enabled = true;

        timer.Enabled = true;

        Assert.That(backend.Timers[0].StartedIntervals, Has.Count.EqualTo(1));
    }

    [Test]
    public void Peer_is_created_once_and_reused_across_restarts()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);

        timer.Start();
        timer.Stop();
        timer.Start();

        Assert.That(backend.Timers, Has.Count.EqualTo(1));
        Assert.That(backend.Timers[0].StartedIntervals, Has.Count.EqualTo(2));
    }

    [Test]
    public void Dispose_stops_and_disposes_the_peer()
    {
        var backend = new HeadlessBackend();
        var timer = new Timer(backend);
        timer.Start();

        timer.Dispose();

        var peer = backend.Timers[0];
        Assert.That(peer.IsRunning, Is.False);
        Assert.That(peer.Disposed, Is.True);
        Assert.That(timer.Enabled, Is.False);
    }

    [Test]
    public void Enabling_before_the_loop_runs_arms_when_the_loop_starts()
    {
        var backend = new HeadlessBackend();
        var timer = new Timer { Interval = 50 };

        // No application loop yet: the wish joins the pending registry, nothing is created.
        timer.Enabled = true;
        Assert.That(backend.Timers, Is.Empty);

        var runningInsideLoop = false;
        backend.RunAction = () => runningInsideLoop = backend.Timers.Count == 1 && backend.Timers[0].IsRunning;
        Application.Run(new Form(), backend);

        // The registry armed the timer the moment the loop started — no further touch needed.
        Assert.That(runningInsideLoop, Is.True, "ticking while the loop pumps");
        Assert.That(backend.Timers, Has.Count.EqualTo(1));
        Assert.That(backend.Timers[0].StartedIntervals, Is.EqualTo(new[] { 50 }));
        timer.Dispose();
    }

    [Test]
    public void A_pending_timer_disabled_again_never_arms()
    {
        var backend = new HeadlessBackend();
        var timer = new Timer();

        timer.Enabled = true;
        timer.Enabled = false;
        Application.Run(new Form(), backend);

        Assert.That(backend.Timers, Is.Empty);
        timer.Dispose();
    }

    [Test]
    public void Tick_path_does_not_allocate_beyond_the_subscriber()
    {
        var backend = new HeadlessBackend();
        using var timer = new Timer(backend);
        var ticks = 0;
        timer.Tick += (_, _) => ++ticks;
        timer.Start();
        var peer = backend.Timers[0];

        peer.FireTick(); // warm up so first-call JIT compilation isn't counted
        var before = GC.GetAllocatedBytesForCurrentThread();
        peer.FireTick();
        peer.FireTick();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(bytes, Is.Zero, $"{bytes} bytes for two ticks");
    }
}
