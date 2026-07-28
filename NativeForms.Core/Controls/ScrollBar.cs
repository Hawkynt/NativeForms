using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

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

            this.PushNativeRange();
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

            this.PushNativeRange();
            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The step an arrow click scrolls by. At least 1.</summary>
    public int SmallChange
    {
        get => field;
        set
        {
            field = Math.Max(1, value);
            this.PushNativeRange();
        }
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
            this.PushNativeRange();
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
            _native?.SetValue(clamped);
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised for every user scroll gesture, carrying the gesture type.</summary>
    public event EventHandler<ScrollEventArgs>? Scroll;

    /// <summary>Raised when <see cref="Value"/> changes, by user gesture or assignment.</summary>
    public event EventHandler? ValueChanged;

    private IScrollBarPeer? _native;
    private bool? _nativeOffered;


    /// <summary>Whether this bar is currently rendered by a real platform widget.</summary>
    public override bool IsNativeWidget => _native is not null;

    /// <summary>
    /// Whether the current property values are all expressible by a platform scroll bar. Everything this
    /// control models is: both platforms carry the same range/page/step quartet, and both report which
    /// gesture moved the thumb.
    /// </summary>
    private static bool IsNativeEligible => true;

    /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
    private bool WouldBeNative
        => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && IsNativeEligible && (_nativeOffered ?? true);

    /// <inheritdoc/>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && IsNativeEligible)
        {
            var offered = backend.CreateScrollBar(this.IsVertical);
            _nativeOffered = offered is not null;
            if (offered is { } peer)
            {
                _native = peer;
                peer.SetRange(_minimum, _maximum, this.LargeChange, this.SmallChange);
                peer.SetValue(_value);
                peer.Scrolled += this.OnNativeScrolled;
                return peer;
            }
        }

        return base.CreatePeer(backend);
    }

    /// <summary>Pushes the whole range quartet, which the platforms take as one unit.</summary>
    private void PushNativeRange() => _native?.SetRange(_minimum, _maximum, this.LargeChange, this.SmallChange);

    /// <summary>
    /// The widget scrolled. Reading the position back rather than stepping locally keeps the two paths
    /// identical when the platform clamps differently at the ends, and routes through the same
    /// user-gesture path the owner-drawn bar uses, so <see cref="Scroll"/> precedes
    /// <see cref="ValueChanged"/> either way.
    /// </summary>
    private void OnNativeScrolled(object? sender, ScrollEventType type)
    {
        if (_native is not { } peer)
            return;

        if (type == ScrollEventType.EndScroll)
        {
            this.RaiseScroll(type);
            return;
        }

        this.SetValue(peer.GetValue(), type);
    }

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
        if (_native is { } peer)
        {
            peer.Scrolled -= this.OnNativeScrolled;
            _native = null;
        }

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
