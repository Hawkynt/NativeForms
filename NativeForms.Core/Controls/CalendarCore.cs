using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>What a point on a calendar page hits.</summary>
internal enum CalendarHit
{
    /// <summary>Nothing interactive.</summary>
    None,

    /// <summary>The previous-period arrow zone in the title row.</summary>
    PreviousMonth,

    /// <summary>The next-period arrow zone in the title row.</summary>
    NextMonth,

    /// <summary>A day cell on the month page.</summary>
    Day,

    /// <summary>The title text between the arrows, which drills one level out.</summary>
    Title,

    /// <summary>A period cell (month, year or decade) on a drilled-out page, which drills back in.</summary>
    Period,
}

/// <summary>
/// How far the calendar is drilled out, in the order the title cycles through: the day page, the
/// twelve months of a year, the ten years of a decade (plus a padding year either side), the ten
/// decades of a century. Clicking the title zooms out one step, clicking a cell zooms back in.
/// </summary>
internal enum CalendarLevel
{
    /// <summary>The 6×7 day grid of one month.</summary>
    Month,

    /// <summary>The twelve months of one year.</summary>
    Year,

    /// <summary>The ten years of one decade, padded to twelve cells.</summary>
    Decade,

    /// <summary>The ten decades of one century, padded to twelve cells.</summary>
    Century,
}

/// <summary>
/// The month-page engine <see cref="MonthCalendar"/> and the drop-down of <see cref="DateTimePicker"/>
/// share: grid geometry, painting, hit-testing, range selection and keyboard/wheel navigation live
/// here once, so the standalone control and the popup stay pixel- and behavior-identical. The engine
/// is surface-agnostic — the host passes its theme and size into every call and receives repaint and
/// selection notifications through the callback slots, which stay <see langword="null"/> until
/// assigned, like unsubscribed events.
/// </summary>
/// <remarks>
/// The title is a button, not a caption: clicking it drills one <see cref="CalendarLevel"/> out —
/// day page → months of the year → years of the decade → decades of the century — and clicking a
/// cell drills back in, which is how the classic control lets a user jump years without paging
/// month by month. Every level shares the title row and its paging arrows (which then page by year,
/// decade or century), honours the <see cref="MinDate"/>/<see cref="MaxDate"/> window, and is
/// keyboard-drivable; only the day page selects, the drilled-out levels merely navigate.
/// </remarks>
internal sealed class CalendarCore
{
    /// <summary>Columns in a drilled-out period grid.</summary>
    private const int _PeriodColumns = 4;

    /// <summary>Rows in a drilled-out period grid.</summary>
    private const int _PeriodRows = 3;

    /// <summary>Cells in a drilled-out period grid.</summary>
    private const int _PeriodCells = _PeriodColumns * _PeriodRows;

    /// <summary>The earliest supported date, matching the classic toolkit's calendar floor.</summary>
    internal static readonly DateTime MinimumDate = new(1753, 1, 1);

    /// <summary>The latest supported date, matching the classic toolkit's calendar ceiling.</summary>
    internal static readonly DateTime MaximumDate = new(9998, 12, 31);


    /// <summary>The day-number strings "1"–"31", materialized once so painting stays allocation-free.</summary>
    private static string[]? _dayNumbers;

    private DateTime _displayMonth;
    private string? _title;
    private bool _dragging;
    private CalendarLevel _level;

    /// <summary>The twelve cell captions of the drilled-out year/decade pages, built on the first
    /// drill-out and refreshed whenever the shown period moves — never on a steady-state frame.
    /// Stays <see langword="null"/> for a calendar the user never drills out of, so the day page
    /// costs nothing for a feature it does not use.</summary>
    private string[]? _periodLabels;

    private bool _periodLabelsDirty = true;

    /// <summary>Creates a page showing the current month with today selected and focused.</summary>
    public CalendarCore()
    {
        var today = DateTime.Today;
        this.TodayDate = today;
        this.SelectionStart = today;
        this.SelectionEnd = today;
        this.FocusDate = today;
        this.AnchorDate = today;
        _displayMonth = new(today.Year, today.Month, 1);
    }

    /// <summary>The first day of the displayed month. Setting it drops the cached title and cell
    /// captions.</summary>
    public DateTime DisplayMonth
    {
        get => _displayMonth;
        set
        {
            _displayMonth = value;
            _title = null;
            _periodLabelsDirty = true;
        }
    }

