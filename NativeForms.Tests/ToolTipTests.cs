using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ToolTipTests
{
    /// <summary>Realizes a panel with a registered tip and returns all the actors.</summary>
    private static Panel CreatePanel(out ToolTip toolTip, out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var panel = new Panel { Bounds = new(10, 10, 200, 150) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(50, 60);

        toolTip = new();
        toolTip.SetToolTip(panel, "hint");
        return panel;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void SetToolTip_registers_and_clears_the_text()
    {
        var panel = CreatePanel(out var toolTip, out _, out _);

        Assert.That(toolTip.GetToolTip(panel), Is.EqualTo("hint"));

        toolTip.SetToolTip(panel, null);
        Assert.That(toolTip.GetToolTip(panel), Is.Empty);
    }

    [Test]
    public void Tip_shows_after_the_initial_delay_at_the_cursor_offset()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);

        canvas.RaiseMouseMove(20, 30);
        Assert.Multiple(() =>
        {
            Assert.That(toolTip.Active, Is.False, "nothing shows before the delay elapses");
            Assert.That(backend.Timers.Single().StartedIntervals, Is.EqualTo(new[] { 500 }));
        });

        backend.Timers[0].FireTick();

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(toolTip.Active, Is.True);
            // Cursor (20, 30) mapped to screen (50+20, 60+30) plus the 18px vertical offset; the
            // popup wraps "hint" (28×16) in 4px padding on each side.
            Assert.That(popup.ShowCalls.Single(), Is.EqualTo((new Point(70, 108), new Size(36, 24))));
        });
    }

    [Test]
    public void Tip_paints_background_border_and_text()
    {
        CreatePanel(out _, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);
        backend.Timers[0].FireTick();

        var g = PopupOf(backend).RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 0,0,36,24"));
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 0,0,35,23"));
            Assert.That(g.DrewText("hint"), Is.True);
        });
    }

    [Test]
    public void Tip_hides_when_the_pointer_leaves()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);
        backend.Timers[0].FireTick();

        canvas.RaiseMouseLeave();

        Assert.Multiple(() =>
        {
            Assert.That(toolTip.Active, Is.False);
            Assert.That(PopupOf(backend).IsShown, Is.False);
        });
    }

    [Test]
    public void Tip_hides_on_a_button_press()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);
        backend.Timers[0].FireTick();

        canvas.RaiseMouseDown(20, 30);

        Assert.That(toolTip.Active, Is.False);
    }

    [Test]
    public void Tip_hides_after_the_auto_pop_delay()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);
        var timer = backend.Timers[0];
        timer.FireTick();
        Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500, 5000 }), "the auto-pop phase re-arms the timer");

        timer.FireTick();

        Assert.Multiple(() =>
        {
            Assert.That(toolTip.Active, Is.False);
            Assert.That(PopupOf(backend).IsShown, Is.False);
        });
    }

    [Test]
    public void Leaving_before_the_delay_cancels_the_pending_tip()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);

        canvas.RaiseMouseLeave();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Timers[0].IsRunning, Is.False);
            Assert.That(toolTip.Active, Is.False);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty, "no popup was ever created");
        });
    }

    [Test]
    public void Unregistered_control_shows_no_tip()
    {
        var panel = CreatePanel(out var toolTip, out var canvas, out var backend);
        toolTip.SetToolTip(panel, null);

        canvas.RaiseMouseMove(20, 30);

        Assert.That(backend.Timers, Is.Empty, "the observers were detached");
    }

    [Test]
    public void Dispose_hides_and_releases_everything()
    {
        CreatePanel(out var toolTip, out var canvas, out var backend);
        canvas.RaiseMouseMove(20, 30);
        backend.Timers[0].FireTick();

        toolTip.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(PopupOf(backend).Disposed, Is.True);
            Assert.That(backend.Timers[0].Disposed, Is.True);
        });

        canvas.RaiseMouseMove(20, 30); // detached: nothing restarts
        Assert.That(backend.Timers, Has.Count.EqualTo(1));
    }
}
