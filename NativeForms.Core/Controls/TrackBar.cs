using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn slider: a themed groove with an accent-filled portion and thumb, plus tick marks
/// every <see cref="TickFrequency"/> values. Clicking the track pages toward the click, dragging the
/// thumb scrubs with live <see cref="ValueChanged"/> notifications, and the keyboard follows the
/// native trackbar: arrows step by <see cref="SmallChange"/> (Left/Up toward the minimum),
/// PageUp/PageDown by <see cref="LargeChange"/>, Home/End jump to the ends.
/// </summary>
public class TrackBar : OwnerDrawnControl
{
    /// <summary>Track inset at both ends, leaving room for the thumb to center over the extremes.</summary>
    private const int _EndMargin = 8;

    /// <summary>The thumb's extent along the track axis.</summary>
    private const int _ThumbLength = 10;

    /// <summary>The groove's extent across the track axis.</summary>
    private const int _GrooveThickness = 4;

    /// <summary>The length of a painted tick mark.</summary>
    private const int _TickLength = 3;

    private int _minimum;
    private int _maximum = 10;
    private int _value;
    private bool _dragging;
    private int _dragOffset;

    /// <summary>The value at the start of the track.</summary>
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
                return;

            _minimum = value;
            if (_maximum < _minimum)
                _maximum = _minimum;

            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The value at the end of the track.</summary>
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
                return;