    /// <summary>How far the page is drilled out. Setting it drops the cached title and cell
    /// captions; the day page is <see cref="CalendarLevel.Month"/>.</summary>
    public CalendarLevel Level
    {
        get => _level;
        set
        {
            if (_level == value)
                return;

            _level = value;
            _title = null;
            _periodLabelsDirty = true;
        }
    }

    /// <summary>The day keyboard navigation operates on.</summary>
    public DateTime FocusDate { get; set; }

    /// <summary>The first selected day.</summary>
    public DateTime SelectionStart { get; set; }

    /// <summary>The last selected day.</summary>
    public DateTime SelectionEnd { get; set; }

    /// <summary>The day a range gesture grows from.</summary>
    public DateTime AnchorDate { get; set; }

    /// <summary>The earliest selectable day; earlier cells paint disabled and reject clicks.</summary>
    public DateTime MinDate { get; set; } = MinimumDate;

    /// <summary>The latest selectable day; later cells paint disabled and reject clicks.</summary>
    public DateTime MaxDate { get; set; } = MaximumDate;

    /// <summary>An optional per-day background colour — the hook for shading holidays, deadlines and the
    /// like. Returns a <see cref="Color"/> to fill that day's cell (in-month days only), or
    /// <see langword="null"/> to leave it plain. The current selection still paints over it.</summary>
    public Func<DateTime, Color?>? DayBackgroundProvider { get; set; }

    /// <summary>An optional predicate that blocks individual days from being picked — a blackout of
    /// weekends, booked slots or any custom rule on top of <see cref="MinDate"/>/<see cref="MaxDate"/>.
    /// A day it rejects paints disabled and refuses clicks.</summary>
    public Func<DateTime, bool>? DateSelectable { get; set; }

    /// <summary>An optional per-day tooltip text, shown by the hosting control on hover.</summary>
    public Func<DateTime, string?>? DayTooltipProvider { get; set; }

    /// <summary>Whether a day may be selected: inside the window and not vetoed by <see cref="DateSelectable"/>.</summary>
    private bool IsDaySelectable(DateTime date)
        => date >= this.MinDate.Date && date <= this.MaxDate.Date && (this.DateSelectable is null || this.DateSelectable(date));

    /// <summary>The day under a point on the month page, or <see langword="null"/> elsewhere — the hook
    /// a host uses to drive per-day tooltips.</summary>
    public DateTime? DayAt(ITheme theme, Size size, int x, int y)
        => this.HitTest(theme, size, x, y, out var day, out _) == CalendarHit.Day ? day : null;

    /// <summary>The day of week in the leftmost grid column.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>The largest number of days a selection gesture may span.</summary>
    public int MaxSelectionCount { get; set; } = 7;

    /// <summary>The day painted with the accent circle.</summary>
    public DateTime TodayDate { get; set; }

    /// <summary>Invoked whenever the page needs repainting.</summary>
    public Action? Invalidated { get; set; }

    /// <summary>Invoked once per change of the selected range.</summary>
    public Action? SelectionChanged { get; set; }

    /// <summary>Invoked when the user commits a selection: the click gesture ends or Enter/Space lands.</summary>
    public Action? DateSelected { get; set; }

    /// <summary>The date of the top-left grid cell: the <paramref name="firstDayOfWeek"/> on or
    /// before the first day of <paramref name="month"/>.</summary>
    public static DateTime FirstGridDate(DateTime month, DayOfWeek firstDayOfWeek)
        => month.AddDays(-(((int)month.DayOfWeek - (int)firstDayOfWeek + 7) % 7));

    /// <summary>Clamps a date to a whole day inside [<see cref="MinDate"/>, <see cref="MaxDate"/>].</summary>
    public DateTime ClampDay(DateTime day)
    {
        day = day.Date;
        var min = this.MinDate.Date;
        var max = this.MaxDate.Date;
        return day < min ? min : day > max ? max : day;
    }

    /// <summary>Sets the selected range (each end clamped to the selectable window), repainting and
    /// reporting through <see cref="SelectionChanged"/> when it actually changes.</summary>
    public void SetSelection(DateTime start, DateTime end)
    {
        start = this.ClampDay(start);
        end = this.ClampDay(end);
        if (start == this.SelectionStart && end == this.SelectionEnd)
            return;

        this.SelectionStart = start;
        this.SelectionEnd = end;
        this.Invalidated?.Invoke();
        this.SelectionChanged?.Invoke();
    }

