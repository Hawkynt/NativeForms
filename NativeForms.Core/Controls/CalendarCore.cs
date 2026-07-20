using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>What a point on a calendar month page hits.</summary>
internal enum CalendarHit
{
    /// <summary>Nothing interactive.</summary>
    None,

    /// <summary>The previous-month arrow zone in the title row.</summary>
    PreviousMonth,

    /// <summary>The next-month arrow zone in the title row.</summary>
    NextMonth,

    /// <summary>A day cell.</summary>
    Day,
}

/// <summary>
/// The month-page engine <see cref="MonthCalendar"/> and the drop-down of <see cref="DateTimePicker"/>
/// share: grid geometry, painting, hit-testing, range selection and keyboard/wheel navigation live
/// here once, so the standalone control and the popup stay pixel- and behavior-identical. The engine
/// is surface-agnostic — the host passes its theme and size into every call and receives repaint and
/// selection notifications through the callback slots, which stay <see langword="null"/> until
/// assigned, like unsubscribed events.
/// </summary>
internal sealed class CalendarCore
{
    /// <summary>The earliest supported date, matching the classic toolkit's calendar floor.</summary>
    internal static readonly DateTime MinimumDate = new(1753, 1, 1);

    /// <summary>The latest supported date, matching the classic toolkit's calendar ceiling.</summary>
    internal static readonly DateTime MaximumDate = new(9998, 12, 31);


    /// <summary>The day-number strings "1"–"31", materialized once so painting stays allocation-free.</summary>
    private static string[]? _dayNumbers;

    private DateTime _displayMonth;
    private string? _title;
    private bool _dragging;

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

    /// <summary>The first day of the displayed month. Setting it drops the cached title.</summary>
    public DateTime DisplayMonth
    {
        get => _displayMonth;
        set
        {
            _displayMonth = value;
            _title = null;
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

    // --- Painting ----------------------------------------------------------------------------------

    /// <summary>Paints the whole page: title row with paging arrows, day-of-week header, 6×7 day grid
    /// (leading/trailing and out-of-range days greyed, selection highlighted, today circled in the
    /// accent color, the focus day outlined while <paramref name="showFocus"/>), and the border.</summary>
    public void Paint(IGraphics g, ITheme theme, Size size, bool showFocus)
    {
        g.FillRectangle(theme.FieldBackground, new(0, 0, size.Width, size.Height));

        var rowHeight = theme.RowHeight;
        var title = _title ??= _displayMonth.ToString("MMMM yyyy", Strings.DateTimeFormat);
        g.DrawText(title, theme.DefaultFont, theme.ControlText, new(0, 0, size.Width, rowHeight), ContentAlignment.MiddleCenter);
        DrawArrow(g, theme, new(0, 0, rowHeight, rowHeight), true);
        DrawArrow(g, theme, new(size.Width - rowHeight, 0, rowHeight, rowHeight), false);

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
        var minDay = this.MinDate.Date;
        var maxDay = this.MaxDate.Date;
        var today = this.TodayDate.Date;
        var focus = this.FocusDate.Date;
        for (var row = 0; row < 6; ++row)
            for (var col = 0; col < 7; ++col, date = date.AddDays(1))
            {
                var cell = new Rectangle(col * cellWidth, top + row * cellHeight, cellWidth, cellHeight);
                var selected = date >= selectionStart && date <= selectionEnd;
                if (selected)
                    GlyphRenderer.FillSelection(g, theme, cell);

                if (date == today)
                    g.DrawEllipse(theme.Accent, new(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2));

                var color = selected ? theme.SelectionText
                    : date < minDay || date > maxDay || date.Month != _displayMonth.Month || date.Year != _displayMonth.Year ? theme.DisabledText
                    : theme.ControlText;
                g.DrawText(numbers[date.Day - 1], theme.DefaultFont, color, cell, ContentAlignment.MiddleCenter);

                if (showFocus && date == focus)
                    g.DrawRectangle(theme.Accent, new(cell.X, cell.Y, cell.Width - 1, cell.Height - 1));
            }

        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
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

    /// <summary>Classifies a point on a page of the given size, producing the day it falls on.</summary>
    public CalendarHit HitTest(ITheme theme, Size size, int x, int y, out DateTime day)
    {
        day = default;
        if (x < 0 || x >= size.Width || y < 0 || y >= size.Height)
            return CalendarHit.None;

        var rowHeight = theme.RowHeight;
        if (y < rowHeight)
            return x < rowHeight ? CalendarHit.PreviousMonth
                : x >= size.Width - rowHeight ? CalendarHit.NextMonth
                : CalendarHit.None;

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

    /// <summary>Reacts to a press: pages on the title arrows, or starts a selection gesture on an
    /// in-range day — plain press selects it, Shift extends from the anchor.</summary>
    public void HandleMouseDown(ITheme theme, Size size, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        switch (this.HitTest(theme, size, e.X, e.Y, out var day))
        {
            case CalendarHit.PreviousMonth:
                this.NavigateMonths(-1);
                break;

            case CalendarHit.NextMonth:
                this.NavigateMonths(+1);
                break;

            case CalendarHit.Day when day >= this.MinDate.Date && day <= this.MaxDate.Date:
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

        if (this.HitTest(theme, size, e.X, e.Y, out var day) != CalendarHit.Day)
            return;

        if (day < this.MinDate.Date || day > this.MaxDate.Date)
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

    /// <summary>Pages the month by wheel turns: down pages forward, up pages back.</summary>
    public void HandleMouseWheel(int delta)
    {
        if (delta == 0)
            return;

        this.NavigateMonths(delta > 0 ? -1 : +1);
    }

    /// <summary>Keyboard navigation: arrows move the focus day, PageUp/PageDown page months (with
    /// Ctrl whole years), Home/End jump to the month edges, Enter/Space select the focus day.</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
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
