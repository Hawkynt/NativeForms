using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A scroll bar: a real <c>NSScroller</c> standing on its own, with the toolkit's range projected onto
/// the only two numbers it holds.
/// </summary>
/// <remarks>
/// <para>
/// This is the promotion that had to be argued for rather than simply wired. An <c>NSScroller</c> has
/// no range model at all: it knows a knob position between nothing and everything, and how much of the
/// track the knob covers. Windows Forms' minimum, maximum, large change and small change are all
/// arithmetic on top of that, so the peer keeps them and does the arithmetic. Nothing is lost by it —
/// the reachable maximum is <c>maximum - largeChange + 1</c> either way, and a widget that only ever
/// reports a fraction cannot round-trip an integer wrongly, because the integer is recomputed from the
/// fraction against the same range that produced it.
/// </para>
/// <para>
/// Legacy style, deliberately. A modern overlay scroller fades out when nothing is scrolling, which is
/// right for a scroller inside a scroll view and wrong for one an application placed as a control:
/// what the toolkit models here is a visible widget with a position.
/// </para>
/// <para>
/// The orientation is fixed when the object is made — <c>NSScroller</c> reads it off the frame it is
/// initialized with and never revisits it — so the first frame carries the shape rather than the 1×1
/// placeholder every other peer starts from.
/// </para>
/// </remarks>
internal sealed class CocoaScrollBarPeer : CocoaControlPeer, IScrollBarPeer
{
    /// <summary>NSScrollerPart: no part 0, decrement page 1, knob 2, increment page 3, and the lines.</summary>
    private const nint _DecrementPage = 1;
    private const nint _Knob = 2;
    private const nint _IncrementPage = 3;
    private const nint _DecrementLine = 4;
    private const nint _IncrementLine = 5;

    private readonly nint _target;

    private int _minimum;
    private int _maximum = 100;
    private int _largeChange = 10;
    private int _smallChange = 1;
    private int _value;

    public CocoaScrollBarPeer(bool vertical)
        : base(Create(vertical))
    {
        if (this.Handle == 0)
            return;

        _target = CocoaAction.Create(this.OnScrolled);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    /// <inheritdoc/>
    public event EventHandler<ScrollEventType>? Scrolled;

    private static nint Create(bool vertical)
    {
        var allocated = CocoaRuntime.Allocate("NSScroller");
        var scroller = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(
                allocated,
                CocoaRuntime.sel_registerName("initWithFrame:"),
                vertical ? new(0, 0, 15, 100) : new(0, 0, 100, 15));

        if (scroller == 0)
            return 0;

        // NSScrollerStyleLegacy, and enabled: a scroller with nothing to scroll draws itself dead.
        CocoaRuntime.SendVoid(scroller, CocoaRuntime.sel_registerName("setScrollerStyle:"), 0);
        CocoaRuntime.SendVoid(scroller, CocoaRuntime.sel_registerName("setEnabled:"), true);
        return scroller;
    }

    /// <summary>How far the value can actually travel, which is never the whole range.</summary>
    private int Reach => CalculateReach(_minimum, _maximum, _largeChange);

    /// <summary>
    /// Calculates the distance between the minimum and the effective maximum. A range smaller than
    /// one page has no travel at all; forcing a positive denominator there would manufacture a value
    /// above <paramref name="maximum"/>.
    /// </summary>
    internal static int CalculateReach(int minimum, int maximum, int largeChange)
        => Math.Max(0, maximum - Math.Max(1, largeChange) + 1 - minimum);

    /// <inheritdoc/>
    /// <remarks>A caption on a scroll bar is meaningless, and an <c>NSScroller</c> answers no
    /// <c>setStringValue:</c> — an unrecognized selector here ends the process.</remarks>
    public override void SetText(string text) { }

    /// <inheritdoc/>
    public void SetRange(int minimum, int maximum, int largeChange, int smallChange)
    {
        _minimum = minimum;
        _maximum = maximum;
        _largeChange = Math.Max(1, largeChange);
        _smallChange = Math.Max(1, smallChange);
        _value = Math.Clamp(_value, _minimum, _minimum + this.Reach);
        this.Push();
    }

    /// <inheritdoc/>
    public void SetValue(int value)
    {
        _value = Math.Clamp(value, _minimum, _minimum + this.Reach);
        this.Push();
    }

    /// <inheritdoc/>
    public int GetValue() => _value;

    /// <summary>Writes the remembered range and position out as the two fractions the widget holds.</summary>
    private void Push()
    {
        if (this.Handle == 0)
            return;

        var span = Math.Max(1, _maximum - _minimum + 1);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setKnobProportion:"), Math.Clamp(_largeChange / (double)span, 0.0, 1.0));
        var position = this.Reach == 0 ? 0.0 : (_value - _minimum) / (double)this.Reach;
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setDoubleValue:"), Math.Clamp(position, 0.0, 1.0));
    }

    /// <summary>
    /// The user worked the scroller. Which gesture it was comes from the part that was hit, and what it
    /// means for the value comes from the range the widget does not hold.
    /// </summary>
    private void OnScrolled()
    {
        if (this.Handle == 0)
            return;

        var part = CocoaRuntime.SendInteger(this.Handle, CocoaRuntime.sel_registerName("hitPart"));
        var (moved, gesture) = part switch
        {
            _Knob => (
                _minimum + (int)Math.Round(CocoaRuntime.SendDouble(this.Handle, CocoaRuntime.sel_registerName("doubleValue")) * this.Reach),
                ScrollEventType.ThumbTrack),
            _DecrementPage => (_value - _largeChange, ScrollEventType.LargeDecrement),
            _IncrementPage => (_value + _largeChange, ScrollEventType.LargeIncrement),
            _DecrementLine => (_value - _smallChange, ScrollEventType.SmallDecrement),
            _IncrementLine => (_value + _smallChange, ScrollEventType.SmallIncrement),
            _ => (_value, ScrollEventType.EndScroll),
        };

        _value = Math.Clamp(moved, _minimum, _minimum + this.Reach);
        this.Push();
        Scrolled?.Invoke(this, gesture);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        CocoaAction.Forget(_target);
        base.Dispose();
    }
}

/// <summary>A slider: a real <c>NSSlider</c>, which is what AppKit has where Win32 has a trackbar.</summary>
/// <remarks>
/// The one part of the contract this widget cannot serve is the step sizes. AppKit has no small and
/// large change: an arrow key moves an <c>NSSlider</c> by a hundredth of its range, or from tick to
/// tick when it has tick marks, and neither is a number a caller sets. It could be faked by giving the
/// slider as many tick marks as the range has steps, but tick marks are drawn — the slider would grow a
/// row of notches nobody asked for, and the control models tick frequency separately. So that one call
/// is refused and said out loud rather than half-answered.
/// </remarks>
internal sealed class CocoaTrackBarPeer : CocoaControlPeer, ITrackBarPeer
{
    private readonly nint _target;

