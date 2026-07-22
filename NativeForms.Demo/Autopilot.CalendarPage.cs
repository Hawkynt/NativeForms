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

        this.Check("CalendarView: dragging a movable appointment reschedules it through the move events", () =>
        {
            var today = this.Read(() => calendar.Now).Date;
            this.Do(() =>
            {
                calendar.ViewMode = CalendarViewMode.Week;
                calendar.SelectedDate = today;
            });

            var box = this.LocateAppointment(calendar, "Team sync");
            if (box.IsEmpty)
            {
                this.Fail("could not locate the movable 'Team sync' appointment");
                return;
            }

            // LocateAppointment clicked this box to read its subject; let the double-click window lapse
            // so the drag's own press is a fresh single click (a move), not a second click (an activate).
            this.Settle(600);

            var from = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            var to = new Point(from.X, from.Y + 66); // three 30-minute slots down
            this.DragWithCapture(calendar, from, to, "state-calendar-move-drag");

            this.ExpectTrue(
                $"the move did not report, status reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Calendar: moved ", StringComparison.Ordinal));
        });

        this.Check("CalendarView: a locked appointment refuses to move", () =>
        {
            var box = this.LocateAppointment(calendar, "Company holiday");
            if (box.IsEmpty)
            {
                this.Fail("could not locate the locked 'Company holiday' appointment");
                return;
            }

            this.Settle(600); // clear the double-click window left by LocateAppointment's identifying click

            var centre = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            this.Drag(calendar, centre, new(centre.X, centre.Y + 160));

            this.ExpectTrue(
                $"a locked appointment must not move, status reads \"{this.Read(() => status.Text)}\"",
                !this.Read(() => status.Text).StartsWith("Calendar: moved ", StringComparison.Ordinal));
            this.ExpectTrue(
                "the locked appointment should still select on a press",
                string.Equals(this.Read(() => calendar.SelectedAppointment?.Subject), "Company holiday", StringComparison.Ordinal));
        });

        this.Do(_form.ResetToAuthoredState); // restore the moved meeting before the month captures

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

    /// <summary>Finds the laid-out box of the appointment with a given subject by selecting each in turn
    /// and reading back <see cref="CalendarView.SelectedAppointment"/> — the only subject the public API
    /// exposes. Returns its client rectangle, or empty when it is not on screen.</summary>
    private Rectangle LocateAppointment(CalendarView calendar, string subject)
    {
        var count = this.Read(() => calendar.AppointmentCount);
        for (var i = 0; i < count; ++i)
        {
            var index = i;
            var box = this.Read(() => calendar.TryGetAppointmentBounds(index, out var b) && b.Width > 6 && b.Height > 6 ? b : Rectangle.Empty);
            if (box.IsEmpty)
                continue;

            this.Click(calendar, box.X + (box.Width / 2), box.Y + (box.Height / 2));
            if (string.Equals(this.Read(() => calendar.SelectedAppointment?.Subject), subject, StringComparison.Ordinal))
                return box;
        }

        return Rectangle.Empty;
    }

    /// <summary>Presses inside a control, drags to a second offset and releases — like <see cref="Drag"/>
    /// but pausing at the mid-point to capture the screen, so a live move preview is photographed.</summary>
    private void DragWithCapture(Control control, Point from, Point to, string capture, int steps = 8)
    {
        var start = this.ScreenOf(control, from.X, from.Y);
        var end = this.ScreenOf(control, to.X, to.Y);
        var root = this.RootAt(start);
        this.Pump("a drag press", () =>
        {
            Injection.Move(root, start);
            _landings.Add(Injection.Press(root, start, 1, 0).WidgetName);
        });

        for (var i = 1; i <= steps; ++i)
        {
            var point = new Point(
                start.X + ((end.X - start.X) * i / steps),
                start.Y + ((end.Y - start.Y) * i / steps));
            this.Pump("a drag move", () => Injection.Move(root, point, buttonHeld: true));
            Thread.Sleep(8);
        }

        // Photograph the drag at full displacement, still held: the translucent ghost sits over the
        // landing slot, clear of the original, before the drop commits it.
        this.Settle();
        this.Screenshot(capture);

        this.Pump("a drag release", () => Injection.Release(root, end, 1, 0));
        this.Settle();
    }
}