    /// <summary>Makes the month containing <paramref name="date"/> the displayed page and repaints.</summary>
    public void ShowMonthOf(DateTime date)
    {
        var month = new DateTime(date.Year, date.Month, 1);
        if (month != _displayMonth)
            this.DisplayMonth = month;

        this.Invalidated?.Invoke();
    }

    /// <summary>Pages the display by whole months, dragging the focus day along; a page that would
    /// leave the [<see cref="MinDate"/>, <see cref="MaxDate"/>] window entirely is refused.</summary>
    public void NavigateMonths(int months)
    {
        var target = _displayMonth.AddMonths(months);
        var lastDay = DateTime.DaysInMonth(target.Year, target.Month);
        if (target > this.MaxDate.Date || target.AddDays(lastDay - 1) < this.MinDate.Date)
            return;

        this.DisplayMonth = target;
        this.FocusDate = this.ClampDay(new(target.Year, target.Month, Math.Min(this.FocusDate.Day, lastDay)));
        this.Invalidated?.Invoke();
    }

    // --- Drill-down levels -------------------------------------------------------------------------

    /// <summary>The first year of the decade the displayed page sits in.</summary>
    private int DecadeStart => _displayMonth.Year - (((_displayMonth.Year % 10) + 10) % 10);

    /// <summary>The first year of the century the displayed page sits in.</summary>
    private int CenturyStart => _displayMonth.Year - (((_displayMonth.Year % 100) + 100) % 100);

    /// <summary>
    /// Drills one level out — day page → months → years → decades — repainting when it moved. The
    /// century page is the outermost, so drilling out of it is a no-op.
    /// </summary>
    public void ZoomOut()
    {
        if (_level >= CalendarLevel.Century)
            return;

        this.Level = _level + 1;
        this.Invalidated?.Invoke();
    }

    /// <summary>
    /// Drills back into the cell at <paramref name="index"/> (0–11 in reading order) of the current
    /// drilled-out page: a month cell opens that month's day page, a year cell that year's month
    /// page, a decade cell that decade's year page. The focus follows the cell, so the keyboard
    /// lands where the mouse did. Refused on the day page and on a cell whose whole period lies
    /// outside [<see cref="MinDate"/>, <see cref="MaxDate"/>].
    /// </summary>
    public void ZoomInto(int index)
    {
        if (_level == CalendarLevel.Month || index < 0 || index >= _PeriodCells)
            return;

        if (!this.IsPeriodSelectable(index))
            return;

        var level = _level;
        var startYear = this.PeriodStartYear(index);
        var month = level == CalendarLevel.Year ? index + 1 : _displayMonth.Month;
        this.Level = level - 1;
        this.DisplayMonth = new(startYear, month, 1);
        this.FocusDate = this.ClampDay(new(startYear, month, Math.Min(this.FocusDate.Day, DateTime.DaysInMonth(startYear, month))));
        this.Invalidated?.Invoke();
    }

    /// <summary>Pages the displayed period by whole units of the current level: months on the day
    /// page, then years, decades and centuries. A page that would leave the selectable window
    /// entirely is refused.</summary>
    public void NavigatePeriods(int steps)
    {
        if (_level == CalendarLevel.Month)
        {
            this.NavigateMonths(steps);
            return;
        }

        var years = _level switch
        {
            CalendarLevel.Year => steps,
            CalendarLevel.Decade => steps * 10,
            _ => steps * 100,
        };

        var year = _displayMonth.Year + years;
        if (year < CalendarCore.MinimumDate.Year || year > CalendarCore.MaximumDate.Year)
            return;

        var target = new DateTime(year, _displayMonth.Month, 1);
        if (target > this.MaxDate.Date || target.AddMonths(1).AddDays(-1) < this.MinDate.Date)
        {
            // The month itself may fall outside while the page as a whole still overlaps the window
            // (a year page whose January is below MinDate but whose December is not), so only refuse
            // when the entire drilled-out page lies outside.
            if (this.PeriodPageEnd(year) < this.MinDate.Date || this.PeriodPageStart(year) > this.MaxDate.Date)
                return;
        }

        this.DisplayMonth = target;
        this.FocusDate = this.ClampDay(new(year, this.FocusDate.Month, Math.Min(this.FocusDate.Day, DateTime.DaysInMonth(year, this.FocusDate.Month))));
        this.Invalidated?.Invoke();
    }

