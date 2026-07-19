using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class MonthCalendarTests
{
    // The fixture calendar is 140x176 with the default theme (RowHeight 22): title row 0-21,
    // header row 22-43, then a 6x7 grid of 20x22 cells starting at y=44. It shows July 2026
    // (Monday-first), so the top-left cell is Monday, June 29.

    private static MonthCalendar CreateCalendar(out HeadlessCanvasPeer canvas)
    {
        var calendar = new MonthCalendar { Bounds = new(10, 10, 140, 176), TodayDate = new(2026, 7, 19) };
        calendar.SetSelectionRange(new(2026, 7, 15), new(2026, 7, 15));
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(calendar);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return calendar;
    }

    private static void Click(HeadlessCanvasPeer canvas, int x, int y, KeyModifiers modifiers = KeyModifiers.None)
    {
        canvas.RaiseMouseDown(x, y, MouseButtons.Left, modifiers);
        canvas.RaiseMouseUp(x, y, MouseButtons.Left, modifiers);
    }

    [Test]
    public void Defaults_select_today_with_a_monday_week_and_seven_day_cap()
    {
        var calendar = new MonthCalendar();
        Assert.Multiple(() =>
        {
            Assert.That(calendar.FirstDayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(calendar.MaxSelectionCount, Is.EqualTo(7));
            Assert.That(calendar.SelectionStart, Is.EqualTo(DateTime.Today));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(DateTime.Today));
            Assert.That(calendar.MinDate, Is.EqualTo(new DateTime(1753, 1, 1)));
            Assert.That(calendar.MaxDate, Is.EqualTo(new DateTime(9998, 12, 31)));
        });
    }

    [Test]
    public void First_grid_cell_is_the_first_day_of_week_on_or_before_the_month()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CalendarCore.FirstGridDate(new(2026, 7, 1), DayOfWeek.Monday), Is.EqualTo(new DateTime(2026, 6, 29)));
            Assert.That(CalendarCore.FirstGridDate(new(2026, 7, 1), DayOfWeek.Sunday), Is.EqualTo(new DateTime(2026, 6, 28)));
            Assert.That(CalendarCore.FirstGridDate(new(2026, 6, 1), DayOfWeek.Monday), Is.EqualTo(new DateTime(2026, 6, 1)), "a month starting on the first day of week keeps its own first");
        });
    }

    [Test]
    public void Title_and_headers_paint_the_invariant_names()
    {
        _ = CreateCalendar(out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("July 2026"), Is.True, "invariant month title");
            Assert.That(g.Operations, Does.Contain("text \"Mo\" #FF303030 MiddleCenter @0,22"), "Monday leads the header row");
            Assert.That(g.Operations, Does.Contain("text \"Su\" #FF303030 MiddleCenter @120,22"), "Sunday closes the header row");
        });
    }

    [Test]
    public void Header_order_follows_FirstDayOfWeek()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.FirstDayOfWeek = DayOfWeek.Sunday;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("text \"Su\" #FF303030 MiddleCenter @0,22"));
            Assert.That(g.Operations, Does.Contain("text \"Sa\" #FF303030 MiddleCenter @120,22"));
        });
    }

    [Test]
    public void Leading_and_trailing_days_paint_in_the_disabled_color()
    {
        _ = CreateCalendar(out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("text \"29\" #FF9A9A9A MiddleCenter @0,44"), "leading June 29");
            Assert.That(g.Operations, Does.Contain("text \"1\" #FF9A9A9A MiddleCenter @100,132"), "trailing August 1");
            Assert.That(g.Operations, Does.Contain("text \"16\" #FF1A1A1A MiddleCenter @60,88"), "a regular July day in control text");
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 40,88,20,22"), "the selected day is highlighted");
            Assert.That(g.Operations, Does.Contain("text \"15\" #FFFFFFFF MiddleCenter @40,88"), "the selected day in selection text");
        });
    }

    [Test]
    public void Today_is_circled_in_the_accent_color()
    {
        _ = CreateCalendar(out var canvas);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("ellipse #FF0078D4 121,89,18,20"), "July 19 wears the accent circle");
    }

    [Test]
    public void Click_selects_a_single_day_and_raises_both_events()
    {
        var calendar = CreateCalendar(out var canvas);
        var changes = 0;
        var selections = 0;
        DateRangeEventArgs? selected = null;
        calendar.DateChanged += (_, _) => ++changes;
        calendar.DateSelected += (_, e) =>
        {
            ++selections;
            selected = e;
        };

        Click(canvas, 85, 70); // July 10

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 10)));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(new DateTime(2026, 7, 10)));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(selections, Is.EqualTo(1));
            Assert.That(selected!.Start, Is.EqualTo(new DateTime(2026, 7, 10)));
            Assert.That(selected.End, Is.EqualTo(new DateTime(2026, 7, 10)));
        });
    }

    [Test]
    public void Shift_click_extends_the_range_capped_by_MaxSelectionCount()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.MaxSelectionCount = 5;

        Click(canvas, 85, 70); // July 10
        Click(canvas, 5, 115, KeyModifiers.Shift); // July 20, six days beyond the cap

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 10)));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(new DateTime(2026, 7, 14)), "capped at five days");
        });
    }

    [Test]
    public void Dragging_extends_the_range_until_the_mouse_is_released()
    {
        var calendar = CreateCalendar(out var canvas);
        var selections = 0;
        calendar.DateSelected += (_, _) => ++selections;

        canvas.RaiseMouseDown(85, 70); // July 10
        canvas.RaiseMouseMove(125, 70); // July 12

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 10)));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(new DateTime(2026, 7, 12)));
            Assert.That(selections, Is.Zero, "the gesture is still in flight");
        });

        canvas.RaiseMouseUp(125, 70);

        Assert.That(selections, Is.EqualTo(1));
    }

    [Test]
    public void Days_outside_the_min_max_range_are_disabled_and_unclickable()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.MinDate = new(2026, 7, 5);
        calendar.MaxDate = new(2026, 7, 25);
        var changes = 0;
        var selections = 0;
        calendar.DateChanged += (_, _) => ++changes;
        calendar.DateSelected += (_, _) => ++selections;

        Click(canvas, 65, 50); // July 2, below MinDate

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 15)), "the click bounced off");
            Assert.That(changes, Is.Zero);
            Assert.That(selections, Is.Zero);
        });

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("text \"2\" #FF9A9A9A MiddleCenter @60,44"), "July 2 painted disabled");
            Assert.That(g.Operations, Does.Contain("text \"26\" #FF9A9A9A MiddleCenter @120,110"), "July 26 painted disabled");
        });
    }

    [Test]
    public void Arrow_keys_move_the_focus_day_and_Enter_selects_it()
    {
        var calendar = CreateCalendar(out var canvas);
        var selections = 0;
        calendar.DateSelected += (_, _) => ++selections;

        canvas.RaiseKeyDown(Keys.Right); // July 16
        canvas.RaiseKeyDown(Keys.Down); // July 23
        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 15)), "moving the focus leaves the selection alone");

        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 23)));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(new DateTime(2026, 7, 23)));
            Assert.That(selections, Is.EqualTo(1));
        });
    }

    [Test]
    public void Page_keys_change_the_month_and_Ctrl_pages_change_the_year()
    {
        _ = CreateCalendar(out var canvas);

        canvas.RaiseKeyDown(Keys.PageDown);
        Assert.That(canvas.RaisePaint().DrewText("August 2026"), Is.True);

        canvas.RaiseKeyDown(Keys.PageDown, KeyModifiers.Control);
        Assert.That(canvas.RaisePaint().DrewText("August 2027"), Is.True);

        canvas.RaiseKeyDown(Keys.PageUp, KeyModifiers.Control);
        canvas.RaiseKeyDown(Keys.PageUp);
        Assert.That(canvas.RaisePaint().DrewText("July 2026"), Is.True);
    }

    [Test]
    public void Home_and_End_jump_to_the_month_edges()
    {
        var calendar = CreateCalendar(out var canvas);

        canvas.RaiseKeyDown(Keys.Home);
        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 1)));

        canvas.RaiseKeyDown(Keys.End);
        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 31)));
    }

    [Test]
    public void Keyboard_focus_clamps_to_the_min_max_range()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.MaxDate = new(2026, 7, 20);

        for (var i = 0; i < 6; ++i)
            canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 20)), "the focus stopped at MaxDate");

        canvas.RaiseKeyDown(Keys.PageDown);
        Assert.That(canvas.RaisePaint().DrewText("July 2026"), Is.True, "paging past MaxDate is refused");
    }

    [Test]
    public void Wheel_and_title_arrows_page_the_displayed_month()
    {
        _ = CreateCalendar(out var canvas);

        canvas.RaiseMouseWheel(-120);
        Assert.That(canvas.RaisePaint().DrewText("August 2026"), Is.True, "wheel down pages forward");

        canvas.RaiseMouseWheel(120);
        Assert.That(canvas.RaisePaint().DrewText("July 2026"), Is.True, "wheel up pages back");

        Click(canvas, 5, 5); // the previous-month arrow zone
        Assert.That(canvas.RaisePaint().DrewText("June 2026"), Is.True);

        Click(canvas, 130, 5); // the next-month arrow zone
        Assert.That(canvas.RaisePaint().DrewText("July 2026"), Is.True);
    }

    [Test]
    public void Setting_the_selection_follows_the_displayed_month()
    {
        var calendar = CreateCalendar(out var canvas);
        var changes = 0;
        calendar.DateChanged += (_, _) => ++changes;

        calendar.SelectionStart = new(2026, 9, 3);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 9, 3)));
            Assert.That(calendar.SelectionEnd, Is.EqualTo(new DateTime(2026, 9, 3)), "the stale end collapsed onto the new start");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(canvas.RaisePaint().DrewText("September 2026"), Is.True);
        });
    }
}
