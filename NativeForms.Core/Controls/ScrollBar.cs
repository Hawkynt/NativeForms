using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The owner-drawn scrollbar engine behind <see cref="HScrollBar"/> and <see cref="VScrollBar"/>:
/// themed arrows at both ends (with press-and-hold autorepeat), a thumb sized proportionally to
/// <see cref="LargeChange"/> over the range, thumb-drag scrubbing and channel-click paging. Geometry
/// and painting live in <see cref="ScrollBarRenderer"/>, shared with future scrolling hosts.
/// </summary>
/// <remarks>
/// Like its Win32 namesake, the highest value the user can scroll to is
/// <c>Maximum - LargeChange + 1</c>; <see cref="Value"/> is clamped to that scrollable range.
/// <see cref="Scroll"/> fires for user gestures only (with the gesture type); <see cref="ValueChanged"/>
/// fires for every value change, user or programmatic.
/// </remarks>
public abstract class ScrollBar : OwnerDrawnControl
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private ScrollBarPart _pressed;
    private bool _dragging;
    private int _dragOffset;
    private AutoRepeat? _autoRepeat;

    /// <summary>Whether the bar runs top-to-bottom rather than left-to-right.</summary>
    private protected abstract bool IsVertical { get; }

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

    /// <summary>The step an arrow click scrolls by. At least 1.</summary>
    public int SmallChange
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 1;

    /// <summary>The page a channel click scrolls by; also the thumb's share of the range. At least 1.</summary>
    public int LargeChange
    {
        get => field;
        set
        {
            value = Math.Max(1, value);
            if (field == value)
                return;

            field = value;
            this.Value = _value;
            this.Invalidate();
        }
    } = 10;

    /// <summary>The current scroll position, clamped to [<see cref="Minimum"/>,
    /// <c>Maximum - LargeChange + 1</c>].</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, this.MaximumValue);
            if (_value == clamped)
                return;

            _value = clamped;
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised for every user scroll gesture, carrying the gesture type.</summary>
    public event EventHandler<ScrollEventArgs>? Scroll;

    /// <summary>Raised when <see cref="Value"/> changes, by user gesture or assignment.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="Scroll"/>.</summary>
    protected virtual void OnScroll(ScrollEventArgs e) => this.Scroll?.Invoke(this, e);

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <summary>The highest value the user can scroll to.</summary>
    private int MaximumValue => ScrollBarRenderer.MaximumValue(_minimum, _maximum, this.LargeChange);

    /// <summary>The bar's client rectangle.</summary>
    private Rectangle ClientRect => new(0, 0, this.Width, this.Height);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
        => ScrollBarRenderer.Paint(
            e.Graphics, this.Theme, this.ClientRect, this.IsVertical,
            _minimum, _maximum, _value, this.LargeChange,
            _dragging ? ScrollBarPart.Thumb : _pressed);

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var bounds = this.ClientRect;
        var part = ScrollBarRenderer.HitTest(bounds, this.IsVertical, _minimum, _maximum, _value, this.LargeChange, e.Location);
        switch (part)
        {
            case ScrollBarPart.DecreaseArrow:
            case ScrollBarPart.IncreaseArrow:
                _pressed = part;
                this.StepPressedArrow();
                this.StartAutoRepeat();
                this.Invalidate();
                break;

            case ScrollBarPart.Thumb:
                var thumb = ScrollBarRenderer.ThumbRect(bounds, this.IsVertical, _minimum, _maximum, _value, this.LargeChange);
                _dragging = true;
                _dragOffset = this.AxisOf(e) - (this.IsVertical ? thumb.Y : thumb.X);
                this.Invalidate();
                break;

            case ScrollBarPart.DecreaseChannel:
                this.ScrollBy(-this.LargeChange, ScrollEventType.LargeDecrement);
                break;

            case ScrollBarPart.IncreaseChannel:
                this.ScrollBy(this.LargeChange, ScrollEventType.LargeIncrement);
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var bounds = this.ClientRect;
        var track = ScrollBarRenderer.TrackRect(bounds, this.IsVertical);
        var offset = this.AxisOf(e) - _dragOffset - (this.IsVertical ? track.Y : track.X);
        this.SetValue(
            ScrollBarRenderer.ValueFromThumbOffset(bounds, this.IsVertical, _minimum, _maximum, this.LargeChange, offset),
            ScrollEventType.ThumbTrack);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            this.RaiseScroll(ScrollEventType.EndScroll);
            this.Invalidate();
        }

        this.ReleaseArrow();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e) => this.ReleaseArrow();

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _pressed = ScrollBarPart.None;
        _dragging = false;
        _autoRepeat?.Dispose();
        _autoRepeat = null;
    }

    /// <summary>Arms the press-and-hold repeat for the currently pressed arrow.</summary>
    private void StartAutoRepeat()
    {
        var backend = this.Backend;
        if (backend is null)
            return;

        _autoRepeat ??= new(this.StepPressedArrow);
        _autoRepeat.Start(backend);
    }

    /// <summary>Steps once in the pressed arrow's direction; the autorepeat tick action.</summary>
    private void StepPressedArrow()
    {
        if (_pressed == ScrollBarPart.DecreaseArrow)
            this.ScrollBy(-this.SmallChange, ScrollEventType.SmallDecrement);
        else if (_pressed == ScrollBarPart.IncreaseArrow)
            this.ScrollBy(this.SmallChange, ScrollEventType.SmallIncrement);
    }

    /// <summary>Releases a pressed arrow button and stops its autorepeat.</summary>
    private void ReleaseArrow()
    {
        if (_pressed == ScrollBarPart.None)
            return;

        _pressed = ScrollBarPart.None;
        _autoRepeat?.Stop();
        this.Invalidate();
    }

    /// <summary>Scrolls by <paramref name="delta"/> as the given gesture.</summary>
    private void ScrollBy(int delta, ScrollEventType type) => this.SetValue(_value + delta, type);

    /// <summary>Applies a user-gestured value: clamps, repaints, and raises <see cref="Scroll"/>
    /// then <see cref="ValueChanged"/> — only when the value actually moved.</summary>
    private void SetValue(int value, ScrollEventType type)
    {
        var clamped = Math.Clamp(value, _minimum, this.MaximumValue);
        if (_value == clamped)
            return;

        _value = clamped;
        this.Invalidate();
        this.RaiseScroll(type);
        this.OnValueChanged(EventArgs.Empty);
    }

    /// <summary>Raises <see cref="Scroll"/> without allocating when nobody listens.</summary>
    private void RaiseScroll(ScrollEventType type)
    {
        if (this.Scroll is not null)
            this.OnScroll(new(type, _value));
    }

    /// <summary>The event coordinate along the bar's axis.</summary>
    private int AxisOf(MouseEventArgs e) => this.IsVertical ? e.Y : e.X;
}
