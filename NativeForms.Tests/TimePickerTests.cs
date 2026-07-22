using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TimePickerTests
{
    // The fixture picker is 160x24 with the default theme (RowHeight 22, ScrollBarSize 16) and the
    // headless text engine's fixed 7 px per character: a two-digit part is 14 px wide, a separator
    // 7 px. Starting at the 4 px padding the parts therefore sit at x = 4..17 (hour), 25..38
    // (minute) and 46..59 (second), with the AM/PM part — when shown — at 67..80. The spinner
    // column is 17 px wide, so its buttons cover x = 143..159.
    private const int _HourX = 8;
    private const int _MinuteX = 30;
    private const int _SecondX = 50;
    private const int _MeridiemX = 72;
    private const int _SpinnerX = 150;

    private static TimePicker CreatePicker(out HeadlessCanvasPeer canvas, TimeSpan? value = null)
    {
        var picker = new TimePicker { Bounds = new(10, 10, 160, 24), Value = value ?? new(9, 30, 15) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return picker;
    }

    private static void Click(HeadlessCanvasPeer canvas, int x, int y = 12)
    {
        canvas.RaiseMouseDown(x, y);
        canvas.RaiseMouseUp(x, y);
    }

    [Test]
    public void Defaults_are_a_24_hour_field_with_seconds_over_the_whole_day()
    {
        var picker = new TimePicker();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Use24HourClock, Is.True, "the invariant culture the repo pins is 24-hour");
            Assert.That(picker.ShowSeconds, Is.True);
            Assert.That(picker.SelectedField, Is.EqualTo(TimePickerField.Hour));
            Assert.That(picker.MinTime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(picker.MaxTime, Is.EqualTo(new TimeSpan(23, 59, 59)));
            Assert.That(picker.Value.Milliseconds, Is.Zero, "the field cannot show sub-second precision");
        });
    }

    [Test]
    public void Value_assignment_raises_ValueChanged_once_and_drops_sub_second_precision()
    {
        var picker = new TimePicker { Value = TimeSpan.Zero };
        var raised = 0;
        picker.ValueChanged += (_, _) => ++raised;

        picker.Value = new(0, 7, 30, 45, 900);
        picker.Value = new(0, 7, 30, 45, 100); // same whole second: no change, no event

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(7, 30, 45)));
            Assert.That(raised, Is.EqualTo(1));
        });
    }

    [Test]
    public void The_field_paints_the_selected_part_only_while_focused()
    {
        var picker = CreatePicker(out var canvas);
        picker.Focus();

        var focused = canvas.RaisePaint();
        canvas.RaiseLostFocus();
        var unfocused = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(unfocused.DrewText("09"), Is.True, "the hour part");
            Assert.That(unfocused.DrewText("30"), Is.True, "the minute part");
            Assert.That(unfocused.DrewText("15"), Is.True, "the second part");
            Assert.That(unfocused.Operations.Any(o => o.EndsWith(" 4,1,14,22", StringComparison.Ordinal)), Is.False, "an unfocused field must not highlight a part");
            Assert.That(focused.Operations.Any(o => o.StartsWith("fill ", StringComparison.Ordinal) && o.EndsWith(" 4,1,14,22", StringComparison.Ordinal)), Is.True, "the focused hour part carries the selection fill");
        });
    }

    [Test]
    public void A_twelve_hour_field_paints_the_meridiem_and_a_one_to_twelve_hour()
    {
        var picker = CreatePicker(out var canvas, new(13, 5, 0));
        picker.Use24HourClock = false;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("01"), Is.True, "13:00 reads as 01 on a 12-hour clock");
            Assert.That(g.DrewText("PM"), Is.True);
            Assert.That(g.DrewText("13"), Is.False, "the 24-hour hour must be gone");
        });
    }

    [Test]
    public void Hiding_the_seconds_drops_them_from_the_value_and_from_the_field()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 15));

        picker.ShowSeconds = false;
        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(9, 30, 0)));
            Assert.That(g.DrewText("15"), Is.False, "the seconds part is gone from the field");
        });
    }

    [Test]
    public void Clicking_a_part_puts_the_caret_on_it()
    {
        var picker = CreatePicker(out var canvas);

        Click(canvas, _MinuteX);
        var minute = picker.SelectedField;
        Click(canvas, _SecondX);
        var second = picker.SelectedField;
        Click(canvas, _HourX);

        Assert.Multiple(() =>
        {
            Assert.That(minute, Is.EqualTo(TimePickerField.Minute));
            Assert.That(second, Is.EqualTo(TimePickerField.Second));
            Assert.That(picker.SelectedField, Is.EqualTo(TimePickerField.Hour));
        });
    }

    [Test]
    public void Clicking_the_meridiem_part_selects_it_only_on_a_twelve_hour_field()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 15));
        picker.Use24HourClock = false;

        Click(canvas, _MeridiemX);

        Assert.That(picker.SelectedField, Is.EqualTo(TimePickerField.Meridiem));
    }

    [Test]
    public void The_spinner_buttons_step_the_part_under_the_caret()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 15));

        Click(canvas, _SpinnerX, 4); // upper half: the up button
        var afterHour = picker.Value;

        Click(canvas, _MinuteX);
        Click(canvas, _SpinnerX, 20); // lower half: the down button
        var afterMinute = picker.Value;

        Assert.Multiple(() =>
        {
            Assert.That(afterHour, Is.EqualTo(new TimeSpan(10, 30, 15)), "the caret sat on the hour");
            Assert.That(afterMinute, Is.EqualTo(new TimeSpan(10, 29, 15)), "the caret had moved to the minute");
        });
    }

    [Test]
    public void Holding_a_spinner_button_auto_repeats()
    {
        var picker = new TimePicker { Bounds = new(10, 10, 160, 24), Value = new(9, 30, 15) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(_SpinnerX, 4); // steps once and arms the initial 500 ms delay
        var timer = backend.Timers.Single();
        timer.FireTick(); // the first repeat switches to the 50 ms cadence
        timer.FireTick();
        canvas.RaiseMouseUp(_SpinnerX, 4);

        Assert.Multiple(() =>
        {
            Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500, 50 }));
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(12, 30, 15)), "one press plus two ticks is three steps");
            Assert.That(timer.IsRunning, Is.False, "release stops the autorepeat");
        });
    }

    [Test]
    public void Left_and_right_walk_the_visible_parts_and_stop_at_the_ends()
    {
        var picker = CreatePicker(out var canvas);
        picker.Focus();

        canvas.RaiseKeyDown(Keys.Left); // already on the hour: stays
        var atStart = picker.SelectedField;
        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Right);
        var atSecond = picker.SelectedField;
        canvas.RaiseKeyDown(Keys.Right); // no meridiem on a 24-hour field: stays
        var atEnd = picker.SelectedField;

        Assert.Multiple(() =>
        {
            Assert.That(atStart, Is.EqualTo(TimePickerField.Hour));
            Assert.That(atSecond, Is.EqualTo(TimePickerField.Second));
            Assert.That(atEnd, Is.EqualTo(TimePickerField.Second));
        });
    }

    [Test]
    public void Right_skips_the_hidden_seconds_and_lands_on_the_meridiem()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 0));
        picker.ShowSeconds = false;
        picker.Use24HourClock = false;
        picker.Focus();

        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Right);

        Assert.That(picker.SelectedField, Is.EqualTo(TimePickerField.Meridiem));
    }

    [Test]
    public void Up_and_down_step_the_selected_part_wrapping_without_a_carry()
    {
        var picker = CreatePicker(out var canvas, new(23, 59, 59));
        picker.Focus();

        canvas.RaiseKeyDown(Keys.Up);
        var hour = picker.Value;
        picker.SelectedField = TimePickerField.Minute;
        canvas.RaiseKeyDown(Keys.Up);
        var minute = picker.Value;

        Assert.Multiple(() =>
        {
            Assert.That(hour, Is.EqualTo(new TimeSpan(0, 59, 59)), "the hour wraps 23 -> 00 without touching the minute");
            Assert.That(minute, Is.EqualTo(new TimeSpan(0, 0, 59)), "the minute wraps 59 -> 00 without carrying into the hour");
        });
    }

    [Test]
    public void The_meridiem_part_flips_the_half_day_either_way()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 0));
        picker.Use24HourClock = false;
        picker.SelectedField = TimePickerField.Meridiem;
        picker.Focus();

        canvas.RaiseKeyDown(Keys.Up);
        var afternoon = picker.Value;
        canvas.RaiseKeyDown(Keys.Down);

        Assert.Multiple(() =>
        {
            Assert.That(afternoon, Is.EqualTo(new TimeSpan(21, 30, 0)));
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(9, 30, 0)));
        });
    }

    [Test]
    public void A_step_out_of_the_min_max_window_is_refused()
    {
        var picker = CreatePicker(out var canvas, new(9, 0, 0));
        picker.MinTime = new(8, 0, 0);
        picker.MaxTime = new(10, 0, 0);
        picker.Focus();

        canvas.RaiseKeyDown(Keys.Up); // 10:00 is still inside
        var inside = picker.Value;
        canvas.RaiseKeyDown(Keys.Up); // 11:00 is not: refused, not clamped

        Assert.Multiple(() =>
        {
            Assert.That(inside, Is.EqualTo(new TimeSpan(10, 0, 0)));
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(10, 0, 0)));
        });
    }

    [Test]
    public void Assignments_clamp_into_the_window_and_Home_End_jump_to_its_edges()
    {
        var picker = CreatePicker(out var canvas, new(9, 0, 0));
        picker.MinTime = new(8, 0, 0);
        picker.MaxTime = new(10, 0, 0);
        picker.Focus();

        picker.Value = new(3, 0, 0);
        var clampedLow = picker.Value;
        picker.Value = new(22, 0, 0);
        var clampedHigh = picker.Value;
        canvas.RaiseKeyDown(Keys.Home);
        var home = picker.Value;
        canvas.RaiseKeyDown(Keys.End);

        Assert.Multiple(() =>
        {
            Assert.That(clampedLow, Is.EqualTo(new TimeSpan(8, 0, 0)));
            Assert.That(clampedHigh, Is.EqualTo(new TimeSpan(10, 0, 0)));
            Assert.That(home, Is.EqualTo(new TimeSpan(8, 0, 0)));
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(10, 0, 0)));
        });
    }

    [Test]
    public void A_reversed_window_throws()
    {
        var picker = new TimePicker { MinTime = new(8, 0, 0), MaxTime = new(10, 0, 0) };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.MinTime = new(11, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.MaxTime = new(7, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.MinTime = TimeSpan.FromHours(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.MaxTime = TimeSpan.FromDays(2));
        });
    }

    [Test]
    public void The_wheel_steps_the_selected_part()
    {
        var picker = CreatePicker(out var canvas, new(9, 30, 0));
        picker.SelectedField = TimePickerField.Minute;

        canvas.RaiseMouseWheel(120);
        var up = picker.Value;
        canvas.RaiseMouseWheel(-120);

        Assert.Multiple(() =>
        {
            Assert.That(up, Is.EqualTo(new TimeSpan(9, 31, 0)));
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(9, 30, 0)));
        });
    }

    // --- The double-click clock face --------------------------------------------------------------

    // The clock's PreferredSize under the default theme is 192x236 (see ClockFaceTests): centre at
    // (96, 118), the outer 24-hour ring at radius 67 so three o'clock — hour 15 — is at (163, 118),
    // and the OK affordance in the footer around (151, 219).
    private static TimePicker CreateClockPicker(out HeadlessCanvasPeer canvas, out HeadlessBackend backend, TimeSpan? value = null)
    {
        var picker = new TimePicker { Bounds = new(10, 10, 160, 24), Value = value ?? new(9, 30, 15) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return picker;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    private static void DoubleClickField(HeadlessCanvasPeer canvas, int x = _HourX)
    {
        canvas.RaiseMouseDown(x, 12);
        canvas.RaiseMouseDown(x, 12); // the second press inside the double-click window opens the clock
    }

    [Test]
    public void A_double_click_on_the_field_opens_the_clock_below_it()
    {
        var picker = CreateClockPicker(out var canvas, out var backend);
        canvas.ScreenOrigin = new(300, 400);

        DoubleClickField(canvas);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(picker.ClockDroppedDown, Is.True);
            Assert.That(popup.ShowCalls, Is.EqualTo(new[] { (new Point(300, 424), new Size(192, 236)) }));
        });
    }

    [Test]
    public void A_double_click_on_the_spinner_does_not_open_the_clock()
    {
        var picker = CreateClockPicker(out var canvas, out _);

        canvas.RaiseMouseDown(_SpinnerX, 4);
        canvas.RaiseMouseDown(_SpinnerX, 4);

        Assert.That(picker.ClockDroppedDown, Is.False, "the spinner steps, it does not open the clock");
    }

    [Test]
    public void Picking_an_hour_on_the_clock_previews_into_the_field_and_raises_ValueChanged()
    {
        var picker = CreateClockPicker(out var canvas, out var backend);
        var changes = 0;
        picker.ValueChanged += (_, _) => ++changes;
        DoubleClickField(canvas);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(163, 118); // the outer three-o'clock number: hour 15

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(15, 30, 15)), "the hour previews live into the field");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(picker.ClockDroppedDown, Is.True, "picking the hour advances the stage, it does not close");
        });
    }

    [Test]
    public void The_OK_affordance_commits_the_previewed_value_and_closes()
    {
        var picker = CreateClockPicker(out var canvas, out var backend);
        DoubleClickField(canvas);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(163, 118); // hour 15
        popup.RaiseMouseUp(163, 118);
        popup.RaiseMouseDown(151, 219); // OK

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(15, 30, 15)));
            Assert.That(picker.ClockDroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void An_outside_click_cancels_the_clock_reverting_the_field()
    {
        var picker = CreateClockPicker(out var canvas, out var backend);
        DoubleClickField(canvas);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(163, 118); // preview hour 15
        popup.FireDismiss();            // a click outside the surface

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(9, 30, 15)), "dismissal reverts to the opening value");
            Assert.That(picker.ClockDroppedDown, Is.False);
        });
    }

    [Test]
    public void Escape_on_the_clock_cancels_reverting_the_field()
    {
        var picker = CreateClockPicker(out var canvas, out var backend);
        DoubleClickField(canvas);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(163, 118); // preview hour 15
        popup.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new TimeSpan(9, 30, 15)));
            Assert.That(picker.ClockDroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void The_committed_clock_value_is_clamped_into_the_window()
    {
        var picker = CreateClockPicker(out var canvas, out var backend, new(9, 0, 0));
        picker.MaxTime = new(12, 0, 0);
        DoubleClickField(canvas);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(163, 118); // hour 15 is past MaxTime

        Assert.That(picker.Value, Is.EqualTo(new TimeSpan(12, 0, 0)), "the preview clamps into [MinTime, MaxTime]");
    }

    [Test]
    public void Selecting_a_part_the_layout_hides_falls_back_to_the_hour()
    {
        var picker = new TimePicker { ShowSeconds = false };

        picker.SelectedField = TimePickerField.Second;
        var hidden = picker.SelectedField;
        picker.SelectedField = TimePickerField.Meridiem; // 24-hour by default

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo(TimePickerField.Hour));
            Assert.That(picker.SelectedField, Is.EqualTo(TimePickerField.Hour));
        });
    }
}
