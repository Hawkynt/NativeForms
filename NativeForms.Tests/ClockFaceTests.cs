using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ClockFaceTests
{
    // Realized with the default theme (RowHeight 22) the dial's PreferredSize is 192x236: an 8-margin
    // frame around a 176 px square dial under a 30 px header and over a 30 px footer. That puts the
    // centre at (96, 118), the outer hour ring at radius 67 and — on a 24-hour dial — the inner ring
    // at radius 40. Three o'clock is therefore the outer number at (163, 118), six o'clock the outer
    // number at (96, 185). The OK affordance fills the footer's right; the AM/PM toggle the header's.
    private const int _CenterX = 96;
    private const int _ThreeOClockX = 163;
    private const int _BottomY = 185;

    private static ClockFace CreatePicker(out HeadlessCanvasPeer canvas, bool use24Hour = true, bool showSeconds = false)
    {
        var clock = new ClockFace { Bounds = new(0, 0, 192, 236), Use24HourClock = use24Hour, ShowSeconds = showSeconds, Value = new(9, 30, 15) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(clock);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return clock;
    }

    [Test]
    public void Defaults_are_a_24_hour_dial_on_the_hour_stage()
    {
        var clock = new ClockFace();

        Assert.Multiple(() =>
        {
            Assert.That(clock.Use24HourClock, Is.True);
            Assert.That(clock.ShowSeconds, Is.False);
            Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Hour));
            Assert.That(clock.FinalStage, Is.EqualTo(ClockFaceStage.Minute), "no seconds stage without ShowSeconds");
            Assert.That(clock.Value.Milliseconds, Is.Zero, "the dial holds whole seconds");
        });
    }

    [Test]
    public void Value_assignment_drops_sub_second_precision_and_raises_ValueChanged_once()
    {
        var clock = new ClockFace { Value = TimeSpan.Zero };
        var raised = 0;
        clock.ValueChanged += (_, _) => ++raised;

        clock.Value = new(0, 7, 30, 45, 900);
        clock.Value = new(0, 7, 30, 45, 100); // same whole second: no change, no event

        Assert.Multiple(() =>
        {
            Assert.That(clock.Value, Is.EqualTo(new TimeSpan(7, 30, 45)));
            Assert.That(raised, Is.EqualTo(1));
        });
    }

    [Test]
    public void The_hour_ring_paints_both_24_hour_rings()
    {
        var clock = CreatePicker(out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("12"), Is.True, "the outer ring starts at noon/midnight");
            Assert.That(g.DrewText("00"), Is.True, "the inner ring starts at 00");
            Assert.That(g.DrewText("15"), Is.True, "an outer afternoon hour");
            Assert.That(g.DrewText("09"), Is.True, "the hour part of the readout");
        });
    }

    [Test]
    public void A_12_hour_dial_paints_the_meridiem_and_a_one_to_twelve_ring()
    {
        var clock = CreatePicker(out var canvas, use24Hour: false);
        clock.Value = new(13, 5, 0);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("AM"), Is.True);
            Assert.That(g.DrewText("PM"), Is.True);
            Assert.That(g.DrewText("01"), Is.True, "13:00 reads as 01 PM in the readout");
            Assert.That(g.DrewText("11"), Is.True, "the 1..12 ring");
        });
    }

    [Test]
    public void Clicking_the_hour_ring_sets_the_hour_then_advances_to_the_minute()
    {
        var clock = CreatePicker(out var canvas);

        canvas.RaiseMouseDown(_ThreeOClockX, 118); // the outer three-o'clock number: 15:00
        var afterPress = clock.Value;
        var stageDuringPress = clock.Stage;
        canvas.RaiseMouseUp(_ThreeOClockX, 118);

        Assert.Multiple(() =>
        {
            Assert.That(afterPress.Hours, Is.EqualTo(15), "the outer ring is 12..23");
            Assert.That(stageDuringPress, Is.EqualTo(ClockFaceStage.Hour));
            Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Minute), "releasing the hour advances to the minute");
        });
    }

    [Test]
    public void Clicking_the_inner_ring_picks_a_morning_hour()
    {
        var clock = CreatePicker(out var canvas);

        canvas.RaiseMouseDown(_CenterX, 78); // the inner top number, radius 40: 00:xx

        Assert.That(clock.Value.Hours, Is.EqualTo(0), "the inner ring is 00..11");
    }

    [Test]
    public void Clicking_the_minute_ring_snaps_to_the_nearest_minute()
    {
        var clock = CreatePicker(out var canvas);
        clock.Stage = ClockFaceStage.Minute;

        canvas.RaiseMouseDown(_CenterX, _BottomY); // six o'clock: 30 minutes

        Assert.That(clock.Value.Minutes, Is.EqualTo(30));
    }

    [Test]
    public void A_header_segment_click_switches_the_stage()
    {
        var clock = CreatePicker(out var canvas, use24Hour: false);

        canvas.RaiseMouseDown(100, 15); // the minute segment of the readout band

        Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Minute));
    }

    [Test]
    public void The_meridiem_toggle_flips_the_half_day()
    {
        var clock = CreatePicker(out var canvas, use24Hour: false);
        clock.Value = new(9, 30, 0);

        canvas.RaiseMouseDown(173, 15); // the PM half of the header toggle
        var afternoon = clock.Value;
        canvas.RaiseMouseDown(151, 15); // the AM half

        Assert.Multiple(() =>
        {
            Assert.That(afternoon, Is.EqualTo(new TimeSpan(21, 30, 0)));
            Assert.That(clock.Value, Is.EqualTo(new TimeSpan(9, 30, 0)));
        });
    }

    [Test]
    public void Clicking_the_OK_affordance_commits()
    {
        var clock = CreatePicker(out var canvas);
        var committed = 0;
        clock.Committed = () => ++committed;

        canvas.RaiseMouseDown(151, 219); // the OK zone in the footer

        Assert.That(committed, Is.EqualTo(1));
    }

    [Test]
    public void Picking_the_final_part_on_the_dial_commits()
    {
        var clock = CreatePicker(out var canvas); // Minutes precision → the minute is final
        var committed = 0;
        clock.Committed = () => ++committed;

        canvas.RaiseMouseDown(_ThreeOClockX, 118); // set the hour
        canvas.RaiseMouseUp(_ThreeOClockX, 118);   // hour is not final → advance, no commit
        Assert.That(committed, Is.Zero, "setting a non-final part only advances");
        Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Minute));

        canvas.RaiseMouseDown(_CenterX, _BottomY); // set the minute
        canvas.RaiseMouseUp(_CenterX, _BottomY);   // the minute is final → commit

        Assert.That(committed, Is.EqualTo(1), "picking the last part closes the dial");
    }

    [Test]
    public void Hours_precision_makes_the_hour_the_final_part_and_commits_on_the_first_pick()
    {
        var clock = CreatePicker(out var canvas);
        clock.Precision = ClockFacePrecision.Hours;
        var committed = 0;
        clock.Committed = () => ++committed;

        Assert.That(clock.FinalStage, Is.EqualTo(ClockFaceStage.Hour));

        canvas.RaiseMouseDown(_ThreeOClockX, 118); // pick the hour — the only part
        canvas.RaiseMouseUp(_ThreeOClockX, 118);

        Assert.Multiple(() =>
        {
            Assert.That(clock.Value.Hours, Is.EqualTo(15));
            Assert.That(committed, Is.EqualTo(1), "picking the hour commits when it is the final part");
        });
    }

    [Test]
    public void Lowering_the_precision_pulls_the_active_stage_back()
    {
        var clock = CreatePicker(out _, showSeconds: true);
        clock.Stage = ClockFaceStage.Second;

        clock.Precision = ClockFacePrecision.Hours;

        Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Hour), "a stage past the new precision falls back");
    }

    [Test]
    public void Arrows_nudge_the_active_hand_wrapping_within_the_part()
    {
        var clock = CreatePicker(out var canvas);
        clock.Value = new(23, 0, 0);

        canvas.RaiseKeyDown(Keys.Right); // hour 23 -> 00, wrapping without a carry
        var hour = clock.Value;
        clock.Stage = ClockFaceStage.Minute;
        clock.Value = new(0, 59, 0);
        canvas.RaiseKeyDown(Keys.Up);

        Assert.Multiple(() =>
        {
            Assert.That(hour, Is.EqualTo(new TimeSpan(0, 0, 0)));
            Assert.That(clock.Value, Is.EqualTo(new TimeSpan(0, 0, 0)), "the minute wraps 59 -> 00");
        });
    }

    [Test]
    public void Tab_cycles_the_stage_and_Enter_advances_then_commits()
    {
        var clock = CreatePicker(out var canvas, showSeconds: true);
        var committed = 0;
        clock.Committed = () => ++committed;

        canvas.RaiseKeyDown(Keys.Tab); // Hour -> Minute
        var afterTab = clock.Stage;
        canvas.RaiseKeyDown(Keys.Enter); // Minute -> Second (not final while seconds are shown)
        var afterEnter = clock.Stage;
        canvas.RaiseKeyDown(Keys.Enter); // Second is final: commit

        Assert.Multiple(() =>
        {
            Assert.That(afterTab, Is.EqualTo(ClockFaceStage.Minute));
            Assert.That(afterEnter, Is.EqualTo(ClockFaceStage.Second));
            Assert.That(committed, Is.EqualTo(1));
        });
    }

    [Test]
    public void Escape_cancels()
    {
        var clock = CreatePicker(out var canvas);
        var cancelled = 0;
        clock.Cancelled = () => ++cancelled;

        canvas.RaiseKeyDown(Keys.Escape);

        Assert.That(cancelled, Is.EqualTo(1));
    }

    [Test]
    public void Hiding_the_seconds_pulls_a_seconds_stage_back_to_the_minute()
    {
        var clock = new ClockFace { ShowSeconds = true, Stage = ClockFaceStage.Second };

        clock.ShowSeconds = false;

        Assert.That(clock.Stage, Is.EqualTo(ClockFaceStage.Minute));
    }
}
