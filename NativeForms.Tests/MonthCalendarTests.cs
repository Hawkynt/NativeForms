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

        Assert.That(g.Operations, Does.Contain("ellipse #FF0078D4 122,91,16,16"), "July 19 wears the accent circle");
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

    // --- Title drill-down --------------------------------------------------------------------------

    // A drilled-out page drops the day-of-week header and lays 12 period cells out 4x3 under the
    // title row: with the 140x176 fixture that is 35x51 cells starting at y=22. Cell i therefore
    // centres on ((i % 4) * 35 + 17, 22 + (i / 4) * 51 + 25).
    private const int _TitleX = 70;
    private const int _TitleY = 11;

    private static void ClickPeriod(HeadlessCanvasPeer canvas, int index)
        => Click(canvas, ((index % 4) * 35) + 17, 22 + ((index / 4) * 51) + 25);

    [Test]
    public void Clicking_the_title_drills_out_month_to_year_to_decade_to_century()
    {
        _ = CreateCalendar(out var canvas);

        Click(canvas, _TitleX, _TitleY);
        var year = canvas.RaisePaint();
        Click(canvas, _TitleX, _TitleY);
        var decade = canvas.RaisePaint();
        Click(canvas, _TitleX, _TitleY);
        var century = canvas.RaisePaint();
        Click(canvas, _TitleX, _TitleY); // the century page is the outermost: no further level
        var stillCentury = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(year.DrewText("2026"), Is.True, "the month page drills out to the year");
            Assert.That(year.DrewText("Jan"), Is.True, "the year page lists the months");
            Assert.That(year.DrewText("Dec"), Is.True);
            Assert.That(year.DrewText("Mo"), Is.False, "the day-of-week header belongs to the day page only");
            Assert.That(decade.DrewText("2020-2029"), Is.True);
            Assert.That(decade.DrewText("2019"), Is.True, "the decade page pads with the neighbouring years");
            Assert.That(decade.DrewText("2030"), Is.True);
            Assert.That(century.DrewText("2000-2099"), Is.True);
            Assert.That(century.DrewText("1990"), Is.True, "the century page lists decades");
            Assert.That(century.DrewText("2090"), Is.True);
            Assert.That(stillCentury.DrewText("2000-2099"), Is.True);
        });
    }

    [Test]
    public void Clicking_a_cell_drills_back_in_one_level_at_a_time()
    {
        _ = CreateCalendar(out var canvas);
        Click(canvas, _TitleX, _TitleY);
        Click(canvas, _TitleX, _TitleY);
        Click(canvas, _TitleX, _TitleY); // century: 1990, 2000, 2010, ...

        ClickPeriod(canvas, 3); // 1990, 2000, 2010, 2020 -> the 2020s
        var decade = canvas.RaisePaint();
        ClickPeriod(canvas, 3); // 2019, 2020, 2021, 2022 -> 2022
        var year = canvas.RaisePaint();
        ClickPeriod(canvas, 2); // March
        var month = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(decade.DrewText("2020-2029"), Is.True);
            Assert.That(year.DrewText("2022"), Is.True);
            Assert.That(month.DrewText("March 2022"), Is.True);
            Assert.That(month.DrewText("Mo"), Is.True, "back on the day page the header row returns");
        });
    }

    [Test]
    public void The_title_arrows_page_by_the_drilled_out_unit()
    {
        _ = CreateCalendar(out var canvas);
        Click(canvas, _TitleX, _TitleY);

        Click(canvas, 130, 5);
        var nextYear = canvas.RaisePaint();
        Click(canvas, _TitleX, _TitleY);
        Click(canvas, 5, 5);
        var previousDecade = canvas.RaisePaint();
        canvas.RaiseMouseWheel(-120);
        var wheeledDecade = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(nextYear.DrewText("2027"), Is.True, "on the year page the arrows step whole years");
            Assert.That(previousDecade.DrewText("2010-2019"), Is.True, "on the decade page they step whole decades");
            Assert.That(wheeledDecade.DrewText("2020-2029"), Is.True, "and so does the wheel");
        });
    }

    [Test]
    public void Ctrl_Up_and_Ctrl_Down_drill_the_page_from_the_keyboard()
    {
        _ = CreateCalendar(out var canvas);

        canvas.RaiseKeyDown(Keys.Up, KeyModifiers.Control);
        var year = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.Up, KeyModifiers.Control);
        var decade = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Control);
        var backToYear = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(year.DrewText("2026"), Is.True);
            Assert.That(decade.DrewText("2020-2029"), Is.True);
            Assert.That(backToYear.DrewText("2026"), Is.True, "Ctrl+Down drills back into the focused cell");
        });
    }

    [Test]
    public void The_keyboard_navigates_a_drilled_out_page_and_Enter_drills_in()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.SetSelectionRange(new(2026, 7, 15), new(2026, 7, 15));
        Click(canvas, _TitleX, _TitleY); // the year page, focus on July

        canvas.RaiseKeyDown(Keys.Right); // August
        canvas.RaiseKeyDown(Keys.Down); // December
        var focusMoved = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.Right); // past December: onto the next year
        var nextYear = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.Home); // January of the shown year
        canvas.RaiseKeyDown(Keys.Enter);
        var drilled = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(focusMoved.DrewText("2026"), Is.True, "moving inside the year keeps the page");
            Assert.That(nextYear.DrewText("2027"), Is.True, "walking off the grid pages to the next year");
            Assert.That(drilled.DrewText("January 2027"), Is.True);
        });
    }

    [Test]
    public void PageUp_and_PageDown_page_a_drilled_out_period()
    {
        _ = CreateCalendar(out var canvas);
        Click(canvas, _TitleX, _TitleY);
        Click(canvas, _TitleX, _TitleY); // the decade page

        canvas.RaiseKeyDown(Keys.PageDown);
        var forward = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.PageUp);
        canvas.RaiseKeyDown(Keys.PageUp);

        Assert.Multiple(() =>
        {
            Assert.That(forward.DrewText("2030-2039"), Is.True);
            Assert.That(canvas.RaisePaint().DrewText("2010-2019"), Is.True);
        });
    }

    [Test]
    public void The_min_max_window_greys_and_bounces_cells_at_every_level()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.MinDate = new(2026, 5, 1);
        calendar.MaxDate = new(2026, 9, 30);

        Click(canvas, _TitleX, _TitleY); // the year page of 2026
        var year = canvas.RaisePaint();
        ClickPeriod(canvas, 0); // January: entirely below MinDate, so the click must bounce
        var afterBounce = canvas.RaisePaint();
        ClickPeriod(canvas, 5); // June: inside the window
        var afterPick = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(year.Operations.Any(o => o.StartsWith("text \"Jan\" #FF9A9A9A", StringComparison.Ordinal)), Is.True, "a month wholly outside the window paints disabled");
            Assert.That(year.Operations.Any(o => o.StartsWith("text \"Jun\" #FF1A1A1A", StringComparison.Ordinal)), Is.True, "a month inside it paints enabled");
            Assert.That(afterBounce.DrewText("2026"), Is.True, "the out-of-range month did not drill in");
            Assert.That(afterPick.DrewText("June 2026"), Is.True);
        });
    }

    [Test]
    public void Drilling_out_at_the_supported_range_ends_paints_and_bounces_instead_of_throwing()
    {
        // The padding cells of a decade or century page run one period past the shown one, so at
        // 9998 (and at 1753) they name years outside the supported range. They must paint disabled
        // and reject every gesture rather than blowing up on a DateTime the calendar cannot hold.
        var calendar = CreateCalendar(out var canvas);
        calendar.MaxDate = new(9998, 12, 31);
        calendar.SetSelectionRange(new(9998, 12, 1), new(9998, 12, 1));

        Click(canvas, _TitleX, _TitleY); // 9998
        Click(canvas, _TitleX, _TitleY); // 9990-9999
        var decade = canvas.RaisePaint();
        ClickPeriod(canvas, 11); // the trailing padding cell: year 10000, outside the range
        var afterTrailing = canvas.RaisePaint();
        canvas.RaiseKeyDown(Keys.End);
        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.PageDown);
        var afterKeys = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(decade.DrewText("9990-9999"), Is.True);
            Assert.That(decade.Operations.Any(o => o.StartsWith("text \"10000\" #FF9A9A9A", StringComparison.Ordinal)), Is.True, "the out-of-range padding year paints disabled");
            Assert.That(afterTrailing.DrewText("9990-9999"), Is.True, "clicking it drilled nowhere");
            Assert.That(afterKeys.DrewText("9990-9999"), Is.True, "walking off the end of the range stays put");
        });
    }

    [Test]
    public void A_drilled_out_page_marks_today_and_the_selection()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.SetSelectionRange(new(2026, 3, 4), new(2026, 3, 4)); // today stays July 2026

        Click(canvas, _TitleX, _TitleY);
        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Any(o => o.StartsWith("fill #FF0078D4 70,22,35,51", StringComparison.Ordinal)), Is.True, "March, the selected month, carries the selection fill");
            Assert.That(g.Operations.Any(o => o.StartsWith("ellipse #FF0078D4 72,83", StringComparison.Ordinal)), Is.True, "July, today's month, wears the accent circle");
        });
    }

    [Test]
    public void A_century_page_does_not_mark_a_selection_that_sits_below_it()
    {
        // The century arm of the cell lookup divides by ten; a selection in the decade just below
        // the page must report "not here" rather than truncating onto the first cell.
        var calendar = CreateCalendar(out var canvas);
        calendar.SetSelectionRange(new(1985, 6, 1), new(1985, 6, 1));

        Click(canvas, _TitleX, _TitleY);
        Click(canvas, _TitleX, _TitleY);
        Click(canvas, _TitleX, _TitleY); // the 1900-1999 page, whose cells run 1890..2000
        var onPage = canvas.RaisePaint();
        Click(canvas, 130, 5); // page a century forward, leaving the 1985 selection behind
        var offPage = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(onPage.DrewText("1900-1999"), Is.True);
            Assert.That(onPage.Operations.Any(o => o.StartsWith("fill #FF0078D4 35,124,35,51", StringComparison.Ordinal)), Is.True, "the 1980s, holding the selection, carry the fill");
            Assert.That(offPage.DrewText("2000-2099"), Is.True);
            Assert.That(offPage.Operations.Any(o => o.StartsWith("fill #FF0078D4", StringComparison.Ordinal)), Is.False, "a selection below the page must highlight no cell at all");
        });
    }

    // --- Per-day delegates (background, selectable, tooltip) ------------------------------------

    [Test]
    public void DateSelectable_blocks_a_vetoed_day_from_being_picked()
    {
        var calendar = CreateCalendar(out var canvas); // selection starts on 2026-07-15
        calendar.DateSelectable = d => d != new DateTime(2026, 7, 16);

        Click(canvas, 70, 99); // 2026-07-16 (row 2, col 3) — vetoed
        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 15)), "a vetoed day cannot be selected");

        Click(canvas, 90, 99); // 2026-07-17 (row 2, col 4) — allowed
        Assert.That(calendar.SelectionStart, Is.EqualTo(new DateTime(2026, 7, 17)), "an allowed day still selects");
    }

    [Test]
    public void DayBackgroundProvider_fills_a_days_cell()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.DayBackgroundProvider = d => d == new DateTime(2026, 7, 4) ? System.Drawing.Color.FromArgb(0xFF, 0xFF, 0, 0) : null;

        var g = canvas.RaisePaint();

        // 2026-07-04 is row 0, col 5 → cell 100,44,20,22.
        Assert.That(g.Operations.Any(o => o.StartsWith("fill #FFFF0000 100,44,20,22", StringComparison.Ordinal)), Is.True, "the holiday cell is shaded");
    }

    [Test]
    public void DayTooltipProvider_reports_the_hovered_days_text()
    {
        var calendar = CreateCalendar(out var canvas);
        calendar.DayTooltipProvider = d => d == new DateTime(2026, 7, 4) ? "Independence Day" : null;

        canvas.RaiseMouseMove(110, 55); // over 2026-07-04 (row 0, col 5)
        Assert.That(calendar.HoveredDayTooltipText, Is.EqualTo("Independence Day"));

        canvas.RaiseMouseMove(50, 55); // over 2026-07-01 — no tooltip
        Assert.That(calendar.HoveredDayTooltipText, Is.Null);
    }
}
