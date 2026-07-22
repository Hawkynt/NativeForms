using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>Which hand of a <see cref="ClockFace"/> the dial is currently editing.</summary>
public enum ClockFaceStage
{
    /// <summary>The hour ring.</summary>
    Hour,

    /// <summary>The minute ring, labelled every five minutes.</summary>
    Minute,

    /// <summary>The seconds ring; only reachable while <see cref="ClockFace.ShowSeconds"/> is on.</summary>
    Second,
}

/// <summary>
/// A material-style analog time picker: a themed dial with a ring of hour numbers, a centre hub and a
/// hand pointing at the selected value. Clicking or dragging the hand onto a number sets that part;
/// the dial advances through stages — <see cref="ClockFaceStage.Hour"/>, then
/// <see cref="ClockFaceStage.Minute"/>, then <see cref="ClockFaceStage.Second"/> while
/// <see cref="ShowSeconds"/> is on — and an <c>OK</c> affordance (or Enter on the last stage) commits.
/// A 12-hour dial carries an AM/PM toggle. The control is drawn once in the core and runs on every
/// backend.
/// </summary>
/// <remarks>
/// Like <c>CalendarCore</c> behind <see cref="MonthCalendar"/>, this surface is deliberately dual: the
/// familiar <see cref="OwnerDrawnControl"/> overrides let it stand alone in a form, while the public
/// <see cref="Paint(IGraphics, ITheme, Size)"/>/<c>Handle…</c> engine methods and the
/// <see cref="Invalidated"/>/<see cref="Committed"/>/<see cref="Cancelled"/> callback slots let a host
/// paint it into a light-dismiss popup — which is exactly how <see cref="TimePicker"/> opens it on a
/// double-click. Every string it paints and the hand endpoint are cached and rebuilt only on a
/// stage/value change, never per frame, so a steady repaint allocates nothing.
///
/// <para>The 24-hour dial uses two concentric rings sharing the twelve clock positions: the inner ring
/// holds <c>00</c>–<c>11</c> and the outer ring <c>12</c>–<c>23</c> (so <c>00</c>/<c>12</c> sit at the
/// top, <c>03</c>/<c>15</c> at three o'clock). A click picks the ring by how far from the centre it
/// lands. The minute and seconds rings are labelled at the twelve five-unit marks but a click or drag
/// snaps to the nearest single unit, so any minute is reachable.</para>
/// </remarks>
public class ClockFace : OwnerDrawnControl
{
    /// <summary>The gap around the dial and header.</summary>
    private const int _Margin = 8;

    /// <summary>The strings "12","1"…"11" for the 12-hour ring (index 0 is noon/midnight).</summary>
    private static string[]? _hours12;

    /// <summary>The strings "12"…"23" for the outer 24-hour ring (index 0 is "12").</summary>
    private static string[]? _hoursOuter;

    /// <summary>The strings "00"…"11" for the inner 24-hour ring and the two-digit readout.</summary>
    private static string[]? _twoDigits;

    /// <summary>The five-unit labels "00","05"…"55" for the minute and seconds rings.</summary>
    private static string[]? _fives;

    /// <summary>The sine of each of the sixty 6° positions, top-clockwise; built once.</summary>
    private static double[]? _sin;

    /// <summary>The cosine of each of the sixty 6° positions, top-clockwise; built once.</summary>
    private static double[]? _cos;

    private TimeSpan _value = TruncateToSecond(DateTime.Now.TimeOfDay);
    private ClockFaceStage _stage;
    private bool _dragging;
    private bool _pressedOnDial;

    // Cached hand endpoint, rebuilt only when the value, stage or laid-out size changes.
    private Size _geometrySize;
    private TimeSpan _geometryValue = new(-1, 0, 0);
    private ClockFaceStage _geometryStage = (ClockFaceStage)(-1);
    private Point _handTip;
    private int _handRadius;

