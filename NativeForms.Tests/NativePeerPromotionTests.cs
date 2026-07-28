using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a control with a faithful platform counterpart realizes onto a real widget when the app
/// prefers it, the gate passes and the backend offers one — and behaves identically either way. The
/// headless backend declines by default, which is what keeps every other test on the owner-drawn path.
/// </summary>
[TestFixture]
internal sealed class NativePeerPromotionTests
{
    private static HeadlessBackend Promoting() => new() { OfferNativeCheckBox = true };

    private static CheckBox Realize(CheckBox box, IPlatformBackend backend)
    {
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return box;
    }

    [TearDown]
    public void Restore() => Application.PreferNativeWidgets = true;

    [Test]
    public void A_backend_that_declines_leaves_the_control_owner_drawn()
    {
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20) }, new HeadlessBackend());

        Assert.That(box.IsNativeWidget, Is.False, "the headless backend declines, so nothing is promoted");
    }

    [Test]
    public void A_backend_that_offers_a_widget_promotes_the_control()
    {
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20) }, Promoting());

        Assert.That(box.IsNativeWidget, Is.True);
    }

    [Test]
    public void An_image_keeps_the_control_owner_drawn_even_on_a_willing_backend()
    {
        var backend = Promoting();
        var box = new CheckBox { Bounds = new(0, 0, 120, 20), Image = new HeadlessImage(8, 8) };

        Realize(box, backend);

        Assert.That(box.IsNativeWidget, Is.False, "no platform check box renders our image beside the caption");
    }

    [Test]
    public void The_global_switch_turns_promotion_off()
    {
        Application.PreferNativeWidgets = false;

        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20) }, Promoting());

        Assert.That(box.IsNativeWidget, Is.False);
    }

    [Test]
    public void A_per_control_override_beats_the_global_switch()
    {
        Application.PreferNativeWidgets = false;

        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20), UseNativeWidget = true }, Promoting());

        Assert.That(box.IsNativeWidget, Is.True);
    }

    // --- The behaviour must be identical on both paths -------------------------------------------

    [Test]
    public void Setting_Checked_raises_the_event_once_on_both_paths()
    {
        foreach (var backend in new IPlatformBackend[] { new HeadlessBackend(), Promoting() })
        {
            var box = new CheckBox { Bounds = new(0, 0, 120, 20) };
            var raised = 0;
            box.CheckedChanged += (_, _) => ++raised;
            Realize(box, backend);

            box.Checked = true;
            box.Checked = true; // assigning the same value again is a no-op on either path

            Assert.Multiple(() =>
            {
                Assert.That(box.Checked, Is.True, $"{backend.GetType().Name}");
                Assert.That(raised, Is.EqualTo(1), $"{backend.GetType().Name}: exactly one change event");
            });
        }
    }

    [Test]
    public void The_managed_state_pushes_into_the_widget()
    {
        var backend = Promoting();
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20) }, backend);

        box.Checked = true;

        Assert.That(backend.LastCheckBox!.GetChecked(), Is.True, "the widget mirrors the managed state");
    }

    [Test]
    public void A_toggle_from_the_widget_surfaces_as_the_public_event()
    {
        var backend = Promoting();
        var box = new CheckBox { Bounds = new(0, 0, 120, 20) };
        var raised = 0;
        box.CheckedChanged += (_, _) => ++raised;
        Realize(box, backend);

        backend.LastCheckBox!.RaiseUserToggle();

        Assert.Multiple(() =>
        {
            Assert.That(box.Checked, Is.True, "the core mirrors what the widget actually did");
            Assert.That(raised, Is.EqualTo(1), "and raises the public event exactly once, not twice");
        });
    }

    // --- ProgressBar: the same mechanism, a second control ---------------------------------------

    private static ProgressBar RealizeBar(ProgressBar bar, HeadlessBackend backend)
    {
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return bar;
    }

    [Test]
    public void A_horizontal_progress_bar_promotes_and_pushes_its_fraction()
    {
        var backend = new HeadlessBackend { OfferNativeProgressBar = true };
        var bar = RealizeBar(new ProgressBar { Bounds = new(0, 0, 200, 20), Minimum = 0, Maximum = 200 }, backend);

        bar.Value = 50;

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.True);
            Assert.That(backend.LastProgressBar!.Fraction, Is.EqualTo(0.25).Within(0.001), "50 of 200 is a quarter");
        });
    }

    [Test]
    public void A_vertical_progress_bar_stays_owner_drawn()
    {
        var backend = new HeadlessBackend { OfferNativeProgressBar = true };
        var bar = RealizeBar(new ProgressBar { Bounds = new(0, 0, 20, 200), Orientation = Orientation.Vertical }, backend);

        Assert.That(bar.IsNativeWidget, Is.False, "the peers do not carry a vertical orientation yet");
    }

    [Test]
    public void Marquee_switches_the_widget_into_its_indeterminate_mode()
    {
        var backend = new HeadlessBackend { OfferNativeProgressBar = true };
        var bar = RealizeBar(new ProgressBar { Bounds = new(0, 0, 200, 20) }, backend);

        bar.Style = ProgressBarStyle.Marquee;

        Assert.That(backend.LastProgressBar!.Marquee, Is.True);
    }
}
