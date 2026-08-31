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

    // --- Leaving the gate mid-use ----------------------------------------------------------------

    [Test]
    public void Setting_an_image_on_a_promoted_box_falls_back_to_owner_drawing()
    {
        var backend = Promoting();
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20), Checked = true }, backend);
        Assume.That(box.IsNativeWidget, Is.True, "precondition: it started native");

        box.Image = new HeadlessImage(8, 8); // no platform check box can render this beside the caption

        Assert.Multiple(() =>
        {
            Assert.That(box.IsNativeWidget, Is.False, "leaving the gate must hand the control back to the canvas");
            Assert.That(box.Checked, Is.True, "and must not lose the state it already had");
        });
    }

    [Test]
    public void Clearing_the_image_promotes_the_box_back_to_a_widget()
    {
        var backend = Promoting();
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20), Image = new HeadlessImage(8, 8) }, backend);
        Assume.That(box.IsNativeWidget, Is.False, "precondition: it started owner-drawn");

        box.Image = null;

        Assert.That(box.IsNativeWidget, Is.True, "re-entering the gate takes the widget back");
    }

    [Test]
    public void A_re_realized_box_still_reports_toggles_through_the_new_peer()
    {
        var backend = Promoting();
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20), Image = new HeadlessImage(8, 8) }, backend);
        var raised = 0;
        box.CheckedChanged += (_, _) => ++raised;

        box.Image = null; // promotes; a fresh peer is created and must be the one that is wired
        backend.LastCheckBox!.RaiseUserToggle();

        Assert.Multiple(() =>
        {
            Assert.That(box.Checked, Is.True);
            Assert.That(raised, Is.EqualTo(1), "the new peer is wired, and the old one is not still firing");
        });
    }

    [Test]
    public void Turning_a_promoted_bar_vertical_falls_back_to_owner_drawing()
    {
        var backend = new HeadlessBackend { OfferNativeProgressBar = true };
        var bar = RealizeBar(new ProgressBar { Bounds = new(0, 0, 200, 20), Maximum = 100, Value = 40 }, backend);
        Assume.That(bar.IsNativeWidget, Is.True, "precondition: horizontal starts native");

        bar.Orientation = Orientation.Vertical;

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.False, "a platform bar fixes its orientation at construction");
            Assert.That(bar.Value, Is.EqualTo(40), "and the value survives the swap");
        });
    }

    [Test]
    public void Turning_it_back_horizontal_promotes_it_again_with_its_fraction_intact()
    {
        var backend = new HeadlessBackend { OfferNativeProgressBar = true };
        var bar = RealizeBar(new ProgressBar { Bounds = new(0, 0, 20, 200), Orientation = Orientation.Vertical, Maximum = 100, Value = 40 }, backend);
        Assume.That(bar.IsNativeWidget, Is.False);

        bar.Orientation = Orientation.Horizontal;

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.True);
            Assert.That(backend.LastProgressBar!.Fraction, Is.EqualTo(0.4).Within(0.001), "the fresh peer is seeded with the current value");
        });
    }

    [Test]
    public void A_focused_control_keeps_the_keyboard_across_the_swap()
    {
        var backend = Promoting();
        var box = Realize(new CheckBox { Bounds = new(0, 0, 120, 20) }, backend);
        box.Focus();
        Assume.That(box.Focused, Is.True, "precondition: it holds the keyboard");

        box.Image = new HeadlessImage(8, 8); // demotes to the canvas

        Assert.That(box.Focused, Is.True, "promotion is state-transparent, so the keyboard comes back");
    }

    // --- TrackBar --------------------------------------------------------------------------------

    private static TrackBar RealizeTrack(TrackBar bar, HeadlessBackend backend)
    {
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return bar;
    }

    [Test]
    public void A_track_bar_promotes_and_seeds_the_widget_with_its_range_and_steps()
    {
        var backend = new HeadlessBackend { OfferNativeTrackBar = true };
        var bar = RealizeTrack(new TrackBar
        {
            Bounds = new(0, 0, 200, 30),
            Minimum = 5,
            Maximum = 25,
            Value = 12,
            SmallChange = 2,
            LargeChange = 7,
            TickStyle = TickStyle.None,
        }, backend);

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.True);
            Assert.That(backend.LastTrackBar!.Minimum, Is.EqualTo(5));
            Assert.That(backend.LastTrackBar!.Maximum, Is.EqualTo(25));
            Assert.That(backend.LastTrackBar!.GetValue(), Is.EqualTo(12));
            Assert.That(backend.LastTrackBar!.SmallChange, Is.EqualTo(2));
            Assert.That(backend.LastTrackBar!.LargeChange, Is.EqualTo(7));
        });
    }

    [Test]
    public void Visible_ticks_fall_back_when_a_native_peer_cannot_render_them()
    {
        var backend = new HeadlessBackend { OfferNativeTrackBar = true };
        var bar = RealizeTrack(new TrackBar { Bounds = new(0, 0, 200, 30), TickFrequency = 2 }, backend);

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.False, "visible marks must not disappear merely because the backend offered a slider");
            Assert.That(backend.LastTrackBar!.Disposed, Is.True, "the unsuitable candidate is released before owner drawing takes over");
        });
    }

    [Test]
    public void Disabling_ticks_can_promote_a_fallback_track_bar()
    {
        var backend = new HeadlessBackend { OfferNativeTrackBar = true };
        var bar = RealizeTrack(new TrackBar { Bounds = new(0, 0, 200, 30), Value = 6 }, backend);
        Assume.That(bar.IsNativeWidget, Is.False);

        bar.TickStyle = TickStyle.None;

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.True);
            Assert.That(backend.LastTrackBar!.GetValue(), Is.EqualTo(6), "the value survives the owner-drawn/native swap");
        });
    }

    [Test]
    public void A_drag_on_the_widget_surfaces_as_ValueChanged_and_Scroll_once()
    {
        var backend = new HeadlessBackend { OfferNativeTrackBar = true };
        var bar = RealizeTrack(new TrackBar { Bounds = new(0, 0, 200, 30), Maximum = 100, TickStyle = TickStyle.None }, backend);
        int changed = 0, scrolled = 0;
        bar.ValueChanged += (_, _) => ++changed;
        bar.Scroll += (_, _) => ++scrolled;

        backend.LastTrackBar!.RaiseUserDrag(42);

        Assert.Multiple(() =>
        {
            Assert.That(bar.Value, Is.EqualTo(42));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(scrolled, Is.EqualTo(1));
        });
    }

    [Test]
    public void Turning_a_promoted_slider_rebuilds_it_in_the_new_orientation()
    {
        var backend = new HeadlessBackend { OfferNativeTrackBar = true };
        var bar = RealizeTrack(new TrackBar
        {
            Bounds = new(0, 0, 200, 30),
            Maximum = 100,
            Value = 30,
            TickStyle = TickStyle.None,
        }, backend);
        Assume.That(backend.LastTrackBar!.Vertical, Is.False);

        bar.Orientation = Orientation.Vertical;

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsNativeWidget, Is.True, "it stays native — only the widget is rebuilt");
            Assert.That(backend.LastTrackBar!.Vertical, Is.True, "GTK fixes orientation at construction");
            Assert.That(backend.LastTrackBar!.GetValue(), Is.EqualTo(30), "and the value survives");
        });
    }
}