    /// <summary>The first day the drilled-out page centred on <paramref name="year"/> covers.</summary>
    private DateTime PeriodPageStart(int year) => _level switch
    {
        CalendarLevel.Year => new(year, 1, 1),
        CalendarLevel.Decade => new(Math.Max(CalendarCore.MinimumDate.Year, year - (((year % 10) + 10) % 10) - 1), 1, 1),
        _ => new(Math.Max(CalendarCore.MinimumDate.Year, year - (((year % 100) + 100) % 100) - 10), 1, 1),
    };

    /// <summary>The last day the drilled-out page centred on <paramref name="year"/> covers.</summary>
    private DateTime PeriodPageEnd(int year) => _level switch
    {
        CalendarLevel.Year => new(year, 12, 31),
        CalendarLevel.Decade => new(Math.Min(CalendarCore.MaximumDate.Year, year - (((year % 10) + 10) % 10) + 10), 12, 31),
        _ => new(Math.Min(CalendarCore.MaximumDate.Year, year - (((year % 100) + 100) % 100) + 109), 12, 31),
    };

    /// <summary>
    /// The first year the cell at <paramref name="index"/> of the current page covers. Deliberately
    /// integer arithmetic: the padding cells of a decade or century page can run past the supported
    /// year range at the extremes, and a <see cref="DateTime"/> built from such a year would throw
    /// before <see cref="IsPeriodInRange"/> ever got to reject it.
    /// </summary>
    private int PeriodStartYear(int index) => _level switch
    {
        CalendarLevel.Year => _displayMonth.Year,
        CalendarLevel.Decade => this.DecadeStart - 1 + index,
        _ => this.CenturyStart - 10 + (index * 10),
    };

    /// <summary>How many years one cell of the current page spans (0 for a month cell).</summary>
    private int PeriodSpanYears => _level switch
    {
        CalendarLevel.Year => 0,
        CalendarLevel.Decade => 1,
        _ => 10,
    };

    /// <summary>Whether the cell at <paramref name="index"/> stays inside the supported year range
    /// — false for the padding cells beyond <see cref="MinimumDate"/>/<see cref="MaximumDate"/>.</summary>
    private bool IsPeriodInRange(int index)
    {
        if (_level == CalendarLevel.Year)
            return true;

        var startYear = this.PeriodStartYear(index);
        return startYear >= CalendarCore.MinimumDate.Year
            && startYear + this.PeriodSpanYears - 1 <= CalendarCore.MaximumDate.Year;
    }

    /// <summary>Whether the cell at <paramref name="index"/> overlaps the selectable window at all;
    /// cells that do not paint disabled and reject clicks, at every level.</summary>
    private bool IsPeriodSelectable(int index)
    {
        if (!this.IsPeriodInRange(index))
            return false;

        var startYear = this.PeriodStartYear(index);
        var start = _level == CalendarLevel.Year ? new DateTime(startYear, index + 1, 1) : new DateTime(startYear, 1, 1);
        var end = _level switch
        {
            CalendarLevel.Year => new DateTime(startYear, index + 1, DateTime.DaysInMonth(startYear, index + 1)),
            _ => new DateTime(startYear + this.PeriodSpanYears - 1, 12, 31),
        };

        return end >= this.MinDate.Date && start <= this.MaxDate.Date;
    }

    /// <summary>The cell <paramref name="date"/> falls into on the current page, or -1 when it lies
    /// outside the page entirely.</summary>
    private int PeriodIndexOf(DateTime date)
    {
        // The century arm divides, so its offset must be tested for negativity before the division:
        // integer division truncates toward zero, which would fold a year just below the page onto
        // cell 0 instead of reporting "not on this page".
        var centuryOffset = date.Year - (this.CenturyStart - 10);
        var index = _level switch
        {
            CalendarLevel.Year => date.Year == _displayMonth.Year ? date.Month - 1 : -1,
            CalendarLevel.Decade => date.Year - (this.DecadeStart - 1),
            _ => centuryOffset < 0 ? -1 : centuryOffset / 10,
        };

        return index >= 0 && index < _PeriodCells ? index : -1;
    }

