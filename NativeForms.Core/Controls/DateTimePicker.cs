using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A date field in the ComboBox shape: a closed, owner-drawn field showing <see cref="Value"/> in the
/// invariant <see cref="Format"/> with a themed drop arrow, which opens a light-dismiss popup hosting
/// the same month page as <see cref="MonthCalendar"/> — both surfaces run one shared engine, so they
/// stay pixel- and behavior-identical. Clicking a day commits it into <see cref="Value"/> preserving
/// the time of day; Escape or clicking elsewhere cancels. With <see cref="ShowCheckBox"/> the field
/// carries a check box; while unchecked the text greys and the value cannot be changed. Closed,
/// Up/Down step the day (the classic control steps whichever date part is focused — per-part focus is
/// not implemented yet, and neither is time editing) and Alt+Down or F4 opens the calendar.
/// </summary>
public class DateTimePicker : OwnerDrawnControl
{
    /// <summary>The gap around the check box and before the text.</summary>
    private const int _Padding = 4;

    private DateTime _value = DateTime.Now;
    private DateTime _minDate = CalendarCore.MinimumDate;
    private DateTime _maxDate = CalendarCore.MaximumDate;
    private string? _text;
    private bool _droppedDown;
    private bool _focused;

    private CalendarCore? _calendar;
    private IPopupPeer? _popup;
    private Size _popupSize;