            _maximum = value;
            if (_minimum > _maximum)
                _minimum = _maximum;

            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The current position, clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>].</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped)
                return;

            _value = clamped;
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>The step an arrow key changes the value by. At least 1.</summary>
    public int SmallChange
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 1;

    /// <summary>The step a track click or PageUp/PageDown changes the value by. At least 1.</summary>
    public int LargeChange
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 5;

    /// <summary>The value spacing between painted tick marks. At least 1.</summary>
    public int TickFrequency
    {
        get => field;
        set
        {
            value = Math.Max(1, value);
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = 1;

    /// <summary>The axis the track runs along.</summary>
    public Orientation Orientation
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

    /// <summary>Raised for every user gesture that moves the value — thumb drag, arrow and page
    /// keys, Home/End and track clicks — but never for programmatic <see cref="Value"/> writes,
    /// mirroring the <see cref="ScrollBar.Scroll"/>/<see cref="ScrollBar.ValueChanged"/> split.</summary>
    public event EventHandler? Scroll;

    /// <summary>Raised when <see cref="Value"/> changes — live while the thumb is dragged.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="Scroll"/>.</summary>
    protected virtual void OnScroll(EventArgs e) => this.Scroll?.Invoke(this, e);

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <summary>Applies a user-gestured value: clamps, repaints, and raises <see cref="Scroll"/>
    /// then <see cref="ValueChanged"/> — only when the value actually moved.</summary>
    private void SetValueFromGesture(int value)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        if (_value == clamped)
            return;

        _value = clamped;
        this.Invalidate();
        this.OnScroll(EventArgs.Empty);
        this.OnValueChanged(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Whether the track runs top to bottom.</summary>
    private bool IsVertical => this.Orientation == Orientation.Vertical;

    /// <summary>The control's extent along the track axis.</summary>
    private int AxisExtent => this.IsVertical ? this.Height : this.Width;

    /// <summary>The control's extent across the track axis.</summary>
    private int CrossExtent => this.IsVertical ? this.Width : this.Height;

    /// <summary>The track's pixel length between the end margins.</summary>
    private int TrackLength => Math.Max(0, this.AxisExtent - 2 * _EndMargin);

    /// <summary>The thumb's center along the axis for the current value.</summary>
    private int ThumbCenter => _EndMargin + this.PositionOf(_value);

    /// <summary>Maps a value to its pixel offset from the track start.</summary>
    private int PositionOf(int value)
    {
        var range = _maximum - _minimum;
        return range > 0 ? (int)((long)this.TrackLength * (value - _minimum) / range) : 0;
    }

    /// <summary>Maps an axis pixel position to the nearest value, clamped to the range.</summary>
    private int ValueAt(int position)
    {
        var trackLength = this.TrackLength;
        var range = _maximum - _minimum;
        if (trackLength <= 0 || range <= 0)
            return _minimum;

        var value = _minimum + (int)(((long)(position - _EndMargin) * range + trackLength / 2) / trackLength);
        return Math.Clamp(value, _minimum, _maximum);
    }

    /// <summary>The thumb's bounds: an accent block straddling the groove at the value's position.</summary>
    private Rectangle ThumbRect
    {
        get
        {
            var breadth = Math.Max(8, this.CrossExtent - 10);
            var axisStart = this.ThumbCenter - _ThumbLength / 2;
            var crossStart = (this.CrossExtent - breadth) / 2;
            return this.IsVertical
                ? new(crossStart, axisStart, breadth, _ThumbLength)
                : new(axisStart, crossStart, _ThumbLength, breadth);
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        var trackLength = this.TrackLength;
        if (trackLength <= 0)
            return;

        var vertical = this.IsVertical;
        var grooveCross = this.CrossExtent / 2 - _GrooveThickness / 2;
        var groove = vertical
            ? new Rectangle(grooveCross, _EndMargin, _GrooveThickness, trackLength)
            : new Rectangle(_EndMargin, grooveCross, trackLength, _GrooveThickness);
        g.FillRectangle(theme.FieldBackground, groove);
        g.DrawRectangle(theme.Border, groove);

        // The accent portion covers the range already traveled, from the track start to the thumb.
        var filled = this.ThumbCenter - _EndMargin;
        if (filled > 0)
            g.FillRectangle(
                theme.Accent,
                vertical
                    ? new Rectangle(grooveCross, _EndMargin, _GrooveThickness, filled)
                    : new Rectangle(_EndMargin, grooveCross, filled, _GrooveThickness));

        this.PaintTicks(g);

        var thumb = this.ThumbRect;
        g.FillRectangle(theme.Accent, thumb);
        g.DrawRectangle(theme.Border, thumb);
    }

    /// <summary>Paints one tick per <see cref="TickFrequency"/> step, plus one at the maximum.</summary>
    private void PaintTicks(IGraphics g)
    {
        var color = this.Theme.ControlText;
        var tickStart = this.CrossExtent - 6;
        for (var value = _minimum; value <= _maximum; value += this.TickFrequency)
            this.PaintTick(g, color, value, tickStart);

        if (_maximum > _minimum && (_maximum - _minimum) % this.TickFrequency != 0)
            this.PaintTick(g, color, _maximum, tickStart);
    }

    /// <summary>Paints a single tick mark near the far cross-axis edge.</summary>
    private void PaintTick(IGraphics g, Color color, int value, int tickStart)
    {
        var axis = _EndMargin + this.PositionOf(value);
        if (this.IsVertical)
            g.DrawLine(color, tickStart, axis, tickStart + _TickLength, axis);
        else
            g.DrawLine(color, axis, tickStart, axis, tickStart + _TickLength);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var position = this.IsVertical ? e.Y : e.X;
        if (this.ThumbRect.Contains(e.Location))
        {
            _dragging = true;
            _dragOffset = position - this.ThumbCenter;
            return;
        }

        // A click on the track pages toward the click, like the native control.
        this.SetValueFromGesture(_value + (position > this.ThumbCenter ? this.LargeChange : -this.LargeChange));
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var position = this.IsVertical ? e.Y : e.X;
        this.SetValueFromGesture(this.ValueAt(position - _dragOffset));
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _dragging = false;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Up:
                this.SetValueFromGesture(_value - this.SmallChange);
                break;

            case Keys.Right:
            case Keys.Down:
                this.SetValueFromGesture(_value + this.SmallChange);
                break;

            case Keys.PageUp:
                this.SetValueFromGesture(_value - this.LargeChange);
                break;

            case Keys.PageDown:
                this.SetValueFromGesture(_value + this.LargeChange);
                break;

            case Keys.Home:
                this.SetValueFromGesture(_minimum);
                break;

            case Keys.End:
                this.SetValueFromGesture(_maximum);
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}
