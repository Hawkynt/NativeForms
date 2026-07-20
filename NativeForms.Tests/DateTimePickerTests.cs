using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class DateTimePickerTests
{
    // The fixture field is 180x24; its popup calendar is 182x176 (default theme, RowHeight 22):
    // title row 0-21, header row 22-43, then 26x22 day cells starting at y=44. The value sits in
    // July 2026 (Monday-first), so the popup's top-left cell is Monday, June 29.

    private static DateTimePicker CreatePicker(out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var picker = new DateTimePicker { Bounds = new(10, 10, 180, 24), Value = new(2026, 7, 19, 14, 30, 5) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return picker;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    private static void ClickPopup(HeadlessPopupPeer popup, int x, int y)
    {
        popup.RaiseMouseDown(x, y);
        popup.RaiseMouseUp(x, y);
    }

    [Test]
    public void Defaults_show_the_long_date_with_no_check_box()
    {
        var picker = new DateTimePicker();
        Assert.Multiple(() =>
        {
            Assert.That(picker.Format, Is.EqualTo(DateTimePickerFormat.Long));
            Assert.That(picker.ShowCheckBox, Is.False);
            Assert.That(picker.Checked, Is.True);
            Assert.That(picker.MinDate, Is.EqualTo(new DateTime(1753, 1, 1)));
            Assert.That(picker.MaxDate, Is.EqualTo(new DateTime(9998, 12, 31)));
            Assert.That(picker.DroppedDown, Is.False);
        });
    }

    [Test]
    public void Field_paints_the_value_in_the_invariant_format()
    {
        var picker = CreatePicker(out var canvas, out _);

        Assert.That(canvas.RaisePaint().DrewText("Sunday, 19 July 2026"), Is.True, "Long");

        picker.Format = DateTimePickerFormat.Short;
        Assert.That(canvas.RaisePaint().DrewText("07/19/2026"), Is.True, "Short");

        picker.Format = DateTimePickerFormat.Time;
        Assert.That(canvas.RaisePaint().DrewText("14:30:05"), Is.True, "Time");

        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy-MM-dd";
        Assert.That(canvas.RaisePaint().DrewText("2026-07-19"), Is.True, "Custom");
    }

    [Test]
    public void Open_shows_the_calendar_popup_below_the_field()
    {
        var picker = CreatePicker(out var canvas, out var backend);
        canvas.ScreenOrigin = new(300, 400);

        canvas.RaiseMouseDown(170, 12); // the arrow zone

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(picker.DroppedDown, Is.True);
            Assert.That(popup.ShowCalls, Is.EqualTo(new[] { (new Point(300, 424), new Size(7 * 26, 8 * 22)) }));
        });
    }

    [Test]
    public void Day_click_commits_the_value_preserving_the_time_and_raises_ValueChanged_once()
    {
        var picker = CreatePicker(out var canvas, out var backend);
        var changes = 0;
        picker.ValueChanged += (_, _) => ++changes;
        canvas.RaiseMouseDown(170, 12);
        var popup = PopupOf(backend);

        ClickPopup(popup, 110, 70); // July 10

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 10, 14, 30, 5)), "the day changed, the time survived");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(picker.DroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void Dismiss_and_Escape_cancel_without_committing()
    {
        var picker = CreatePicker(out var canvas, out var backend);
        var changes = 0;
        picker.ValueChanged += (_, _) => ++changes;
        canvas.RaiseMouseDown(170, 12);
        var popup = PopupOf(backend);

        popup.FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(picker.DroppedDown, Is.False);
            Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 19, 14, 30, 5)));
            Assert.That(changes, Is.Zero);
        });

        canvas.RaiseMouseDown(170, 12);
        canvas.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(picker.DroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void Keyboard_navigates_the_open_calendar_and_Enter_commits()
    {
        var picker = CreatePicker(out var canvas, out _);

        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Alt);
        canvas.RaiseKeyDown(Keys.Right); // July 20
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 20, 14, 30, 5)));
            Assert.That(picker.DroppedDown, Is.False);
        });
    }

    [Test]
    public void Closed_Up_and_Down_step_the_day_within_the_clamps()
    {
        var picker = CreatePicker(out var canvas, out _);
        picker.Value = new(2026, 7, 19);
        picker.MinDate = new(2026, 7, 18);
        picker.MaxDate = new(2026, 7, 20);

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 20)));

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 20)), "MaxDate blocks the step");

        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 18)));

        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 18)), "MinDate blocks the step");
    }

    [Test]
    public void Alt_Down_and_F4_open_the_drop_down()
    {
        var picker = CreatePicker(out var canvas, out _);

        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Alt);
        Assert.That(picker.DroppedDown, Is.True);

        canvas.RaiseKeyDown(Keys.Escape);
        Assert.That(picker.DroppedDown, Is.False);

        canvas.RaiseKeyDown(Keys.F4);
        Assert.That(picker.DroppedDown, Is.True);

        canvas.RaiseKeyDown(Keys.F4);
        Assert.That(picker.DroppedDown, Is.False);
    }

    [Test]
    public void Unchecked_check_box_greys_the_text_and_suppresses_commits()
    {
        var picker = CreatePicker(out var canvas, out var backend);
        picker.ShowCheckBox = true;
        picker.Checked = false;
        var changes = 0;
        picker.ValueChanged += (_, _) => ++changes;

        var g = canvas.RaisePaint();
        Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Sunday, 19 July 2026\"") && o.Contains("#FF9A9A9A")), Is.True, "value text in DisabledText");

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 19, 14, 30, 5)), "stepping is suppressed");

        canvas.RaiseMouseDown(170, 12);
        var popup = PopupOf(backend);
        ClickPopup(popup, 110, 70); // July 10

        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 7, 19, 14, 30, 5)), "the commit is suppressed");
            Assert.That(changes, Is.Zero);
            Assert.That(picker.DroppedDown, Is.False, "the popup still closes");
        });
    }

    [Test]
    public void Clicking_the_check_box_zone_toggles_Checked()
    {
        var picker = CreatePicker(out var canvas, out _);
        picker.ShowCheckBox = true;

        canvas.RaiseMouseDown(8, 12);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Checked, Is.False);
            Assert.That(picker.DroppedDown, Is.False, "the check box click does not open the popup");
        });

        canvas.RaiseMouseDown(8, 12);
        Assert.That(picker.Checked, Is.True);
    }

    [Test]
    public void Value_and_min_max_assignments_clamp_each_other()
    {
        var picker = new DateTimePicker { Value = new(2026, 7, 19) };
        var changes = 0;
        picker.ValueChanged += (_, _) => ++changes;

        picker.MinDate = new(2026, 8, 1);
        Assert.Multiple(() =>
        {
            Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 8, 1)), "raising MinDate drags the value up");
            Assert.That(changes, Is.EqualTo(1));
        });

        picker.Value = new(2020, 1, 1);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 8, 1)), "assignments clamp to MinDate");

        picker.MaxDate = new(2026, 8, 15);
        picker.Value = new(2030, 1, 1);
        Assert.That(picker.Value, Is.EqualTo(new DateTime(2026, 8, 15)), "assignments clamp to MaxDate");
    }

    [Test]
    public void DropDown_and_CloseUp_fire_at_open_and_close()
    {
        var picker = CreatePicker(out _, out var backend);
        var drops = 0;
        var closes = 0;
        picker.DropDown += (_, _) => ++drops;
        picker.CloseUp += (_, _) => ++closes;

        picker.OpenDropDown();
        Assert.That((drops, closes), Is.EqualTo((1, 0)));

        picker.CloseDropDown();
        Assert.That((drops, closes), Is.EqualTo((1, 1)));

        picker.OpenDropDown();
        PopupOf(backend).FireDismiss(); // light dismissal closes up too
        Assert.That((drops, closes), Is.EqualTo((2, 2)));
    }
}
