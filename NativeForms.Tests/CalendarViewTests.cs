using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class CalendarViewTests
{
    // The fixture calendar is 700x1100 so a full week fits without a vertical scrollbar (the content
    // is 24 h × 22 px/30-min-slot ≈ 1056 px, below the ~1078 px body), which keeps the geometry the
    // tests aim clicks at deterministic. It shows the week of Wednesday 2026-07-15 (Monday-first: Mon
    // 2026-07-13 .. Sun 2026-07-19) with "now" pinned to 2026-07-15 10:30.

    private static readonly DateTime _Week = new(2026, 7, 15);
    private static readonly DateTime _NowInstant = new(2026, 7, 15, 10, 30, 0);

    private static readonly Color _Red = Color.FromArgb(0xFF, 0xD0, 0x30, 0x30);
    private static readonly Color _Blue = Color.FromArgb(0xFF, 0x30, 0x60, 0xD0);

    private static Appointment[] SampleWeek() =>
    [
        new("Review", new(2026, 7, 13, 15, 0, 0), new(2026, 7, 13, 16, 0, 0)),
        new("Conference", new(2026, 7, 14), new(2026, 7, 15), allDay: true),
        new("Standup", new(2026, 7, 15, 9, 0, 0), new(2026, 7, 15, 9, 30, 0), color: _Red, tag: "s"),
        new("Design", new(2026, 7, 15, 9, 15, 0), new(2026, 7, 15, 10, 0, 0), color: _Blue, location: "Room 2"),
        new("Lunch", new(2026, 7, 16, 12, 0, 0), new(2026, 7, 16, 13, 0, 0)),
    ];

    private static CalendarView CreateCalendar(out HeadlessCanvasPeer canvas, Size? size = null)
    {
        var calendar = new CalendarView
        {
            Bounds = new(Point.Empty, size ?? new Size(700, 1100)),
            SelectedDate = _Week,
            Now = _NowInstant,
        };
        calendar.SetAppointments(SampleWeek());

        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(calendar);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return calendar;
    }

    private static void Click(HeadlessCanvasPeer canvas, int x, int y)
    {
        canvas.RaiseMouseDown(x, y);
        canvas.RaiseMouseUp(x, y);
    }

    private static int IndexOf(CalendarView calendar, string subject)
    {
        for (var i = 0; i < calendar.AppointmentCount; ++i)
            if (calendar.SnapshotAppointment(i).Subject == subject)
                return i;

        return -1;
    }

    [Test]
    public void Defaults_are_a_monday_first_week_view_on_a_thirty_minute_scale()
    {
        var calendar = new CalendarView();
        Assert.Multiple(() =>
        {
            Assert.That(calendar.ViewMode, Is.EqualTo(CalendarViewMode.Week));
            Assert.That(calendar.TimeScale, Is.EqualTo(30));
            Assert.That(calendar.FirstDayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(calendar.WorkDayStart, Is.EqualTo(new TimeSpan(8, 0, 0)));
            Assert.That(calendar.WorkDayEnd, Is.EqualTo(new TimeSpan(17, 0, 0)));
            Assert.That(calendar.AppointmentCount, Is.Zero);
            Assert.That(calendar.SelectedAppointment, Is.Null);
        });
    }

    [Test]
    public void SetAppointments_snapshots_and_sorts_by_start()
    {
        var calendar = CreateCalendar(out _);
        Assert.Multiple(() =>
        {
            Assert.That(calendar.AppointmentCount, Is.EqualTo(5));
            Assert.That(calendar.SnapshotAppointment(0).Subject, Is.EqualTo("Review"), "the earliest start leads");
            Assert.That(calendar.SnapshotAppointment(4).Subject, Is.EqualTo("Lunch"), "the latest start trails");
            Assert.That(calendar.SelectedAppointment, Is.Null, "nothing is selected before a gesture");
        });
    }

    [Test]
    public void SetAppointments_with_a_selector_projects_the_source()
    {
        var calendar = new CalendarView { Bounds = new(0, 0, 700, 600) };
        var rows = new[] { ("A", new DateTime(2026, 7, 15, 9, 0, 0)), ("B", new DateTime(2026, 7, 15, 8, 0, 0)) };
        calendar.SetAppointments(rows, r => new Appointment(r.Item1, r.Item2, r.Item2.AddHours(1)));

        Assert.Multiple(() =>
        {
            Assert.That(calendar.AppointmentCount, Is.EqualTo(2));
            Assert.That(calendar.SnapshotAppointment(0).Subject, Is.EqualTo("B"), "sorted by start, 08:00 first");
        });
    }

    [TestCase(CalendarViewMode.Day, 1)]
    [TestCase(CalendarViewMode.WorkWeek, 5)]
    [TestCase(CalendarViewMode.Week, 7)]
    [TestCase(CalendarViewMode.Month, 7)]
    public void VisibleDayCount_follows_the_view_mode(CalendarViewMode mode, int expected)
    {
        var calendar = CreateCalendar(out _);
        calendar.ViewMode = mode;
        Assert.That(calendar.VisibleDayCount, Is.EqualTo(expected));
    }

    [Test]
    public void FirstVisibleDate_starts_the_week_on_the_first_day_of_week()
    {
        var calendar = CreateCalendar(out _);
        Assert.Multiple(() =>
        {
            Assert.That(calendar.FirstVisibleDate, Is.EqualTo(new DateTime(2026, 7, 13)), "Monday of the shown week");

            calendar.ViewMode = CalendarViewMode.Day;
            Assert.That(calendar.FirstVisibleDate, Is.EqualTo(_Week));

            calendar.ViewMode = CalendarViewMode.WorkWeek;
            Assert.That(calendar.FirstVisibleDate, Is.EqualTo(new DateTime(2026, 7, 13)), "WorkWeek starts Monday");
        });
    }

    [Test]
    public void Clicking_an_appointment_selects_it_and_raises_SelectionChanged()
    {
        var calendar = CreateCalendar(out var canvas);
        var changes = 0;
        calendar.SelectionChanged += (_, _) => ++changes;

        var index = IndexOf(calendar, "Lunch");
        Assert.That(calendar.TryGetAppointmentBounds(index, out var bounds), Is.True, "Lunch should be laid out this week");
        Click(canvas, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectedAppointmentIndex, Is.EqualTo(index));
            Assert.That(calendar.SelectedAppointment?.Subject, Is.EqualTo("Lunch"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clicking_empty_space_clears_the_selection()
    {
        var calendar = CreateCalendar(out var canvas);
        var index = IndexOf(calendar, "Lunch");
        calendar.TryGetAppointmentBounds(index, out var bounds);
        Click(canvas, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        Assert.That(calendar.SelectedAppointment, Is.Not.Null);

        // The hour gutter is always empty space.
        Click(canvas, 10, 400);
        Assert.That(calendar.SelectedAppointment, Is.Null);
    }

    [Test]
    public void A_double_click_on_an_appointment_raises_AppointmentActivate()
    {
        var calendar = CreateCalendar(out var canvas);
        Appointment? activated = null;
        calendar.AppointmentActivate += (_, e) => activated = e.Appointment;

        var index = IndexOf(calendar, "Lunch");
        calendar.TryGetAppointmentBounds(index, out var bounds);
        var x = bounds.X + (bounds.Width / 2);
        var y = bounds.Y + (bounds.Height / 2);
        Click(canvas, x, y);
        Click(canvas, x, y); // the second press inside the double-click window opens it

        Assert.That(activated?.Subject, Is.EqualTo("Lunch"));
    }

    [Test]
    public void Enter_activates_the_selected_appointment()
    {
        var calendar = CreateCalendar(out var canvas);
        Appointment? activated = null;
        calendar.AppointmentActivate += (_, e) => activated = e.Appointment;

        var index = IndexOf(calendar, "Lunch");
        calendar.TryGetAppointmentBounds(index, out var bounds);
        Click(canvas, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(activated?.Subject, Is.EqualTo("Lunch"));
    }

    [Test]
    public void Overlapping_appointments_pack_side_by_side()
    {
        var calendar = CreateCalendar(out _);
        var standup = IndexOf(calendar, "Standup");
        var design = IndexOf(calendar, "Design");
        Assert.That(calendar.TryGetAppointmentBounds(standup, out var a), Is.True);
        Assert.That(calendar.TryGetAppointmentBounds(design, out var b), Is.True);

        Assert.Multiple(() =>
        {
            // Two overlapping appointments each take roughly half the day column and sit next to each
            // other, not stacked on the same x.
            Assert.That(a.X, Is.Not.EqualTo(b.X), "packed columns must differ in x");
            Assert.That(a.Right, Is.LessThanOrEqualTo(b.X + 4).Or.GreaterThanOrEqualTo(b.Right - 4), "the two columns do not overlap");
        });
    }

    [Test]
    public void An_empty_time_drag_raises_TimeRangeSelected()
    {
        var calendar = CreateCalendar(out var canvas, new Size(700, 1100));
        calendar.ViewMode = CalendarViewMode.Day;
        calendar.SetAppointments(Array.Empty<Appointment>());
        DateRangeEventArgs? range = null;
        calendar.TimeRangeSelected += (_, e) => range = e;

        // A drag down an empty day column.
        canvas.RaiseMouseDown(200, 300);
        canvas.RaiseMouseMove(200, 420);
        canvas.RaiseMouseUp(200, 420);

        Assert.That(range, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(range!.Start.Date, Is.EqualTo(_Week), "the range lands on the shown day");
            Assert.That(range.End, Is.GreaterThan(range.Start), "the range spans forward in time");
        });
    }

    [Test]
    public void Next_and_Previous_page_by_the_view_unit()
    {
        var calendar = CreateCalendar(out _);

        calendar.ViewMode = CalendarViewMode.Day;
        calendar.Next();
        Assert.That(calendar.SelectedDate, Is.EqualTo(_Week.AddDays(1)), "Day pages one day");

        calendar.ViewMode = CalendarViewMode.Week;
        var before = calendar.SelectedDate;
        calendar.Next();
        Assert.That(calendar.SelectedDate, Is.EqualTo(before.AddDays(7)), "Week pages seven days");

        calendar.ViewMode = CalendarViewMode.Month;
        before = calendar.SelectedDate;
        calendar.Previous();
        Assert.That(calendar.SelectedDate, Is.EqualTo(before.AddMonths(-1)), "Month pages one month");
    }

    [Test]
    public void GoToToday_returns_to_the_now_date()
    {
        var calendar = CreateCalendar(out _);
        calendar.ViewMode = CalendarViewMode.Day;
        calendar.Next();
        calendar.Next();
        calendar.GoToToday();
        Assert.That(calendar.SelectedDate, Is.EqualTo(_NowInstant.Date));
    }

    [Test]
    public void The_wheel_scrolls_the_time_grid_in_week_view()
    {
        // A short calendar so the day content overflows and the grid scrolls.
        var calendar = CreateCalendar(out var canvas, new Size(700, 320));
        var before = calendar.ScrollOffset;
        canvas.RaiseMouseWheel(-120, 300, 200); // down
        Assert.That(calendar.ScrollOffset, Is.GreaterThan(before), "a downward notch scrolls the grid down");

        var mid = calendar.ScrollOffset;
        canvas.RaiseMouseWheel(120, 300, 200); // up
        Assert.That(calendar.ScrollOffset, Is.LessThan(mid), "an upward notch scrolls back up");
    }

    [Test]
    public void The_wheel_pages_the_month_view()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.ViewMode = CalendarViewMode.Month;
        var before = calendar.SelectedDate;
        canvas.RaiseMouseWheel(-120, 300, 200);
        Assert.That(calendar.SelectedDate, Is.EqualTo(before.AddMonths(1)));
    }

    [Test]
    public void The_time_grid_paints_headers_appointments_and_the_now_line()
    {
        var calendar = CreateCalendar(out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Standup"), Is.True, "a timed appointment is painted");
            Assert.That(g.DrewText("Conference"), Is.True, "an all-day appointment shows in the band");
            Assert.That(g.Operations.Exists(o => o.StartsWith("fillellipse")), Is.True, "the now line's marker dot is painted");
        });
    }

    [Test]
    public void The_month_view_paints_day_numbers_and_chips()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.ViewMode = CalendarViewMode.Month;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("15"), Is.True, "the day-of-month number is painted");
            Assert.That(g.DrewText("Standup"), Is.True, "an appointment chip is painted in its day cell");
        });
    }

    [Test]
    public void The_layout_stays_bounded_for_a_hundred_thousand_appointments()
    {
        var calendar = new CalendarView { Bounds = new(0, 0, 700, 1100), SelectedDate = _Week, Now = _NowInstant };
        var many = new Appointment[100_000];
        var start = new DateTime(2020, 1, 1, 9, 0, 0);
        for (var i = 0; i < many.Length; ++i)
        {
            var when = start.AddMinutes(i * 137);
            many[i] = new Appointment("Item " + i, when, when.AddMinutes(30));
        }

        calendar.SetAppointments(many);

        // Only the shown week's appointments are ever laid out, so the box count is tiny however large
        // the bound set is — the virtualization guarantee.
        Assert.That(calendar.LaidOutBoxCount, Is.LessThan(200), $"{calendar.LaidOutBoxCount} boxes for a week out of 100k appointments");
    }
}
