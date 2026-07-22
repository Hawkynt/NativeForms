using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A time-of-day editor in the classic spinner shape: an owner-drawn field paints
/// <see cref="Value"/> as <c>HH:mm[:ss]</c> (or <c>hh:mm[:ss] AM</c> on a 12-hour clock) with the
/// part under the caret highlighted, and the themed <see cref="Drawing.SpinnerRenderer"/> button
/// column at the right edge steps exactly that part. Left/Right move the caret between parts,
/// Up/Down step the selected one, and holding a spinner button auto-repeats through the shared
/// <see cref="AutoRepeat"/> engine (500 ms, then every 50 ms) — the same cadence as
/// <see cref="NumericUpDown"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="NumericUpDown"/> this control does not host a native <see cref="TextBox"/>: a
/// per-part caret needs to know where the click landed inside the text, which a hosted native editor
/// never reports back to the core. The field is therefore drawn and hit-tested here, part by part,
/// and there is no free-form typed edit to commit — every change goes through a step, so
/// <see cref="Value"/> is always valid and <see cref="ValueChanged"/> never fires for garbage.
/// Stepping wraps within the part without carrying into the next one (23:59 stepped up on the
/// minute becomes 23:00), which is what the Win32 date/time control does; a step that would leave
/// [<see cref="MinTime"/>, <see cref="MaxTime"/>] is refused instead.
/// </remarks>
public class TimePicker : OwnerDrawnControl
{
    /// <summary>The gap before the first part and after the last one.</summary>
    private const int _Padding = 4;

    /// <summary>The two-digit strings "00"–"59", materialized once so painting stays allocation-free.</summary>
    private static string[]? _twoDigits;

    /// <summary>One second short of a full day: the latest time of day the control can hold.</summary>
    private static readonly TimeSpan _DayEnd = new(23, 59, 59);

    private TimeSpan _value = TruncateToSecond(DateTime.Now.TimeOfDay);
    private TimeSpan _minTime = TimeSpan.Zero;
    private TimeSpan _maxTime = _DayEnd;
    private TimePickerField _selectedField;
    private bool _focused;
    private int _pressedDirection; // +1 up button held, -1 down button held, 0 none
    private AutoRepeat? _autoRepeat;

    // The double-click-opened analog clock face. Created lazily on the first open, hosted in a
    // light-dismiss popup exactly as DateTimePicker hosts its calendar.
    private ClockFace? _clock;
    private IPopupPeer? _popup;
    private long _lastClickTime;

    /// <summary>
    /// The picked time of day — whole seconds, clamped into [<see cref="MinTime"/>,
    /// <see cref="MaxTime"/>] and into a single day, defaulting to the current time of day like
    /// <see cref="DateTimePicker.Value"/> does. Setting it repaints and raises
    /// <see cref="ValueChanged"/> when the value actually changes.
    /// </summary>
    public TimeSpan Value
    {
        get => _value;
        set
        {
            var clamped = this.Clamp(value);
            if (_value == clamped)
                return;

            _value = clamped;
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>The earliest pickable time; assignments and steps clamp to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, spans more than a day, or is later than <see cref="MaxTime"/>.</exception>
    public TimeSpan MinTime
    {
        get => _minTime;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _DayEnd);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _maxTime);
            _minTime = value;
            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The latest pickable time; assignments and steps clamp to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, spans more than a day, or is earlier than <see cref="MinTime"/>.</exception>
    public TimeSpan MaxTime
    {
        get => _maxTime;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _DayEnd);
            ArgumentOutOfRangeException.ThrowIfLessThan(value, _minTime);
            _maxTime = value;
            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>
    /// Whether the field carries a seconds part. Turning it off moves a caret parked on the seconds
    /// back to the minutes and drops the seconds from <see cref="Value"/>, so what the field shows
    /// is what the value holds. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ShowSeconds
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value)
            {
                if (_selectedField == TimePickerField.Second)
                    _selectedField = TimePickerField.Minute;

                this.Value = new(_value.Hours, _value.Minutes, 0);
            }

