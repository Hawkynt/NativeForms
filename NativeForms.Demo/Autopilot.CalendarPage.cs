using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class Autopilot
{
    /// <summary>
    /// The Calendar page: the view-switch toolbar (Day/Work Week/Week/Month), Today/previous/next
    /// navigation, selecting an appointment, opening one with a double-click, and dragging empty time
    /// to raise a new-appointment range.
    /// </summary>
    private void DriveCalendar()
    {
        Section("Calendar");
        this.SelectTab("Calendar");
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var calendar = _form.Part<CalendarView>("calendar.view");

        this.Check("CalendarView: the toolbar buttons switch the view mode", () =>
        {
            this.Click(_form.Part<Button>("calendar.day"), 45, 13);
            this.Expect("VisibleDayCount in Day view", this.Read(() => calendar.VisibleDayCount), 1);

            this.Click(_form.Part<Button>("calendar.month"), 45, 13);
            this.Expect("ViewMode after clicking Month", this.Read(() => calendar.ViewMode), CalendarViewMode.Month);

            this.Click(_form.Part<Button>("calendar.week"), 45, 13);
            this.Expect("VisibleDayCount back in Week view", this.Read(() => calendar.VisibleDayCount), 7);
        });

        this.Check("CalendarView: Next and Previous page the week, Today returns", () =>
        {
            var start = this.Read(() => calendar.SelectedDate);
            this.Click(_form.Part<Button>("calendar.next"), 40, 13);
            this.Expect("SelectedDate after Next", this.Read(() => calendar.SelectedDate), start.AddDays(7));

            this.Click(_form.Part<Button>("calendar.prev"), 40, 13);
            this.Expect("SelectedDate after Previous", this.Read(() => calendar.SelectedDate), start);

            this.Click(_form.Part<Button>("calendar.today"), 40, 13);
            this.Expect("SelectedDate after Today", this.Read(() => calendar.SelectedDate), this.Read(() => calendar.Now).Date);
        });

        this.Screenshot("state-calendar-week");

        this.Check("CalendarView: clicking an appointment selects it and reports it", () =>
        {
            var box = this.FirstAppointmentBox(calendar);
            if (box.IsEmpty)
            {
                this.Fail("no appointment was laid out in the shown week");
                return;
            }

            this.Click(calendar, box.X + (box.Width / 2), box.Y + (box.Height / 2));
            this.ExpectTrue("no appointment became selected", this.Read(() => calendar.SelectedAppointment) is not null);
            this.ExpectTrue(
                $"the status line did not report the selection, it reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Calendar: selected ", StringComparison.Ordinal));
        });

        this.Check("CalendarView: a double click opens the appointment for edit", () =>
        {
            var box = this.FirstAppointmentBox(calendar);
            if (box.IsEmpty)
            {
                this.Fail("no appointment was laid out in the shown week");
                return;
            }

            var x = box.X + (box.Width / 2);
            var y = box.Y + (box.Height / 2);
            this.Click(calendar, x, y);
            this.Click(calendar, x, y); // the second press inside the double-click window opens it
            this.ExpectTrue(
                $"a double click should have opened the appointment, status reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Calendar: open ", StringComparison.Ordinal));
        });

        this.Check("CalendarView: dragging empty time raises a new-appointment range", () =>
        {
            // A day the demo week leaves empty, in Day view, so the whole body is draggable emptiness.
            var freeDay = this.Read(() => calendar.Now).Date.AddDays(4); // the free Sunday of the shown week
            this.Do(() =>
            {
                calendar.ViewMode = CalendarViewMode.Day;
                calendar.SelectedDate = freeDay;
            });

            this.Drag(calendar, new(200, 120), new(200, 240));
            this.ExpectTrue(
                $"the drag did not report a new appointment, status reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Calendar: new appointment", StringComparison.Ordinal));

            this.Do(() => calendar.ViewMode = CalendarViewMode.Day);
        });

        this.Screenshot("state-calendar-day");

        this.Do(() => calendar.ViewMode = CalendarViewMode.Month);
        this.Settle(80);
        this.Screenshot("state-calendar-month");
        this.Do(() => calendar.ViewMode = CalendarViewMode.Week);
    }

    /// <summary>The client rectangle of the first laid-out appointment in the current view, or empty.</summary>
    private Rectangle FirstAppointmentBox(CalendarView calendar) => this.Read(() =>
    {
        for (var i = 0; i < calendar.AppointmentCount; ++i)
            if (calendar.TryGetAppointmentBounds(i, out var bounds) && bounds.Width > 6 && bounds.Height > 6)
                return bounds;

        return Rectangle.Empty;
    });
}