    /// <summary>Rebuilds the twelve cell captions when the page moved; a no-op on a steady-state
    /// frame, which is what keeps drilled-out painting allocation-free.</summary>
    private string[] PeriodLabels()
    {
        if (_level == CalendarLevel.Year)
            return Strings.AbbreviatedMonthNames;

        var labels = _periodLabels ??= new string[_PeriodCells];
        if (!_periodLabelsDirty)
            return labels;

        _periodLabelsDirty = false;
        for (var i = 0; i < _PeriodCells; ++i)
            labels[i] = this.PeriodStartYear(i).ToString(CultureInfo.InvariantCulture);

        return labels;
    }

    /// <summary>The title of the current page: "July 2026", "2026", "2020-2029" or "2000-2099".</summary>
    private string PeriodTitle() => _level switch
    {
        CalendarLevel.Month => _displayMonth.ToString("MMMM yyyy", Strings.DateTimeFormat),
        CalendarLevel.Year => _displayMonth.Year.ToString(CultureInfo.InvariantCulture),
        CalendarLevel.Decade => $"{this.DecadeStart}-{this.DecadeStart + 9}",
        _ => $"{this.CenturyStart}-{this.CenturyStart + 99}",
    };

    // --- Painting ----------------------------------------------------------------------------------

    /// <summary>Paints the whole page: title row with paging arrows, day-of-week header, 6×7 day grid
    /// (leading/trailing and out-of-range days greyed, selection highlighted, today circled in the
    /// accent color, the focus day outlined while <paramref name="showFocus"/>), and the border.</summary>
    public void Paint(IGraphics g, ITheme theme, Size size, bool showFocus)
    {
        g.FillRectangle(theme.FieldBackground, new(0, 0, size.Width, size.Height));

        var rowHeight = theme.RowHeight;
        var title = _title ??= this.PeriodTitle();
        g.DrawText(title, theme.DefaultFont, theme.ControlText, new(0, 0, size.Width, rowHeight), ContentAlignment.MiddleCenter);
        DrawArrow(g, theme, new(0, 0, rowHeight, rowHeight), true);
        DrawArrow(g, theme, new(size.Width - rowHeight, 0, rowHeight, rowHeight), false);

        if (_level != CalendarLevel.Month)
        {
            this.PaintPeriods(g, theme, size, showFocus);
            g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
            return;
        }

        var cellWidth = size.Width / 7;
        var dayNames = Strings.AbbreviatedDayNames;
        for (var i = 0; i < 7; ++i)
            g.DrawText(dayNames[((int)this.FirstDayOfWeek + i) % 7], theme.DefaultFont, theme.HeaderText, new(i * cellWidth, rowHeight, cellWidth, rowHeight), ContentAlignment.MiddleCenter);

        var top = 2 * rowHeight;
        var cellHeight = (size.Height - top) / 6;
        var numbers = _dayNumbers ??= CreateDayNumbers();
        var date = FirstGridDate(_displayMonth, this.FirstDayOfWeek);
        var selectionStart = this.SelectionStart.Date;
        var selectionEnd = this.SelectionEnd.Date;
        var today = this.TodayDate.Date;
        var focus = this.FocusDate.Date;
        for (var row = 0; row < 6; ++row)
            for (var col = 0; col < 7; ++col, date = date.AddDays(1))
            {
                var cell = new Rectangle(col * cellWidth, top + row * cellHeight, cellWidth, cellHeight);
                var inMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
                var selected = date >= selectionStart && date <= selectionEnd;
                if (selected)
                    GlyphRenderer.FillSelection(g, theme, cell);
                else if (inMonth && this.DayBackgroundProvider is { } background && background(date) is { } fill)
                    g.FillRectangle(fill, cell);

                if (date == today)
                    g.DrawEllipse(theme.Accent, new(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2));

                var color = selected ? theme.SelectionText
                    : !inMonth || !this.IsDaySelectable(date) ? theme.DisabledText
                    : theme.ControlText;
                g.DrawText(numbers[date.Day - 1], theme.DefaultFont, color, cell, ContentAlignment.MiddleCenter);

                if (showFocus && date == focus)
                    g.DrawRectangle(theme.Accent, new(cell.X, cell.Y, cell.Width - 1, cell.Height - 1));
            }

        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
    }

