namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn one-month calendar page in the native theme: a title row ("July 2026", invariant)
/// with previous/next paging arrows, a day-of-week header of two-letter invariant names starting at
/// <see cref="FirstDayOfWeek"/>, and a 6×7 day grid with leading/trailing days greyed and today
/// circled in the accent color. A click selects one day; Shift+click or dragging selects a range of
/// up to <see cref="MaxSelectionCount"/> days. The keyboard moves a focus day (arrows), pages months
/// (PageUp/PageDown, with Ctrl whole years), jumps to the month edges (Home/End) and selects
/// (Enter/Space); the wheel pages months. Days outside [<see cref="MinDate"/>, <see cref="MaxDate"/>]
/// paint disabled and reject clicks. Bolded dates are not implemented yet.
/// </summary>
public class MonthCalendar : OwnerDrawnControl
{
    private readonly CalendarCore _core;
    private bool _focused;

    /// <summary>Creates a calendar showing the current month with today selected.</summary>
    public MonthCalendar()
    {
        var core = new CalendarCore();
        core.Invalidated = this.Invalidate;
        core.SelectionChanged = this.RaiseDateChanged;
        core.DateSelected = this.RaiseDateSelected;
        _core = core;
    }

    /// <summary>The first selected day. Setting it keeps the end when still valid, collapsing or
    /// capping the range onto the new start otherwise.</summary>
    public DateTime SelectionStart
    {
        get => _core.SelectionStart;
        set
        {
            var start = value.Date;
            var end = _core.SelectionEnd;
            if (end < start)
                end = start;
            else if ((end - start).Days >= _core.MaxSelectionCount)
                end = start.AddDays(_core.MaxSelectionCount - 1);

            this.ApplyRange(start, end, true);
        }
    }

    /// <summary>The last selected day. Setting it keeps the start when still valid, collapsing or
    /// capping the range onto the new end otherwise.</summary>
    public DateTime SelectionEnd
    {
        get => _core.SelectionEnd;
        set
        {
            var end = value.Date;
            var start = _core.SelectionStart;
            if (start > end)
                start = end;
            else if ((end - start).Days >= _core.MaxSelectionCount)
                start = end.AddDays(-(_core.MaxSelectionCount - 1));

            this.ApplyRange(start, end, false);
        }
    }

    /// <summary>The largest number of days a selection may span. Shrinking it trims the current range.</summary>
    public int MaxSelectionCount
    {
        get => _core.MaxSelectionCount;
        set
        {
            _core.MaxSelectionCount = Math.Max(1, value);
            if ((_core.SelectionEnd - _core.SelectionStart).Days >= _core.MaxSelectionCount)
                this.ApplyRange(_core.SelectionStart, _core.SelectionStart.AddDays(_core.MaxSelectionCount - 1), true);
        }
    }

    /// <summary>The day of week shown in the leftmost column. Defaults to Monday.</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => _core.FirstDayOfWeek;
        set
        {
            if (_core.FirstDayOfWeek == value)
                return;

            _core.FirstDayOfWeek = value;
            this.Invalidate();
        }
    }

    /// <summary>The earliest selectable day; earlier cells paint disabled and reject clicks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is later than <see cref="MaxDate"/>.</exception>
    public DateTime MinDate
    {
        get => _core.MinDate;
        set
        {
            if (value < CalendarCore.MinimumDate)
                value = CalendarCore.MinimumDate;

            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _core.MaxDate);
            _core.MinDate = value;
            this.ApplyRange(_core.SelectionStart, _core.SelectionEnd, true);
        }
    }

    /// <summary>The latest selectable day; later cells paint disabled and reject clicks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is earlier than <see cref="MinDate"/>.</exception>
    public DateTime MaxDate
    {
        get => _core.MaxDate;
        set
        {
            if (value > CalendarCore.MaximumDate)
                value = CalendarCore.MaximumDate;

            ArgumentOutOfRangeException.ThrowIfLessThan(value, _core.MinDate);
            _core.MaxDate = value;
            this.ApplyRange(_core.SelectionStart, _core.SelectionEnd, true);
        }
    }

    /// <summary>The day wearing the accent circle. Defaults to the current date; settable, like its
    /// WinForms namesake, so long-running views and tests stay deterministic.</summary>
    public DateTime TodayDate
    {
        get => _core.TodayDate;
        set
        {
            _core.TodayDate = value.Date;
            this.Invalidate();
        }
    }

    /// <summary>Raised whenever the selected range changes, by gesture or assignment.</summary>
    public event EventHandler<DateRangeEventArgs>? DateChanged;

    /// <summary>Raised when the user commits a selection: the click gesture ends or Enter/Space lands.</summary>
    public event EventHandler<DateRangeEventArgs>? DateSelected;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Enter picks the highlighted date, so it stays out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter;

    /// <summary>Selects the given range in one call, swapping reversed ends and capping the span at
    /// <see cref="MaxSelectionCount"/> days.</summary>
    public void SetSelectionRange(DateTime start, DateTime end)
    {
        start = start.Date;
        end = end.Date;
        if (end < start)
            (start, end) = (end, start);

        if ((end - start).Days >= _core.MaxSelectionCount)
            end = start.AddDays(_core.MaxSelectionCount - 1);

        this.ApplyRange(start, end, true);
    }

    /// <summary>Raises <see cref="DateChanged"/>.</summary>
    protected virtual void OnDateChanged(DateRangeEventArgs e) => this.DateChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="DateSelected"/>.</summary>
    protected virtual void OnDateSelected(DateRangeEventArgs e) => this.DateSelected?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e) => _core.Paint(e.Graphics, this.Theme, this.Size, _focused);

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        _core.HandleMouseDown(this.Theme, this.Size, e);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e) => _core.HandleMouseMove(this.Theme, this.Size, e);

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _core.HandleMouseUp(e);

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e) => _core.HandleMouseWheel(e.Delta);

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) => _core.HandleKeyDown(e);

    /// <inheritdoc/>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _focused = true;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _focused = false;
        this.Invalidate();
    }

    /// <summary>Applies a normalized range: anchor and focus follow it, the displayed month follows
    /// the focused end, and the engine raises <see cref="DateChanged"/> if anything moved.</summary>
    private void ApplyRange(DateTime start, DateTime end, bool focusStart)
    {
        start = _core.ClampDay(start);
        end = _core.ClampDay(end);
        _core.AnchorDate = start;
        _core.FocusDate = focusStart ? start : end;
        _core.ShowMonthOf(_core.FocusDate);
        _core.SetSelection(start, end);
    }

    private void RaiseDateChanged() => this.OnDateChanged(new(_core.SelectionStart, _core.SelectionEnd));

    private void RaiseDateSelected() => this.OnDateSelected(new(_core.SelectionStart, _core.SelectionEnd));
}