    /// <summary>The selected time of day — whole seconds, kept inside a single day. Setting it
    /// repaints and raises <see cref="ValueChanged"/> when it actually changes.</summary>
    public TimeSpan Value
    {
        get => _value;
        set
        {
            value = Clamp(value);
            if (_value == value)
                return;

            _value = value;
            this.Repaint();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>The value the dial opened on — a host reverts to it on cancel. Assigning it does not
    /// raise <see cref="ValueChanged"/>.</summary>
    public TimeSpan OriginalValue { get; set; }

    /// <summary>Whether the hour ring runs 00–23 over two rings rather than a single 01–12 ring with an
    /// AM/PM toggle. Defaults to <see langword="true"/>, matching <see cref="TimePicker"/>.</summary>
    public bool Use24HourClock
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.InvalidateGeometry();
            this.Repaint();
        }
    } = true;

    /// <summary>Whether the dial offers a seconds stage after the minute. Turning it off while the
    /// seconds stage is active falls back to the minute.</summary>
    public bool ShowSeconds
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value && _stage == ClockFaceStage.Second)
                this.Stage = ClockFaceStage.Minute;

            this.Repaint();
        }
    }

    /// <summary>The hand the dial is currently editing. Assigning a seconds stage while
    /// <see cref="ShowSeconds"/> is off falls back to the minute.</summary>
    public ClockFaceStage Stage
    {
        get => _stage;
        set
        {
            if (value == ClockFaceStage.Second && !this.ShowSeconds)
                value = ClockFaceStage.Minute;

            if (_stage == value)
                return;

            _stage = value;
            this.InvalidateGeometry();
            this.Repaint();
            this.OnStageChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="Value"/> changes, by gesture or assignment.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised when <see cref="Stage"/> changes.</summary>
    public event EventHandler? StageChanged;

    /// <summary>Invoked whenever a host surface needs repainting — the popup-hosting hook, left
    /// <see langword="null"/> for a stand-alone control that repaints through its own canvas.</summary>
    public Action? Invalidated { get; set; }

    /// <summary>Invoked when the user commits: the OK affordance, or Enter on the final stage.</summary>
    public Action? Committed { get; set; }

    /// <summary>Invoked when the user cancels with Escape.</summary>
    public Action? Cancelled { get; set; }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>An open dial claims Enter/Escape and the arrows ahead of the form's dialog keys.</summary>
    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Escape or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab;

    /// <summary>The final stage of the current layout — the seconds ring when shown, else the minute.</summary>
    public ClockFaceStage FinalStage => this.ShowSeconds ? ClockFaceStage.Second : ClockFaceStage.Minute;

    /// <summary>The natural popup size for a dial painted with the given theme: a square dial framed by
    /// a header (the readout and AM/PM toggle) and a footer (the OK affordance).</summary>
    public static Size PreferredSize(ITheme theme)
    {
        var band = theme.RowHeight + 8;
        var dial = 8 * theme.RowHeight;
        return new(dial + (2 * _Margin), band + dial + band);
    }

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="StageChanged"/>.</summary>
    protected virtual void OnStageChanged(EventArgs e) => this.StageChanged?.Invoke(this, e);

    // --- Stand-alone control surface ---------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
        => this.Paint(e.Graphics, this.Theme, new Size(this.Width, this.Height));

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
        => this.HandleMouseDown(this.Theme, new Size(this.Width, this.Height), e);

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
        => this.HandleMouseMove(this.Theme, new Size(this.Width, this.Height), e);

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => this.HandleMouseUp(e);

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e) => this.HandleMouseWheel(e.Delta);

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) => this.HandleKeyDown(e);

    // --- Geometry ----------------------------------------------------------------------------------

    /// <summary>The laid-out geometry of the dial for a theme and size — value type, so both painting
    /// and hit-testing share one definition without allocating.</summary>
    private readonly struct Layout
    {
        public readonly int CenterX;
        public readonly int CenterY;
        public readonly int OuterRadius;
        public readonly int InnerRadius;
        public readonly int NumberRadius;
        public readonly int HeaderHeight;
        public readonly int Width;
        public readonly int Height;
        public readonly bool TwentyFourHour;
        public readonly bool ShowSeconds;

        public Layout(ITheme theme, Size size, bool twentyFourHour, bool showSeconds)
        {
            this.Width = size.Width;
            this.Height = size.Height;
            this.TwentyFourHour = twentyFourHour;
            this.ShowSeconds = showSeconds;
            this.HeaderHeight = theme.RowHeight + 8;
            var footer = theme.RowHeight + 8;

            var top = this.HeaderHeight;
            var dialHeight = Math.Max(0, size.Height - top - footer);
            var dialWidth = Math.Max(0, size.Width - (2 * _Margin));
            var diameter = Math.Min(dialWidth, dialHeight);
            this.CenterX = size.Width / 2;
            this.CenterY = top + (dialHeight / 2);
            this.NumberRadius = Math.Clamp(diameter / 9, 8, theme.RowHeight);
            this.OuterRadius = Math.Max(0, (diameter / 2) - this.NumberRadius - 2);
            this.InnerRadius = twentyFourHour ? this.OuterRadius * 3 / 5 : this.OuterRadius;
        }

        /// <summary>The OK affordance zone in the footer.</summary>
        public Rectangle OkButton
        {
            get
            {
                var w = Math.Min(this.Width - (2 * _Margin), 3 * (this.HeaderHeight - 8));
                var h = this.HeaderHeight - 12;
                return new(this.Width - _Margin - w, this.Height - _Margin - h, w, h);
            }
        }

        /// <summary>The AM/PM toggle zone at the header's right edge on a 12-hour dial.</summary>
        public Rectangle Meridiem
        {
            get
            {
                var w = 2 * (this.HeaderHeight - 8);
                return new(this.Width - _Margin - w, 2, w, this.HeaderHeight - 4);
            }
        }

        /// <summary>The x where the readout segments end — before the AM/PM toggle on a 12-hour dial.</summary>
        public int SegmentsRight => this.TwentyFourHour ? this.Width - _Margin : this.Meridiem.X - 4;

        /// <summary>The number of readout segments: hour + minute, plus seconds when shown.</summary>
        public int SegmentCount => this.ShowSeconds ? 3 : 2;

        /// <summary>The rectangle of readout segment <paramref name="index"/> (0=hour, 1=minute, 2=seconds).</summary>
        public Rectangle Segment(int index)
        {
            var left = _Margin;
            var span = Math.Max(0, this.SegmentsRight - left);
            var each = span / this.SegmentCount;
            return new(left + (index * each), 2, each, this.HeaderHeight - 4);
        }
    }

    /// <summary>The (dx, dy) unit direction of position <paramref name="index"/> (0–59), measured
    /// clockwise from the top, from the shared sine/cosine tables.</summary>
    private static (double Dx, double Dy) Direction(int index)
    {
        var sin = _sin ??= BuildSin();
        var cos = _cos ??= BuildCos();
        return (sin[index], -cos[index]);
    }

    /// <summary>The position index (0–59) the current value occupies on the active ring.</summary>
    private int ActiveIndex() => _stage switch
    {
        ClockFaceStage.Hour => (_value.Hours % 12) * 5,
        ClockFaceStage.Minute => _value.Minutes,
        _ => _value.Seconds,
    };

    /// <summary>Recomputes the cached hand endpoint when the value, stage or size moved.</summary>
    private void EnsureGeometry(in Layout layout)
    {
        var size = new Size(layout.Width, layout.Height);
        if (_geometrySize == size && _geometryValue == _value && _geometryStage == _stage)
            return;

        _geometrySize = size;
        _geometryValue = _value;
        _geometryStage = _stage;

        var radius = _stage == ClockFaceStage.Hour && this.Use24HourClock && _value.Hours < 12
            ? layout.InnerRadius
            : layout.OuterRadius;
        var (dx, dy) = Direction(this.ActiveIndex());
        _handRadius = radius;
        _handTip = new(layout.CenterX + (int)Math.Round(dx * radius), layout.CenterY + (int)Math.Round(dy * radius));
    }

    /// <summary>Forces the hand endpoint to be recomputed on the next paint.</summary>
    private void InvalidateGeometry() => _geometryStage = (ClockFaceStage)(-1);

    // --- Painting ----------------------------------------------------------------------------------

    /// <summary>Paints the whole dial into <paramref name="size"/> with <paramref name="theme"/>: the
    /// header readout and AM/PM toggle, the number ring(s), the hand and hub, and the OK affordance.</summary>
    public void Paint(IGraphics g, ITheme theme, Size size)
    {
        g.FillRectangle(theme.FieldBackground, new(0, 0, size.Width, size.Height));

        var layout = new Layout(theme, size, this.Use24HourClock, this.ShowSeconds);
        this.EnsureGeometry(layout);
        var font = theme.DefaultFont;

        this.PaintHeader(g, theme, layout, font);

        if (layout.OuterRadius <= 0)
        {
            g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
            return;
        }

        // The dial well and the hand, drawn before the numbers so a number sits on top of its selector.
        g.FillEllipse(theme.ControlBackground, this.Circle(layout.CenterX, layout.CenterY, layout.OuterRadius + layout.NumberRadius));
        g.DrawLine(theme.Accent, layout.CenterX, layout.CenterY, _handTip.X, _handTip.Y, 2);
        g.FillEllipse(theme.Accent, this.Circle(_handTip.X, _handTip.Y, layout.NumberRadius));
        g.FillEllipse(theme.Accent, this.Circle(layout.CenterX, layout.CenterY, 4));

        this.PaintRing(g, theme, layout, font);

        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
    }

    /// <summary>Paints the header: the hh:mm(:ss) readout with the active part accented, and the AM/PM
    /// toggle on a 12-hour dial.</summary>
    private void PaintHeader(IGraphics g, ITheme theme, in Layout layout, Font font)
    {
        var digits = _twoDigits ??= BuildTwoDigits();
        for (var i = 0; i < layout.SegmentCount; ++i)
        {
            var stage = (ClockFaceStage)i;
            var rect = layout.Segment(i);
            var active = stage == _stage;
            if (active)
                GlyphRenderer.FillSelection(g, theme, rect);

            var text = stage switch
            {
                ClockFaceStage.Hour => digits[this.DisplayHour()],
                ClockFaceStage.Minute => digits[_value.Minutes],
                _ => digits[_value.Seconds],
            };
            g.DrawText(text, font, active ? theme.SelectionText : theme.ControlText, rect, ContentAlignment.MiddleCenter);
        }

        if (this.Use24HourClock)
            return;

        var meridiem = layout.Meridiem;
        var pm = _value.Hours >= 12;
        var amRect = new Rectangle(meridiem.X, meridiem.Y, meridiem.Width / 2, meridiem.Height);
        var pmRect = new Rectangle(meridiem.X + (meridiem.Width / 2), meridiem.Y, meridiem.Width - (meridiem.Width / 2), meridiem.Height);
        if (!pm)
            GlyphRenderer.FillSelection(g, theme, amRect);
        else
            GlyphRenderer.FillSelection(g, theme, pmRect);

        g.DrawText("AM", font, !pm ? theme.SelectionText : theme.ControlText, amRect, ContentAlignment.MiddleCenter);
        g.DrawText("PM", font, pm ? theme.SelectionText : theme.ControlText, pmRect, ContentAlignment.MiddleCenter);
    }

    /// <summary>Paints the twelve (or twenty-four) ring numbers, the selected one in the selection
    /// colour on top of the accent selector already drawn.</summary>
    private void PaintRing(IGraphics g, ITheme theme, in Layout layout, Font font)
    {
        var active = this.ActiveIndex();
        if (_stage != ClockFaceStage.Hour)
        {
            var labels = _fives ??= BuildFives();
            for (var i = 0; i < 12; ++i)
                this.PaintNumber(g, theme, layout, font, labels[i], i * 5, layout.OuterRadius, active);

            return;
        }

        if (this.Use24HourClock)
        {
            var outer = _hoursOuter ??= BuildHoursOuter();
            var inner = _twoDigits ??= BuildTwoDigits();
            for (var i = 0; i < 12; ++i)
            {
                this.PaintNumber(g, theme, layout, font, outer[i], i * 5, layout.OuterRadius, active);
                this.PaintNumber(g, theme, layout, font, inner[i], i * 5, layout.InnerRadius, active);
            }

            return;
        }

        var hours = _hours12 ??= BuildHours12();
        for (var i = 0; i < 12; ++i)
            this.PaintNumber(g, theme, layout, font, hours[i], i * 5, layout.OuterRadius, active);
    }

    /// <summary>Paints one ring number centred at its position, in the selection colour when it is the
    /// hand's target on this ring.</summary>
    private void PaintNumber(IGraphics g, ITheme theme, in Layout layout, Font font, string text, int index, int radius, int activeIndex)
    {
        var (dx, dy) = Direction(index);
        var px = layout.CenterX + (int)Math.Round(dx * radius);
        var py = layout.CenterY + (int)Math.Round(dy * radius);
        var selected = index == activeIndex && radius == _handRadius;
        var color = selected ? theme.SelectionText : theme.ControlText;
        g.DrawText(text, font, color, new(px - layout.NumberRadius, py - layout.NumberRadius, 2 * layout.NumberRadius, 2 * layout.NumberRadius), ContentAlignment.MiddleCenter);
    }

    /// <summary>A square whose inscribed circle is centred at (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    private Rectangle Circle(int cx, int cy, int radius) => new(cx - radius, cy - radius, 2 * radius, 2 * radius);

    /// <summary>The hour as the header shows it: 0–23, or 1–12 on a 12-hour dial.</summary>
    private int DisplayHour()
    {
        if (this.Use24HourClock)
            return _value.Hours;

        var hour = _value.Hours % 12;
        return hour == 0 ? 12 : hour;
    }

    // --- Input -------------------------------------------------------------------------------------

    /// <summary>Reacts to a press: the OK affordance commits, the AM/PM toggle flips the half day, a
    /// header segment switches the stage, and a press on the dial begins setting the active part.</summary>
    public void HandleMouseDown(ITheme theme, Size size, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var layout = new Layout(theme, size, this.Use24HourClock, this.ShowSeconds);
        _pressedOnDial = false;

        if (layout.OkButton.Contains(e.Location))
        {
            this.Committed?.Invoke();
            return;
        }

        if (!this.Use24HourClock && layout.Meridiem.Contains(e.Location))
        {
            this.FlipMeridiem();
            return;
        }

        if (e.Y < layout.HeaderHeight)
        {
            for (var i = 0; i < layout.SegmentCount; ++i)
                if (layout.Segment(i).Contains(e.Location))
                {
                    this.Stage = (ClockFaceStage)i;
                    return;
                }

            return;
        }

        _dragging = true;
        _pressedOnDial = true;
        this.SetFromPoint(layout, e.X, e.Y);
    }

    /// <summary>Extends an in-flight drag: the hand follows the pointer onto the nearest value.</summary>
    public void HandleMouseMove(ITheme theme, Size size, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var layout = new Layout(theme, size, this.Use24HourClock, this.ShowSeconds);
        this.SetFromPoint(layout, e.X, e.Y);
    }

    /// <summary>Ends a dial gesture, advancing the hour stage to the minute (and the minute to the
    /// seconds when shown), the way the material picker walks the user forward.</summary>
    public void HandleMouseUp(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        if (!_pressedOnDial)
            return;

        _pressedOnDial = false;
        switch (_stage)
        {
            case ClockFaceStage.Hour:
                this.Stage = ClockFaceStage.Minute;
                break;

            case ClockFaceStage.Minute when this.ShowSeconds:
                this.Stage = ClockFaceStage.Second;
                break;
        }
    }

    /// <summary>Nudges the active part one unit per wheel notch — up steps forward, down back.</summary>
    public void HandleMouseWheel(int delta)
    {
        if (delta != 0)
            this.Nudge(delta > 0 ? +1 : -1);
    }

    /// <summary>Keyboard: arrows nudge the active hand, Tab cycles the stage, Enter advances then
    /// commits on the last stage, Escape cancels.</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down:
                this.Nudge(-1);
                e.Handled = true;
                break;

            case Keys.Right or Keys.Up:
                this.Nudge(+1);
                e.Handled = true;
                break;

            case Keys.Tab:
                this.Stage = this.NextStage();
                e.Handled = true;
                break;

            case Keys.Enter:
                if (_stage == this.FinalStage)
                    this.Committed?.Invoke();
                else
                    this.Stage = this.NextStage();

                e.Handled = true;
                break;

            case Keys.Escape:
                this.Cancelled?.Invoke();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The stage after the current one, skipping the hidden seconds and wrapping to the hour.</summary>
    private ClockFaceStage NextStage() => _stage switch
    {
        ClockFaceStage.Hour => ClockFaceStage.Minute,
        ClockFaceStage.Minute when this.ShowSeconds => ClockFaceStage.Second,
        _ => ClockFaceStage.Hour,
    };

    /// <summary>Steps the active part by <paramref name="direction"/> units, wrapping within the part.</summary>
    private void Nudge(int direction)
    {
        var h = _value.Hours;
        var m = _value.Minutes;
        var s = _value.Seconds;
        switch (_stage)
        {
            case ClockFaceStage.Hour:
                h = Wrap(h + direction, 24);
                break;

            case ClockFaceStage.Minute:
                m = Wrap(m + direction, 60);
                break;

            default:
                s = Wrap(s + direction, 60);
                break;
        }

        this.Value = new(h, m, s);
    }

    /// <summary>Flips the half day of a 12-hour dial, keeping the shown hour digits.</summary>
    private void FlipMeridiem() => this.Value = new(Wrap(_value.Hours + 12, 24), _value.Minutes, _value.Seconds);

    /// <summary>Sets the active part from a dial point: the nearest number on the hour ring (choosing
    /// the inner/outer 24-hour ring by distance), or the nearest single unit on the minute/seconds ring.</summary>
    private void SetFromPoint(in Layout layout, int x, int y)
    {
        var dx = x - layout.CenterX;
        var dy = y - layout.CenterY;

        // Angle clockwise from the top, in [0, 360).
        var degrees = (Math.Atan2(dx, -dy) * 180.0 / Math.PI) + 360.0;

        if (_stage == ClockFaceStage.Hour)
        {
            var slot = ((int)Math.Round(degrees / 30.0)) % 12;
            if (this.Use24HourClock)
            {
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var inner = distance < (layout.InnerRadius + layout.OuterRadius) / 2.0;
                var hour = inner ? slot : slot + 12;
                this.Value = new(hour, _value.Minutes, _value.Seconds);
            }
            else
            {
                var pm = _value.Hours >= 12;
                var hour = pm ? (slot == 0 ? 12 : slot + 12) : slot; // slot 0 is 12: 0h AM, 12h PM
                if (!pm && slot == 0)
                    hour = 0;

                this.Value = new(hour, _value.Minutes, _value.Seconds);
            }

            return;
        }

        var unit = ((int)Math.Round(degrees / 6.0)) % 60;
        this.Value = _stage == ClockFaceStage.Minute
            ? new(_value.Hours, unit, _value.Seconds)
            : new(_value.Hours, _value.Minutes, unit);
    }

    /// <summary>Requests a repaint on whichever surface hosts the dial — its own canvas when realized,
    /// the host popup through <see cref="Invalidated"/> when not.</summary>
    private void Repaint()
    {
        this.Invalidate();
        this.Invalidated?.Invoke();
    }

    private static int Wrap(int value, int period) => ((value % period) + period) % period;

    private static TimeSpan TruncateToSecond(TimeSpan value) => new(value.Hours, value.Minutes, value.Seconds);

    /// <summary>Clamps a time of day to whole seconds inside a single day.</summary>
    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;

        var day = new TimeSpan(23, 59, 59);
        return value > day ? day : TruncateToSecond(value);
    }

    // --- String and trigonometry tables ------------------------------------------------------------

    private static string[] BuildTwoDigits()
    {
        var values = new string[60];
        for (var i = 0; i < values.Length; ++i)
            values[i] = i.ToString("00", CultureInfo.InvariantCulture);

        return values;
    }

    private static string[] BuildHours12()
    {
        var values = new string[12];
        values[0] = "12";
        for (var i = 1; i < 12; ++i)
            values[i] = i.ToString(CultureInfo.InvariantCulture);

        return values;
    }

    private static string[] BuildHoursOuter()
    {
        var values = new string[12];
        for (var i = 0; i < 12; ++i)
            values[i] = (i + 12).ToString(CultureInfo.InvariantCulture);

        return values;
    }

    private static string[] BuildFives()
    {
        var values = new string[12];
        for (var i = 0; i < 12; ++i)
            values[i] = (i * 5).ToString("00", CultureInfo.InvariantCulture);

        return values;
    }

    private static double[] BuildSin()
    {
        var values = new double[60];
        for (var i = 0; i < 60; ++i)
            values[i] = Math.Sin(i * 6.0 * Math.PI / 180.0);

        return values;
    }

    private static double[] BuildCos()
    {
        var values = new double[60];
        for (var i = 0; i < 60; ++i)
            values[i] = Math.Cos(i * 6.0 * Math.PI / 180.0);

        return values;
    }
}