    /// <summary>Paints a drilled-out page: a 4×3 grid of period cells under the title row, with the
    /// cells outside the shown period or outside the selectable window greyed, the period holding
    /// the selection highlighted, the one holding today circled in the accent color, and the focused
    /// cell outlined while <paramref name="showFocus"/>.</summary>
    private void PaintPeriods(IGraphics g, ITheme theme, Size size, bool showFocus)
    {
        var top = theme.RowHeight;
        var cellWidth = size.Width / _PeriodColumns;
        var cellHeight = (size.Height - top) / _PeriodRows;
        if (cellWidth < 1 || cellHeight < 1)
            return;

        var labels = this.PeriodLabels();
        var selected = this.PeriodIndexOf(this.SelectionStart.Date);
        var today = this.PeriodIndexOf(this.TodayDate.Date);
        var focus = this.PeriodIndexOf(this.FocusDate.Date);
        var insidePage = _level == CalendarLevel.Year ? _PeriodCells : 10; // decades/centuries pad by one cell either side
        for (var index = 0; index < _PeriodCells; ++index)
        {
            var cell = new Rectangle((index % _PeriodColumns) * cellWidth, top + ((index / _PeriodColumns) * cellHeight), cellWidth, cellHeight);
            var isSelected = index == selected;
            if (isSelected)
                GlyphRenderer.FillSelection(g, theme, cell);

            if (index == today)
                g.DrawEllipse(theme.Accent, new(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2));

            var outside = insidePage == 10 && (index == 0 || index == _PeriodCells - 1);
            var color = isSelected ? theme.SelectionText
                : outside || !this.IsPeriodSelectable(index) ? theme.DisabledText
                : theme.ControlText;
            g.DrawText(labels[index], theme.DefaultFont, color, cell, ContentAlignment.MiddleCenter);

            if (showFocus && index == focus)
                g.DrawRectangle(theme.Accent, new(cell.X, cell.Y, cell.Width - 1, cell.Height - 1));
        }
    }

    /// <summary>Draws a paging chevron centered in its title-row zone.</summary>
    private static void DrawArrow(IGraphics g, ITheme theme, Rectangle zone, bool pointsLeft)
    {
        var centerX = zone.X + (zone.Width / 2);
        var centerY = zone.Y + (zone.Height / 2);
        var dx = pointsLeft ? 3 : -3;
        g.DrawLine(theme.ControlText, centerX + dx, centerY - 4, centerX - dx, centerY);
        g.DrawLine(theme.ControlText, centerX - dx, centerY, centerX + dx, centerY + 4);
    }

    private static string[] CreateDayNumbers()
    {
        var numbers = new string[31];
        for (var i = 0; i < numbers.Length; ++i)
            numbers[i] = (i + 1).ToString(CultureInfo.InvariantCulture);

        return numbers;
    }

    // --- Hit-testing & input -----------------------------------------------------------------------

    /// <summary>Classifies a point on a page of the given size. On the day page the day it falls on
    /// comes back through <paramref name="day"/>; on a drilled-out page the hit cell's index comes
    /// back through <paramref name="periodIndex"/>.</summary>
    public CalendarHit HitTest(ITheme theme, Size size, int x, int y, out DateTime day, out int periodIndex)
    {
        day = default;
        periodIndex = -1;
        if (x < 0 || x >= size.Width || y < 0 || y >= size.Height)
            return CalendarHit.None;

        var rowHeight = theme.RowHeight;
        if (y < rowHeight)
            return x < rowHeight ? CalendarHit.PreviousMonth
                : x >= size.Width - rowHeight ? CalendarHit.NextMonth
                : CalendarHit.Title;

        if (_level != CalendarLevel.Month)
        {
            var periodWidth = size.Width / _PeriodColumns;
            var periodHeight = (size.Height - rowHeight) / _PeriodRows;
            if (periodWidth < 1 || periodHeight < 1)
                return CalendarHit.None;

            var periodColumn = x / periodWidth;
            var periodRow = (y - rowHeight) / periodHeight;
            if (periodColumn >= _PeriodColumns || periodRow >= _PeriodRows)
                return CalendarHit.None;

            periodIndex = (periodRow * _PeriodColumns) + periodColumn;
            return CalendarHit.Period;
        }

        var top = 2 * rowHeight;
        if (y < top)
            return CalendarHit.None;

        var cellWidth = size.Width / 7;
        var cellHeight = (size.Height - top) / 6;
        if (cellWidth < 1 || cellHeight < 1)
            return CalendarHit.None;

        var col = x / cellWidth;
        var row = (y - top) / cellHeight;
        if (col >= 7 || row >= 6)
            return CalendarHit.None;

        day = FirstGridDate(_displayMonth, this.FirstDayOfWeek).AddDays((row * 7) + col);
        return CalendarHit.Day;
    }