    public CocoaTrackBarPeer(bool vertical)
        : base(Create(vertical))
    {
        if (this.Handle == 0)
            return;

        _target = CocoaAction.Create(this.OnValueChanged);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    /// <inheritdoc/>
    public event EventHandler? ValueChanged;

    private static nint Create(bool vertical)
    {
        var allocated = CocoaRuntime.Allocate("NSSlider");
        var slider = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(
                allocated,
                CocoaRuntime.sel_registerName("initWithFrame:"),
                vertical ? new(0, 0, 20, 100) : new(0, 0, 100, 20));

        if (slider == 0)
            return 0;

        // A slider takes its orientation from the frame it is made with; the explicit setter arrived
        // later and is asked for only where it exists, because an unrecognized selector ends the
        // process rather than being ignored.
        var setVertical = CocoaRuntime.sel_registerName("setVertical:");
        if (CocoaRuntime.SendBool(slider, CocoaRuntime.sel_registerName("respondsToSelector:"), setVertical))
            CocoaRuntime.SendVoid(slider, setVertical, vertical);

        // Continuous, so dragging reports as it moves rather than only when it is let go — which is
        // what the owner-drawn twin does and what ValueChanged is documented to mean.
        CocoaRuntime.SendVoid(slider, CocoaRuntime.sel_registerName("setContinuous:"), true);
        return slider;
    }

    /// <inheritdoc/>
    /// <remarks>A slider shows no caption; <c>setStringValue:</c> would set its value from a string.</remarks>
    public override void SetText(string text) { }

    /// <inheritdoc/>
    public void SetRange(int minimum, int maximum)
    {
        if (this.Handle == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setMinValue:"), (double)minimum);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setMaxValue:"), (double)Math.Max(minimum, maximum));
    }

    /// <inheritdoc/>
    public void SetValue(int value)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setDoubleValue:"), (double)value);
    }

    /// <inheritdoc/>
    public int GetValue()
        => this.Handle == 0
            ? 0
            : (int)Math.Round(CocoaRuntime.SendDouble(this.Handle, CocoaRuntime.sel_registerName("doubleValue")));

    /// <inheritdoc/>
    /// <inheritdoc cref="CocoaTrackBarPeer"/>
    public void SetSteps(int smallChange, int largeChange) { }

    private void OnValueChanged() => ValueChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public override void Dispose()
    {
        CocoaAction.Forget(_target);
        base.Dispose();
    }
}
