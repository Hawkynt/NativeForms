using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A two-thumb horizontal slider: a lower and an upper value over [<see cref="Minimum"/>,
/// <see cref="Maximum"/>], with the span between them filled in the accent. Each thumb drags
/// independently (never crossing the other), a track click moves the nearer thumb, and the arrow keys
/// nudge the last-touched thumb — every gesture raising <see cref="RangeChanged"/>. Media trim, filter
/// ranges and level endpoints. Owner-drawn and native-themed.
/// </summary>
public class RangeSlider : OwnerDrawnControl
{
    private const int _EndMargin = 8;
    private const int _ThumbLength = 10;
    private const int _GrooveThickness = 4;

    private int _minimum;
    private int _maximum = 100;
    private int _lower;
    private int _upper = 100;
    private int _dragThumb = -1; // 0 = lower, 1 = upper, -1 = none
    private int _dragOffset;
    private int _activeThumb; // the thumb the keyboard drives

    /// <summary>The value at the start of the track.</summary>
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; this.ClampValues(); this.Invalidate(); }
    }

    /// <summary>The value at the end of the track.</summary>
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; if (_minimum > _maximum) _minimum = _maximum; this.ClampValues(); this.Invalidate(); }
    }

    /// <summary>The lower thumb's value, clamped to [<see cref="Minimum"/>, <see cref="UpperValue"/>].</summary>
    public int LowerValue
    {
        get => _lower;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _upper);
            if (_lower == clamped)
                return;

            _lower = clamped;
            this.Invalidate();
            this.OnRangeChanged(EventArgs.Empty);
        }
    }

    /// <summary>The upper thumb's value, clamped to [<see cref="LowerValue"/>, <see cref="Maximum"/>].</summary>
    public int UpperValue
    {
        get => _upper;
        set
        {
            var clamped = Math.Clamp(value, _lower, _maximum);
            if (_upper == clamped)
                return;

            _upper = clamped;
            this.Invalidate();
            this.OnRangeChanged(EventArgs.Empty);
        }
    }

    /// <summary>The step an arrow key nudges the active thumb by. At least 1.</summary>
    public int SmallChange
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 1;

    /// <summary>Raised when either <see cref="LowerValue"/> or <see cref="UpperValue"/> changes.</summary>
    public event EventHandler? RangeChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="RangeChanged"/>.</summary>
    protected virtual void OnRangeChanged(EventArgs e) => this.RangeChanged?.Invoke(this, e);

    private void ClampValues()
    {
        _upper = Math.Clamp(_upper, _minimum, _maximum);
        _lower = Math.Clamp(_lower, _minimum, _upper);
    }

    private int TrackLength => Math.Max(0, this.Width - (2 * _EndMargin));

    private int PositionOf(int value)
    {
        var range = _maximum - _minimum;
        return range > 0 ? (int)((long)this.TrackLength * (value - _minimum) / range) : 0;
    }

    private int ValueAt(int x)
    {
        var trackLength = this.TrackLength;
        var range = _maximum - _minimum;
        if (trackLength <= 0 || range <= 0)
            return _minimum;

        var value = _minimum + (int)((((long)(x - _EndMargin) * range) + (trackLength / 2)) / trackLength);
        return Math.Clamp(value, _minimum, _maximum);
    }

    private int CenterOf(int value) => _EndMargin + this.PositionOf(value);

    private Rectangle ThumbRect(int value)
    {
        var breadth = Math.Max(8, this.Height - 10);
        return new Rectangle(this.CenterOf(value) - (_ThumbLength / 2), (this.Height - breadth) / 2, _ThumbLength, breadth);
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

        var grooveY = (this.Height / 2) - (_GrooveThickness / 2);
        var groove = new Rectangle(_EndMargin, grooveY, trackLength, _GrooveThickness);
        g.FillRectangle(theme.FieldBackground, groove);
        g.DrawRectangle(theme.Border, groove);

        // The selected span between the two thumbs.
        var lowX = this.CenterOf(_lower);
        var highX = this.CenterOf(_upper);
        if (highX > lowX)
            g.FillRectangle(this.Enabled ? theme.Accent : theme.Border, new Rectangle(lowX, grooveY, highX - lowX, _GrooveThickness));

        foreach (var value in new[] { _lower, _upper })
        {
            var thumb = this.ThumbRect(value);
            g.FillRectangle(this.Enabled ? theme.Accent : theme.Border, thumb);
            g.DrawRectangle(theme.Border, thumb);
        }

        if (this.Focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(2, 2, this.Width - 5, this.Height - 5));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.ThumbRect(_lower).Contains(e.Location))
        {
            this.BeginDrag(0, e.X);
            return;
        }

        if (this.ThumbRect(_upper).Contains(e.Location))
        {
            this.BeginDrag(1, e.X);
            return;
        }

        // A track click moves whichever thumb is nearer the click toward it.
        var value = this.ValueAt(e.X);
        var thumb = Math.Abs(value - _lower) <= Math.Abs(value - _upper) ? 0 : 1;
        this.BeginDrag(thumb, this.CenterOf(thumb == 0 ? _lower : _upper));
        this.DragTo(e.X);
    }

    private void BeginDrag(int thumb, int grabX)
    {
        _dragThumb = thumb;
        _activeThumb = thumb;
        _dragOffset = grabX - this.CenterOf(thumb == 0 ? _lower : _upper);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragThumb >= 0)
            this.DragTo(e.X);
    }

    private void DragTo(int x)
    {
        var value = this.ValueAt(x - _dragOffset);
        if (_dragThumb == 0)
            this.LowerValue = value;
        else
            this.UpperValue = value;
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _dragThumb = -1;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var delta = e.KeyCode switch
        {
            Keys.Left or Keys.Down => -this.SmallChange,
            Keys.Right or Keys.Up => this.SmallChange,
            _ => 0,
        };

        if (delta == 0)
            return;

        if (_activeThumb == 0)
            this.LowerValue = _lower + delta;
        else
            this.UpperValue = _upper + delta;

        e.Handled = true;
    }
}