    /// <summary>Reacts to a press: pages on the title arrows, drills out on the title text, drills
    /// back in on a period cell, or starts a selection gesture on an in-range day — plain press
    /// selects it, Shift extends from the anchor.</summary>
    public void HandleMouseDown(ITheme theme, Size size, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        switch (this.HitTest(theme, size, e.X, e.Y, out var day, out var periodIndex))
        {
            case CalendarHit.PreviousMonth:
                this.NavigatePeriods(-1);
                break;

            case CalendarHit.NextMonth:
                this.NavigatePeriods(+1);
                break;

            case CalendarHit.Title:
                this.ZoomOut();
                break;

            case CalendarHit.Period:
                this.ZoomInto(periodIndex);
                break;

            case CalendarHit.Day when this.IsDaySelectable(day):
                this.FocusDate = day;
                _dragging = true;
                if (e.Shift)
                    this.ExtendTo(day);
                else
                {
                    this.AnchorDate = day;
                    this.SetSelection(day, day);
                }

                this.ShowMonthOf(day);
                break;
        }
    }

    /// <summary>Extends the in-flight drag gesture over the day under the pointer.</summary>
    public void HandleMouseMove(ITheme theme, Size size, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        if (this.HitTest(theme, size, e.X, e.Y, out var day, out _) != CalendarHit.Day)
            return;

        if (!this.IsDaySelectable(day))
            return;

        this.FocusDate = day;
        this.ExtendTo(day);
    }

    /// <summary>Ends the selection gesture, committing it through <see cref="DateSelected"/>.</summary>
    public void HandleMouseUp(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        this.DateSelected?.Invoke();
    }

    /// <summary>Pages the displayed period by wheel turns: down pages forward, up pages back. The
    /// unit follows the drill level — months, years, decades, centuries.</summary>
    public void HandleMouseWheel(int delta)
    {
        if (delta == 0)
            return;

        this.NavigatePeriods(delta > 0 ? -1 : +1);
    }

    /// <summary>Keyboard navigation: arrows move the focus day, PageUp/PageDown page months (with
    /// Ctrl whole years), Home/End jump to the month edges, Enter/Space select the focus day, and
    /// Ctrl+Up drills the title out one level (Ctrl+Down drills back in).</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode is Keys.Up or Keys.Down)
        {
            if (e.KeyCode == Keys.Up)
                this.ZoomOut();
            else
                this.ZoomInto(this.PeriodIndexOf(this.FocusDate.Date));

            e.Handled = true;
            return;
        }