    /// <summary>The picked date and time, clamped into [<see cref="MinDate"/>, <see cref="MaxDate"/>].
    /// Setting it repaints and raises <see cref="ValueChanged"/> when the value actually changes.</summary>
    public DateTime Value
    {
        get => _value;
        set
        {
            var clamped = value < _minDate ? _minDate : value > _maxDate ? _maxDate : value;
            if (_value == clamped)
                return;

            _value = clamped;
            _text = null;
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>The earliest pickable date; assignments and steps clamp to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is later than <see cref="MaxDate"/>.</exception>
    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            if (value < CalendarCore.MinimumDate)
                value = CalendarCore.MinimumDate;

            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _maxDate);
            _minDate = value;
            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The latest pickable date; assignments and steps clamp to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is earlier than <see cref="MinDate"/>.</exception>
    public DateTime MaxDate
    {
        get => _maxDate;
        set
        {
            if (value > CalendarCore.MaximumDate)
                value = CalendarCore.MaximumDate;

            ArgumentOutOfRangeException.ThrowIfLessThan(value, _minDate);
            _maxDate = value;
            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>How the closed field renders <see cref="Value"/>; always the invariant culture.</summary>
    public DateTimePickerFormat Format
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _text = null;
            this.Invalidate();
        }
    } = DateTimePickerFormat.Long;

    /// <summary>The invariant pattern used while <see cref="Format"/> is
    /// <see cref="DateTimePickerFormat.Custom"/>; empty renders an empty field.</summary>
    public string CustomFormat
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _text = null;
            this.Invalidate();
        }
    } = string.Empty;

    /// <summary>Whether the field carries a check box in front of the text.</summary>
    public bool ShowCheckBox
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>Whether the value applies. While <see cref="ShowCheckBox"/> is on and this is off,
    /// the text greys and every value-changing gesture is suppressed.</summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = true;

    /// <summary>Whether the calendar popup is currently open.</summary>
    public bool DroppedDown => _droppedDown;

    /// <summary>Raised when <see cref="Value"/> changes, by user gesture or assignment.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised when the calendar popup opens.</summary>
    public event EventHandler? DropDown;

    /// <summary>Raised when the calendar popup closes — by pick, cancel or light dismissal alike.</summary>
    public event EventHandler? CloseUp;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>An open calendar claims Enter (pick) and Escape (close) ahead of the form's dialog keys.</summary>
    protected override bool IsInputKey(Keys keyData)
        => this.DroppedDown && keyData is Keys.Enter or Keys.Escape;

    /// <summary>The width of the arrow-button zone at the right edge of the field.</summary>
    private int ButtonWidth => this.Theme.ScrollBarSize + 1;

    /// <summary>Whether value-changing gestures are currently suppressed by the unchecked box.</summary>
    private bool ValueLocked => this.ShowCheckBox && !this.Checked;

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="DropDown"/>.</summary>
    protected virtual void OnDropDown(EventArgs e) => this.DropDown?.Invoke(this, e);

    /// <summary>Raises <see cref="CloseUp"/>.</summary>
    protected virtual void OnCloseUp(EventArgs e) => this.CloseUp?.Invoke(this, e);

    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _droppedDown = false;
        _popup?.Dispose();
        _popup = null;
    }

    // --- The closed field --------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        var textLeft = _Padding;
        if (this.ShowCheckBox)
        {
            GlyphRenderer.DrawCheckBox(g, theme, new(_Padding, (height - GlyphRenderer.CheckBoxSize) / 2, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), this.Checked);
            textLeft = _Padding + GlyphRenderer.CheckBoxSize + _Padding;
        }

        var buttonWidth = this.ButtonWidth;
        var textColor = !this.Enabled || this.ValueLocked ? theme.DisabledText : theme.ControlText;
        g.DrawText(this.FormatValue(), theme.DefaultFont, textColor, new Rectangle(textLeft, 0, Math.Max(0, width - buttonWidth - textLeft), height), ContentAlignment.MiddleLeft);

        // The drop-down arrow, centered in the button zone.
        var arrowColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        GlyphRenderer.DrawComboArrow(g, arrowColor, new Rectangle(width - buttonWidth, 0, buttonWidth, height));

        g.DrawRectangle(_focused ? theme.Accent : theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.ShowCheckBox && e.X < _Padding + GlyphRenderer.CheckBoxSize + _Padding)
        {
            this.Checked = !this.Checked;
            return;
        }

        if (_droppedDown)
            this.CloseDropDown();
        else
            this.OpenDropDown();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F4 || (e.KeyCode == Keys.Down && e.Alt))
        {
            if (_droppedDown && e.KeyCode == Keys.F4)
                this.CloseDropDown();
            else
                this.OpenDropDown();

            e.Handled = true;
            return;
        }

        if (_droppedDown)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.CloseDropDown();
                e.Handled = true;
                return;
            }

            _calendar?.HandleKeyDown(e); // the popup calendar owns navigation while open
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Up: // closed arrows step the day directly; per-part stepping is pending
                this.StepDays(+1);
                e.Handled = true;
                break;

            case Keys.Down:
                this.StepDays(-1);
                e.Handled = true;
                break;
        }
    }

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
        this.CloseDropDown();
        this.Invalidate();
    }

    /// <summary>Formats <see cref="Value"/> with the pattern of the active <see cref="Format"/>
    /// through <see cref="Strings.DateTimeFormat"/> (invariant by default), cached between value
    /// changes so painting stays allocation-free.</summary>
    private string FormatValue()
        => _text ??= this.Format switch
        {
            DateTimePickerFormat.Long => _value.ToString("dddd, dd MMMM yyyy", Strings.DateTimeFormat),
            DateTimePickerFormat.Short => _value.ToString("MM/dd/yyyy", Strings.DateTimeFormat),
            DateTimePickerFormat.Time => _value.ToString("HH:mm:ss", Strings.DateTimeFormat),
            _ => this.CustomFormat.Length > 0 ? _value.ToString(this.CustomFormat, Strings.DateTimeFormat) : string.Empty,
        };

    /// <summary>Moves <see cref="Value"/> by whole days, refusing steps that leave the clamp window
    /// and steps while the unchecked box locks the value.</summary>
    private void StepDays(int days)
    {
        if (this.ValueLocked)
            return;

        var target = _value.AddDays(days);
        if (target < _minDate || target > _maxDate)
            return;

        this.Value = target;
    }

    // --- The calendar popup ------------------------------------------------------------------------

    /// <summary>
    /// Opens the calendar popup below the field, its page centered on <see cref="Value"/>. A no-op
    /// while already open or before the control is realized (only a live widget knows its screen
    /// position).
    /// </summary>
    public void OpenDropDown()
    {
        if (_droppedDown)
            return;

        var backend = this.Backend;
        if (backend is null)
            return;

        var popup = _popup ??= this.CreatePopup(backend);
        var calendar = _calendar!;
        var theme = this.Theme;
        _popupSize = new(7 * (theme.RowHeight + 4), 8 * theme.RowHeight);

        var day = _value.Date;
        calendar.MinDate = _minDate;
        calendar.MaxDate = _maxDate;
        calendar.TodayDate = DateTime.Today;
        calendar.SelectionStart = day;
        calendar.SelectionEnd = day;
        calendar.AnchorDate = day;
        calendar.FocusDate = day;
        calendar.DisplayMonth = new(day.Year, day.Month, 1);

        _droppedDown = true;
        this.OwnsOpenPopup = true;
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), _popupSize);
        this.Invalidate();
        this.OnDropDown(EventArgs.Empty);
    }

    /// <summary>Closes the calendar popup without committing. A no-op while closed.</summary>
    public void CloseDropDown()
    {
        if (!_droppedDown)
            return;

        _droppedDown = false;
        this.OwnsOpenPopup = false;
        _popup?.Hide();
        this.Invalidate();
        this.OnCloseUp(EventArgs.Empty);
    }

    private IPopupPeer CreatePopup(IPlatformBackend backend)
    {
        var calendar = new CalendarCore();
        calendar.Invalidated = () => _popup?.InvalidateAll();
        calendar.DateSelected = this.OnCalendarDateSelected;
        _calendar = calendar;

        var popup = backend.CreatePopup();
        popup.Paint += (_, e) => calendar.Paint(e.Graphics, this.Theme, _popupSize, true);
        popup.MouseDown += (_, e) => calendar.HandleMouseDown(this.Theme, _popupSize, e);
        popup.MouseMove += (_, e) => calendar.HandleMouseMove(this.Theme, _popupSize, e);
        popup.MouseUp += (_, e) => calendar.HandleMouseUp(e);
        popup.MouseWheel += (_, e) => calendar.HandleMouseWheel(e.Delta);
        popup.KeyDown += (_, e) => this.OnKeyDown(e); // backends with a keyboard grab route keys here
        popup.Dismissed += (_, _) => this.OnPopupDismissed();
        return popup;
    }

    /// <summary>Commits the day the popup calendar selected — keeping the time of day — unless the
    /// unchecked box locks the value; the popup closes either way.</summary>
    private void OnCalendarDateSelected()
    {
        this.CloseDropDown();
        if (this.ValueLocked)
            return;

        this.Value = _calendar!.SelectionStart.Date + _value.TimeOfDay;
    }

    /// <summary>Reacts to light dismissal (click outside, grab loss, Escape): the surface is already
    /// hidden, so only the open flag and the field's arrow state need resetting.</summary>
    private void OnPopupDismissed()
    {
        if (!_droppedDown)
            return;

        _droppedDown = false;
        this.OwnsOpenPopup = false;
        this.Invalidate();
        this.OnCloseUp(EventArgs.Empty);
    }
}
