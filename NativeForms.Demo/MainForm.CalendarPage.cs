using System.Drawing;
using System.Globalization;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>A meeting row bound into the calendar through a selector, so the demo exercises the
    /// same reflection-free binding a real application would use. <see cref="Movable"/> maps straight to
    /// <see cref="Appointment.Movable"/>, so a locked entry (a company holiday) cannot be dragged.</summary>
    private sealed record Meeting(string Subject, DateTime Start, DateTime End, Color Category, bool AllDay = false, string Location = "", bool Movable = true);

    // The demo pins "now" to a fixed instant so the week is populated and the captures stay
    // deterministic across runs; the shown week is the one that holds it.
    private static readonly DateTime _CalendarNow = new(2026, 7, 15, 10, 30, 0);
    private static readonly DateTime _CalendarWeek = new(2026, 7, 13);

    private static readonly Color _CatWork = Color.FromArgb(0xFF, 0x2B, 0x57, 0xC0);
    private static readonly Color _CatPersonal = Color.FromArgb(0xFF, 0x2E, 0x8B, 0x57);
    private static readonly Color _CatFocus = Color.FromArgb(0xFF, 0xE8, 0x8A, 0x1A);
    private static readonly Color _CatExternal = Color.FromArgb(0xFF, 0x8A, 0x3F, 0xC0);
    private static readonly Color _CatUrgent = Color.FromArgb(0xFF, 0xD0, 0x30, 0x30);

    /// <summary>The realistic week the scheduler shows: several categories, overlapping meetings, a
    /// two-day all-day conference and a spread of times.</summary>
    private static Meeting[] BuildMeetings()
    {
        var mon = _CalendarWeek;
        return
        [
            new("Team sync", mon.AddHours(9), mon.AddHours(9).AddMinutes(30), _CatWork, Location: "Room A"),
            new("1:1 with Alex", mon.AddHours(9).AddMinutes(15), mon.AddHours(10), _CatPersonal),
            new("Design review", mon.AddDays(1).AddHours(11), mon.AddDays(1).AddHours(12).AddMinutes(30), _CatFocus, Location: "Room B"),
            new("Lunch w/ Sam", mon.AddDays(1).AddHours(12), mon.AddDays(1).AddHours(13), _CatPersonal),
            new("Standup", mon.AddDays(2).AddHours(9), mon.AddDays(2).AddHours(9).AddMinutes(15), _CatWork),
            new("Focus: PRD", mon.AddDays(2).AddHours(10), mon.AddDays(2).AddHours(12), _CatFocus, Location: "Desk"),
            new("Interview", mon.AddDays(2).AddHours(10).AddMinutes(30), mon.AddDays(2).AddHours(11).AddMinutes(30), _CatExternal, Location: "Room C"),
            new("Release", mon.AddDays(2).AddHours(14), mon.AddDays(2).AddHours(15), _CatUrgent),
            new("Conference", mon.AddDays(3), mon.AddDays(4), _CatExternal, AllDay: true, Location: "Berlin"),
            new("Talk prep", mon.AddDays(3).AddHours(16), mon.AddDays(3).AddHours(17), _CatFocus),
            new("Retro", mon.AddDays(4).AddHours(15), mon.AddDays(4).AddHours(16), _CatPersonal),
            new("Gym", mon.AddDays(5).AddHours(8), mon.AddDays(5).AddHours(9), _CatPersonal),
            // A locked entry: the whole day is off, and it must not be dragged onto another slot.
            new("Company holiday", mon.AddDays(4), mon.AddDays(5), _CatUrgent, AllDay: true, Movable: false),
        ];
    }

    /// <summary>
    /// The Calendar page: an Outlook-style <see cref="CalendarView"/> bound to a week of meetings in
    /// several categories, with overlapping items and an all-day conference, plus a toolbar that
    /// switches Day/Work Week/Week/Month and navigates Today/previous/next. Selecting, opening
    /// (double-click) and dragging a new time range each report into the status strip.
    /// </summary>
    private TabPage BuildCalendarPage()
    {
        var page = new TabPage("Calendar") { ImageIndex = _IconPurple };

        var calendar = new CalendarView
        {
            Bounds = new(16, 72, 948, 464),
            ViewMode = CalendarViewMode.Week,
            SelectedDate = _CalendarNow.Date,
            Now = _CalendarNow,
            TimeScale = 30,
        };
        // A mutable model the move events write back into: the control owns no storage, so a dropped
        // move updates this list and re-binds — exactly the setter/validation idiom a real app uses.
        var meetings = new List<Meeting>(BuildMeetings());
        void Rebind() => calendar.SetAppointments(meetings, static m => new Appointment(m.Subject, m.Start, m.End, m.AllDay, m.Location, m.Category, m, m.Movable));
        Rebind();

        calendar.SelectionChanged += (_, _) =>
            this.SetStatus(calendar.SelectedAppointment is { } appt
                ? $"Calendar: selected \"{appt.Subject}\"."
                : "Calendar: nothing selected.");
        calendar.AppointmentActivate += (_, e) =>
            this.SetStatus($"Calendar: open \"{e.Appointment.Subject}\" for edit.");
        calendar.TimeRangeSelected += (_, e) =>
            this.SetStatus($"Calendar: new appointment {e.Start.ToString("ddd HH:mm", CultureInfo.InvariantCulture)} – {e.End.ToString("HH:mm", CultureInfo.InvariantCulture)}.");
        calendar.AppointmentMoved += (_, e) =>
        {
            // Apply the proposal to the model item the appointment carried, then re-bind. The control
            // never mutated its own snapshot; the new time only shows once the fresh set is bound.
            if (e.Appointment.Tag is not Meeting moved)
                return;

            var i = meetings.IndexOf(moved);
            if (i < 0)
                return;

            meetings[i] = moved with { Start = e.Start, End = e.End };
            Rebind();
            this.SetStatus($"Calendar: moved \"{moved.Subject}\" to {e.Start.ToString("ddd HH:mm", CultureInfo.InvariantCulture)}.");
        };

        Button ViewButton(string text, int x, int width, CalendarViewMode mode)
        {
            var button = new Button { Text = text, Bounds = new(x, 36, width, 26) };
            button.Click += (_, _) =>
            {
                calendar.ViewMode = mode;
                this.SetStatus($"Calendar: {text} view.");
            };
            return button;
        }

        var day = ViewButton("Day", 16, 70, CalendarViewMode.Day);
        var workWeek = ViewButton("Work Week", 94, 120, CalendarViewMode.WorkWeek);
        var week = ViewButton("Week", 222, 80, CalendarViewMode.Week);
        var month = ViewButton("Month", 310, 84, CalendarViewMode.Month);

        var today = new Button { Text = "Today", Bounds = new(414, 36, 84, 26) };
        today.Click += (_, _) =>
        {
            calendar.GoToToday();
            this.SetStatus("Calendar: jumped to today.");
        };
        var prev = new Button { Text = "◀ Prev", Bounds = new(506, 36, 96, 26) };
        prev.Click += (_, _) =>
        {
            calendar.Previous();
            this.SetStatus("Calendar: previous period.");
        };
        var next = new Button { Text = "Next ▶", Bounds = new(610, 36, 96, 26) };
        next.Click += (_, _) =>
        {
            calendar.Next();
            this.SetStatus("Calendar: next period.");
        };

        page.Controls.AddRange(
            Caption("CalendarView — an Outlook-style scheduler; drag empty time to add, drag a movable appointment to move it.", 16, 12, 948),
            day, workWeek, week, month, today, prev, next,
            calendar);

        this.OnReset(() =>
        {
            calendar.ViewMode = CalendarViewMode.Week;
            calendar.SelectedDate = _CalendarNow.Date;
            meetings.Clear();
            meetings.AddRange(BuildMeetings());
            Rebind();
            calendar.Invalidate();
        });

        this.Publish("calendar.page", page);
        this.Publish("calendar.view", calendar);
        this.Publish("calendar.day", day);
        this.Publish("calendar.week", week);
        this.Publish("calendar.month", month);
        this.Publish("calendar.today", today);
        this.Publish("calendar.next", next);
        this.Publish("calendar.prev", prev);
        return page;
    }
}