        if (_level != CalendarLevel.Month)
        {
            this.HandlePeriodKeyDown(e);
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left:
                this.MoveFocus(this.FocusDate.AddDays(-1));
                e.Handled = true;
                break;

            case Keys.Right:
                this.MoveFocus(this.FocusDate.AddDays(+1));
                e.Handled = true;
                break;

            case Keys.Up:
                this.MoveFocus(this.FocusDate.AddDays(-7));
                e.Handled = true;
                break;

            case Keys.Down:
                this.MoveFocus(this.FocusDate.AddDays(+7));
                e.Handled = true;
                break;

            case Keys.PageUp:
                this.NavigateMonths(e.Control ? -12 : -1);
                e.Handled = true;
                break;

            case Keys.PageDown:
                this.NavigateMonths(e.Control ? +12 : +1);
                e.Handled = true;
                break;

            case Keys.Home:
                this.MoveFocus(new(this.FocusDate.Year, this.FocusDate.Month, 1));
                e.Handled = true;
                break;

            case Keys.End:
                this.MoveFocus(new(this.FocusDate.Year, this.FocusDate.Month, DateTime.DaysInMonth(this.FocusDate.Year, this.FocusDate.Month)));
                e.Handled = true;
                break;

            case Keys.Enter or Keys.Space:
                this.AnchorDate = this.FocusDate;
                this.SetSelection(this.FocusDate, this.FocusDate);
                this.DateSelected?.Invoke();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Keyboard navigation on a drilled-out page: Left/Right step one cell, Up/Down one row of four,
    /// Home/End jump to the first and last cell of the shown period, PageUp/PageDown page the whole
    /// period, and Enter/Space drill back into the focused cell.
    /// </summary>
    private void HandlePeriodKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
                this.MovePeriodFocus(-1);
                e.Handled = true;
                break;

            case Keys.Right:
                this.MovePeriodFocus(+1);
                e.Handled = true;
                break;

            case Keys.Up:
                this.MovePeriodFocus(-_PeriodColumns);
                e.Handled = true;
                break;

            case Keys.Down:
                this.MovePeriodFocus(+_PeriodColumns);
                e.Handled = true;
                break;

            case Keys.PageUp:
                this.NavigatePeriods(-1);
                e.Handled = true;
                break;

            case Keys.PageDown:
                this.NavigatePeriods(+1);
                e.Handled = true;
                break;

            case Keys.Home:
                this.FocusPeriod(_level == CalendarLevel.Year ? 0 : 1);
                e.Handled = true;
                break;

            case Keys.End:
                this.FocusPeriod(_level == CalendarLevel.Year ? _PeriodCells - 1 : 10);
                e.Handled = true;
                break;

            case Keys.Enter or Keys.Space:
                this.ZoomInto(this.PeriodIndexOf(this.FocusDate.Date));
                e.Handled = true;
                break;
        }
    }

    /// <summary>Moves the focus by whole cells on a drilled-out page, following it onto the
    /// neighbouring period when it walks off the grid.</summary>
    private void MovePeriodFocus(int cells)
    {
        var focus = this.FocusDate;
        DateTime target;
        if (_level == CalendarLevel.Year)
            target = focus.AddMonths(cells); // ±8 months can never leave the DateTime range
        else
        {
            // Years must be stepped by hand and clamped before the DateTime is built: ±40 years off
            // a focus already at the ceiling would overflow the calendar's own year range.
            var year = focus.Year + (cells * this.PeriodSpanYears);
            year = Math.Clamp(year, CalendarCore.MinimumDate.Year, CalendarCore.MaximumDate.Year);
            target = new(year, focus.Month, Math.Min(focus.Day, DateTime.DaysInMonth(year, focus.Month)));
        }

        this.FocusDate = this.ClampDay(target);
        this.ShowPeriodOf(this.FocusDate);
        this.Invalidated?.Invoke();
    }

    /// <summary>Parks the focus on the cell at <paramref name="index"/> of the shown period.</summary>
    private void FocusPeriod(int index)
    {
        if (!this.IsPeriodInRange(index))
            return;

        var year = this.PeriodStartYear(index);
        var month = _level == CalendarLevel.Year ? index + 1 : this.FocusDate.Month;
        this.FocusDate = this.ClampDay(new(year, month, Math.Min(this.FocusDate.Day, DateTime.DaysInMonth(year, month))));
        this.Invalidated?.Invoke();
    }

    /// <summary>Pages the drilled-out display so <paramref name="date"/> is on it.</summary>
    private void ShowPeriodOf(DateTime date)
    {
        if (this.PeriodIndexOf(date) >= 0)
            return;

        this.DisplayMonth = new(date.Year, _level == CalendarLevel.Year ? date.Month : _displayMonth.Month, 1);
    }

    /// <summary>Grows the selection from the anchor toward <paramref name="day"/>, capped at
    /// <see cref="MaxSelectionCount"/> days.</summary>
    private void ExtendTo(DateTime day)
    {
        var anchor = this.AnchorDate.Date;
        var cap = this.MaxSelectionCount - 1;
        if (day >= anchor)
            this.SetSelection(anchor, (day - anchor).Days > cap ? anchor.AddDays(cap) : day);
        else
            this.SetSelection((anchor - day).Days > cap ? anchor.AddDays(-cap) : day, anchor);
    }

    /// <summary>Moves the focus day (clamped to the selectable window), following it across months.</summary>
    private void MoveFocus(DateTime target)
    {
        target = this.ClampDay(target);
        this.FocusDate = target;
        this.ShowMonthOf(target);
    }
}