            this.Invalidate();
        }
    } = true;

    /// <summary>
    /// Whether the hour part runs 00–23 rather than 01–12 with an AM/PM part. Defaults to
    /// <see langword="true"/>: the repository builds with <c>InvariantGlobalization</c>, and the
    /// invariant culture's short-time pattern is the 24-hour <c>HH:mm</c> — the default is spelled
    /// out here rather than probed from <see cref="Strings.DateTimeFormat"/> so it cannot drift when
    /// an application swaps that provider after the control was built. Set it to
    /// <see langword="false"/> for a 12-hour field.
    /// </summary>
    public bool Use24HourClock
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (value && _selectedField == TimePickerField.Meridiem)
                _selectedField = TimePickerField.Hour;

            this.Invalidate();
        }
    } = true;

    /// <summary>
    /// The part the caret sits on — what the spinner buttons and the Up/Down keys step. Assigning a
    /// part the current layout does not show (seconds while <see cref="ShowSeconds"/> is off, AM/PM
    /// while <see cref="Use24HourClock"/> is on) falls back to the hour.
    /// </summary>
    public TimePickerField SelectedField
    {
        get => _selectedField;
        set
        {
            if (!this.IsFieldVisible(value))
                value = TimePickerField.Hour;

            if (_selectedField == value)
                return;

            _selectedField = value;
            this.Invalidate();
        }
    }

    /// <summary>Raised when <see cref="Value"/> changes, by stepping or assignment.</summary>
    public event EventHandler? ValueChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Left/Right pick the part, so they stay out of the form's dialog-key routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right;

    /// <summary>Steps the selected part one increment up.</summary>
    public void UpButton() => this.Step(+1);

    /// <summary>Steps the selected part one increment down.</summary>
    public void DownButton() => this.Step(-1);

    /// <summary>Moves the caret to the previous visible part, stopping at the first one.</summary>
    public void SelectPreviousField() => this.MoveField(-1);

    /// <summary>Moves the caret to the next visible part, stopping at the last one.</summary>
    public void SelectNextField() => this.MoveField(+1);

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _pressedDirection = 0;
        _autoRepeat?.Dispose();
        _autoRepeat = null;
        this.OwnsOpenPopup = false;
        _popup?.Dispose();
        _popup = null;
        _clock = null;
    }

    // --- The analog clock face ---------------------------------------------------------------------

    /// <summary>Whether the double-click clock face is currently open.</summary>
    public bool ClockDroppedDown => this.OwnsOpenPopup;

    /// <summary>
    /// Opens the analog clock face below the field, its hand on <see cref="Value"/> and its first
    /// stage the hour. A no-op while already open or before the control is realized (only a live
    /// widget knows its screen position). Committing keeps the picked value; Escape or an outside
    /// click reverts to the value the field held when it opened.
    /// </summary>
    public void OpenClock()
    {
        if (this.OwnsOpenPopup)
            return;

        var backend = this.Backend;
        if (backend is null)
            return;

        var popup = _popup ??= this.CreateClockPopup(backend);
        var clock = _clock!;
        clock.Use24HourClock = this.Use24HourClock;
        clock.ShowSeconds = this.ShowSeconds;
        clock.Stage = ClockFaceStage.Hour;
        clock.OriginalValue = _value;
        clock.Value = _value;

        this.OwnsOpenPopup = true;
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), ClockFace.PreferredSize(this.Theme));
        this.Invalidate();
    }

    /// <summary>Closes the clock face, keeping whatever value is showing. A no-op while closed.</summary>
    public void CloseClock()
    {
        if (!this.OwnsOpenPopup)
            return;

        this.OwnsOpenPopup = false;
        _popup?.Hide();
        this.Invalidate();
    }

    private IPopupPeer CreateClockPopup(IPlatformBackend backend)
    {
        var clock = new ClockFace();
        clock.Invalidated = () => _popup?.InvalidateAll();
        clock.ValueChanged += (_, _) => this.Value = _clock!.Value; // live preview into the field
        clock.Committed = this.CloseClock;
        clock.Cancelled = this.OnClockCancelled;
        _clock = clock;

        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => clock.Paint(e.Graphics, this.Theme, ClockFace.PreferredSize(this.Theme));
        popup.MouseDown += (_, e) => clock.HandleMouseDown(this.Theme, ClockFace.PreferredSize(this.Theme), e);
        popup.MouseMove += (_, e) => clock.HandleMouseMove(this.Theme, ClockFace.PreferredSize(this.Theme), e);
        popup.MouseUp += (_, e) => clock.HandleMouseUp(e);
        popup.MouseWheel += (_, e) => clock.HandleMouseWheel(e.Delta);
        popup.KeyDown += (_, e) => this.OnKeyDown(e); // backends with a keyboard grab route keys here
        popup.Dismissed += (_, _) => this.OnPopupDismissed();
        return popup;
    }

    /// <summary>Escape on the dial: revert to the value the field opened on, then close.</summary>
    private void OnClockCancelled()
    {
        this.Value = _clock!.OriginalValue;
        this.CloseClock();
    }

    /// <summary>Reacts to light dismissal (outside click, grab loss): the surface is already hidden,
    /// so revert to the value the field opened on and reset the open flag.</summary>
    private void OnPopupDismissed()
    {
        if (!this.OwnsOpenPopup)
            return;

        this.OwnsOpenPopup = false;
        this.Value = _clock!.OriginalValue;
        this.Invalidate();
    }

    // --- Painting ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        var font = theme.DefaultFont;
        var digits = _twoDigits ??= CreateTwoDigits();
        var textColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        var digitWidth = g.MeasureText("00", font).Width;
        var separatorWidth = g.MeasureText(":", font).Width;
        var meridiemWidth = g.MeasureText("AM", font).Width;

        var x = _Padding;
        this.PaintPart(g, theme, font, digits[this.DisplayHour()], TimePickerField.Hour, ref x, digitWidth, textColor);
        g.DrawText(":", font, textColor, new(x, 0, separatorWidth, height), ContentAlignment.MiddleCenter);
        x += separatorWidth;
        this.PaintPart(g, theme, font, digits[_value.Minutes], TimePickerField.Minute, ref x, digitWidth, textColor);
        if (this.ShowSeconds)
        {
            g.DrawText(":", font, textColor, new(x, 0, separatorWidth, height), ContentAlignment.MiddleCenter);
            x += separatorWidth;
            this.PaintPart(g, theme, font, digits[_value.Seconds], TimePickerField.Second, ref x, digitWidth, textColor);
        }

        if (!this.Use24HourClock)
        {
            x += separatorWidth; // the blank before the meridiem, one separator wide
            this.PaintPart(g, theme, font, _value.Hours < 12 ? "AM" : "PM", TimePickerField.Meridiem, ref x, meridiemWidth, textColor);
        }

        SpinnerRenderer.Paint(g, theme, width, height, _pressedDirection, this.Enabled);
    }

    /// <summary>Paints one part into its slot, highlighting it while it carries the caret and the
    /// control has focus, then advances the running x by the slot width.</summary>
    private void PaintPart(IGraphics g, ITheme theme, Font font, string text, TimePickerField part, ref int x, int slotWidth, Color textColor)
    {
        var slot = new Rectangle(x, 1, slotWidth, this.Height - 2);
        var selected = _focused && _selectedField == part;
        if (selected)
            GlyphRenderer.FillSelection(g, theme, slot);

        g.DrawText(text, font, selected ? theme.SelectionText : textColor, slot, ContentAlignment.MiddleCenter);
        x += slotWidth;
    }

    /// <summary>The hour as the current clock shows it: 0–23, or 1–12 on a 12-hour field.</summary>
    private int DisplayHour()
    {
        if (this.Use24HourClock)
            return _value.Hours;

        var hour = _value.Hours % 12;
        return hour == 0 ? 12 : hour;
    }

    private static string[] CreateTwoDigits()
    {
        var digits = new string[60];
        for (var i = 0; i < digits.Length; ++i)
            digits[i] = i.ToString("00", CultureInfo.InvariantCulture);

        return digits;
    }

    // --- Input -------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var theme = this.Theme;
        if (SpinnerRenderer.UpButton(theme, this.Width, this.Height).Contains(e.Location))
        {
            this.PressButton(+1);
            return;
        }

        if (SpinnerRenderer.DownButton(theme, this.Width, this.Height).Contains(e.Location))
        {
            this.PressButton(-1);
            return;
        }

        // Everything left of the spinner is the field: a click parks the caret on the part under it,
        // and a second click inside the double-click window opens the analog clock face.
        var now = Environment.TickCount64;
        var isDouble = !this.ClockDroppedDown && now - _lastClickTime <= this.Theme.DoubleClickTime;
        _lastClickTime = isDouble ? 0 : now; // reset so a triple click is not two doubles

        if (this.FieldAt(e.X) is { } part)
            this.SelectedField = part;

        if (isDouble)
            this.OpenClock();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => this.ReleaseButton();

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e) => this.ReleaseButton();

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e.Delta == 0)
            return;

        this.Step(e.Delta > 0 ? +1 : -1);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // While the clock is up it owns the keyboard — arrows nudge the hand, Tab/Enter walk the
        // stages and commit, Escape cancels — routed through the popup's key grab into the dial.
        if (this.ClockDroppedDown)
        {
            _clock?.HandleKeyDown(e);
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Up:
                this.UpButton();
                e.Handled = true;
                break;

            case Keys.Down:
                this.DownButton();
                e.Handled = true;
                break;

            case Keys.Left:
                this.MoveField(-1);
                e.Handled = true;
                break;

            case Keys.Right:
                this.MoveField(+1);
                e.Handled = true;
                break;

            case Keys.Home:
                this.Value = _minTime;
                e.Handled = true;
                break;

            case Keys.End:
                this.Value = _maxTime;
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
        this.ReleaseButton();
        this.Invalidate();
    }

    /// <summary>The part whose slot covers <paramref name="x"/>, or <see langword="null"/> for the
    /// gaps and separators between them. Measured through the backend, which the peer contract binds
    /// to the same text engine <see cref="IGraphics.MeasureText"/> uses, so hit-testing and painting
    /// agree pixel for pixel.</summary>
    private TimePickerField? FieldAt(int x)
    {
        var backend = this.Backend;
        if (backend is null)
            return null;

        var font = this.Theme.DefaultFont;
        var digitWidth = backend.MeasureText("00", font).Width;
        var separatorWidth = backend.MeasureText(":", font).Width;
        var meridiemWidth = backend.MeasureText("AM", font).Width;

        var left = _Padding;
        if (x >= left && x < left + digitWidth)
            return TimePickerField.Hour;

        left += digitWidth + separatorWidth;
        if (x >= left && x < left + digitWidth)
            return TimePickerField.Minute;

        left += digitWidth;
        if (this.ShowSeconds)
        {
            left += separatorWidth;
            if (x >= left && x < left + digitWidth)
                return TimePickerField.Second;

            left += digitWidth;
        }

        if (!this.Use24HourClock)
        {
            left += separatorWidth;
            if (x >= left && x < left + meridiemWidth)
                return TimePickerField.Meridiem;
        }

        return null;
    }

    /// <summary>Whether the current layout shows the given part at all.</summary>
    private bool IsFieldVisible(TimePickerField part) => part switch
    {
        TimePickerField.Second => this.ShowSeconds,
        TimePickerField.Meridiem => !this.Use24HourClock,
        _ => true,
    };

    /// <summary>Moves the caret to the next visible part in the given direction, stopping at the
    /// first and last one rather than wrapping.</summary>
    private void MoveField(int direction)
    {
        var next = (int)_selectedField;
        for (var i = 0; i < 4; ++i)
        {
            next += direction;
            if (next < 0 || next > (int)TimePickerField.Meridiem)
                return;

            if (!this.IsFieldVisible((TimePickerField)next))
                continue;

            this.SelectedField = (TimePickerField)next;
            return;
        }
    }

    /// <summary>
    /// Steps the selected part by <paramref name="direction"/> increments. The part wraps within
    /// itself without carrying (23 + 1 = 00, 59 + 1 = 00), matching the classic control; a result
    /// outside [<see cref="MinTime"/>, <see cref="MaxTime"/>] is refused, leaving the value alone.
    /// </summary>
    private void Step(int direction)
    {
        var hours = _value.Hours;
        var minutes = _value.Minutes;
        var seconds = _value.Seconds;
        switch (_selectedField)
        {
            case TimePickerField.Hour:
                hours = Wrap(hours + direction, 24);
                break;

            case TimePickerField.Minute:
                minutes = Wrap(minutes + direction, 60);
                break;

            case TimePickerField.Second:
                seconds = Wrap(seconds + direction, 60);
                break;

            default: // TimePickerField.Meridiem — either step flips the half-day
                hours = Wrap(hours + 12, 24);
                break;
        }

        var target = new TimeSpan(hours, minutes, seconds);
        if (target < _minTime || target > _maxTime)
            return; // a step out of the window is refused, not clamped: the part would lie

        this.Value = target;
    }

    /// <summary>Wraps <paramref name="value"/> into [0, <paramref name="period"/>).</summary>
    private static int Wrap(int value, int period) => ((value % period) + period) % period;

    /// <summary>Drops sub-second precision the field could never show.</summary>
    private static TimeSpan TruncateToSecond(TimeSpan value) => new(value.Hours, value.Minutes, value.Seconds);

    /// <summary>Clamps a time into the day and into [<see cref="MinTime"/>, <see cref="MaxTime"/>],
    /// dropping the parts the current layout does not show.</summary>
    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        else if (value > _DayEnd)
            value = _DayEnd;

        value = this.ShowSeconds ? TruncateToSecond(value) : new(value.Hours, value.Minutes, 0);
        return value < _minTime ? _minTime : value > _maxTime ? _maxTime : value;
    }

    // --- Spinner buttons ---------------------------------------------------------------------------

    /// <summary>Presses a spinner button: steps once and arms the autorepeat.</summary>
    private void PressButton(int direction)
    {
        _pressedDirection = direction;
        this.StepPressedButton();
        this.Invalidate();

        var backend = this.Backend;
        if (backend is null)
            return;

        _autoRepeat ??= new(this.StepPressedButton);
        _autoRepeat.Start(backend);
    }

    /// <summary>Releases a pressed spinner button and stops its autorepeat.</summary>
    private void ReleaseButton()
    {
        if (_pressedDirection == 0)
            return;

        _pressedDirection = 0;
        _autoRepeat?.Stop();
        this.Invalidate();
    }

    /// <summary>Steps once in the pressed button's direction; the autorepeat tick action.</summary>
    private void StepPressedButton()
    {
        if (_pressedDirection != 0)
            this.Step(_pressedDirection);
    }
}
