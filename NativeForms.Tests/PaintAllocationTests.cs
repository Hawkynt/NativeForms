using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The zero-per-frame-allocation guarantee for owner-drawn painting: once realized and warmed up, a
/// repaint driven through the peer's reused <see cref="PaintEventArgs"/> must allocate no managed
/// memory at all. Painted onto <see cref="NullGraphics"/> so the only bytes the measurement could
/// see are the control's own — a regression here means a paint path started newing per frame.
/// </summary>
[TestFixture]
internal sealed class PaintAllocationTests
{
    private const int _Frames = 100;

    /// <summary>Realizes the control, warms the paint path up, then measures a steady-state run.</summary>
    private static long MeasureSteadyStatePaint(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var graphics = new NullGraphics();

        // Warm up: JIT the paint path and let the peer build its reusable event args.
        canvas.RaisePaint(graphics);
        canvas.RaisePaint(graphics);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < _Frames; ++i)
            canvas.RaisePaint(graphics);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void CheckBox_steady_state_repaint_allocates_nothing()
    {
        var check = new CheckBox { Text = "Remember me", Bounds = new(0, 0, 160, 20), Checked = true };

        Assert.That(MeasureSteadyStatePaint(check), Is.Zero);
    }

    [Test]
    public void ToggleSwitch_steady_state_repaint_allocates_nothing()
    {
        var toggle = new ToggleSwitch { Text = "Run", Bounds = new(0, 0, 120, 24), Checked = true };

        Assert.That(MeasureSteadyStatePaint(toggle), Is.Zero);
    }

    [Test]
    public void ProgressBar_steady_state_repaint_allocates_nothing()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 160, 20), Value = 40 };

        Assert.That(MeasureSteadyStatePaint(bar), Is.Zero);
    }
}
