using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn progress bar: a themed track filled with the accent color, either in proportion to
/// <see cref="Value"/> (<see cref="ProgressBarStyle.Blocks"/>) or as an animated segment sweeping the
/// track (<see cref="ProgressBarStyle.Marquee"/>). Horizontal bars fill left to right, vertical ones
/// bottom to top. Purely visual — it takes no focus and handles no input.
/// </summary>
/// <remarks>
/// The marquee runs on a <see cref="Timer"/> ticking every <see cref="MarqueeAnimationSpeed"/>
/// milliseconds; each tick advances an integer phase and invalidates — nothing on the tick or paint
/// path allocates. A speed of 0 pauses the animation.
/// </remarks>
public class ProgressBar : OwnerDrawnControl
{
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.ProgressBar;

    private IProgressBarPeer? _native;

    /// <summary>Whether the backend offered a widget the last time we asked; <see langword="null"/> until
    /// we have. See <see cref="WouldBeNative"/>.</summary>
    private bool? _nativeOffered;


    /// <summary>Whether the control is currently backed by a real platform progress indicator.</summary>
    public override bool IsNativeWidget => _native is not null;

    /// <summary>
    /// Whether the configured properties stay inside what a platform progress bar expresses. A vertical
    /// bar is excluded: GTK needs the orientation set at construction and Win32 needs a distinct style, so
    /// the owner-drawn path keeps that case until the peers carry it.
    /// </summary>
    private bool IsNativeEligible => this.Orientation == Orientation.Horizontal;

    /// <summary>Which path this control would take if it were realized right now, so a property change
    /// only rebuilds the peer when that would actually change the rendering.</summary>
    private bool WouldBeNative
        => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible && (_nativeOffered ?? true);

    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _marqueePhase;
    private Timer? _marqueeTimer;

    /// <summary>The lowest value the bar can represent.</summary>
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

    /// <summary>The highest value the bar can represent.</summary>
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

    /// <summary>The current progress, clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>].</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped)
                return;

            _value = clamped;
            this.PushNativeFraction();
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>How the bar presents progress. Switching to <see cref="ProgressBarStyle.Marquee"/>
    /// starts the animation timer; switching away stops it.</summary>
    public ProgressBarStyle Style
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _native?.SetMarquee(value == ProgressBarStyle.Marquee);
            this.PushNativeFraction();
            this.UpdateMarqueeTimer();
            this.Invalidate();
        }
    }

    /// <summary>The marquee tick period in milliseconds; 0 pauses the animation.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MarqueeAnimationSpeed
    {
        get => field;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (field == value)
                return;

            field = value;
            this.UpdateMarqueeTimer();
        }
    } = 100;

    /// <summary>The amount <see cref="PerformStep"/> advances <see cref="Value"/> by.</summary>
    public int Step { get; set; } = 10;

    /// <summary>The axis the bar fills along.</summary>
    public Orientation Orientation
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;

            // A platform progress bar fixes its orientation at construction, so turning the bar moves it
            // between the widget and the canvas rather than leaving a horizontal widget drawn sideways.
            if (this.IsNativeWidget != this.WouldBeNative)
                this.RerealizePeer();

            this.Invalidate();
        }
    }

    /// <summary>Advances <see cref="Value"/> by <see cref="Step"/>, clamped at <see cref="Maximum"/>.</summary>
    public void PerformStep() => this.Value = _value + this.Step;

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    /// <remarks>
    /// The promotion point (PRD §12): a horizontal bar on a willing backend becomes a real platform
    /// progress indicator, so the desktop's own trough, fill and animation are used. Everything else falls
    /// back to the owner-drawn canvas.
    /// </remarks>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible)
        {
            var offered = backend.CreateProgressBar();
            _nativeOffered = offered is not null;
            if (offered is { } peer)
            {
                _native = peer;
                peer.SetMarquee(this.Style == ProgressBarStyle.Marquee);
                peer.SetFraction(this.Fraction);
                return peer;
            }
        }

        return base.CreatePeer(backend);
    }

    /// <summary>The filled share of the track in 0..1, shared by the drawn and the native path.</summary>
    private double Fraction
    {
        get
        {
            var range = _maximum - _minimum;
            return range <= 0 ? 0 : Math.Clamp((double)(_value - _minimum) / range, 0, 1);
        }
    }

    private void PushNativeFraction() => _native?.SetFraction(this.Fraction);

    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        this.UpdateMarqueeTimer();
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _native = null;
        _marqueeTimer?.Dispose();
        _marqueeTimer = null;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, this.Height));

        if (this.Style == ProgressBarStyle.Marquee)
            this.PaintMarquee(g, theme);
        else
            this.PaintBlocks(g, theme);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>Paints the determinate accent fill proportional to <see cref="Value"/>.</summary>
    private void PaintBlocks(IGraphics g, ITheme theme)
    {
        var range = _maximum - _minimum;
        if (range <= 0 || this.Width <= 2 || this.Height <= 2)
            return;

        if (this.Orientation == Orientation.Horizontal)
        {
            var track = this.Width - 2;
            var filled = (int)((long)track * (_value - _minimum) / range);
            if (filled > 0)
                g.FillRectangle(theme.Accent, new Rectangle(1, 1, filled, this.Height - 2));

            return;
        }

        var verticalTrack = this.Height - 2;
        var verticalFilled = (int)((long)verticalTrack * (_value - _minimum) / range);
        if (verticalFilled > 0)
            g.FillRectangle(theme.Accent, new Rectangle(1, this.Height - 1 - verticalFilled, this.Width - 2, verticalFilled));
    }

    /// <summary>Paints the marquee's accent segment at its current sweep position.</summary>
    private void PaintMarquee(IGraphics g, ITheme theme)
    {
        var track = this.MarqueeTrack;
        if (track <= 0 || this.Width <= 2 || this.Height <= 2)
            return;

        // The segment slides in from before the track and out past its end, then wraps around.
        var segment = this.MarqueeSegment;
        var position = _marqueePhase % this.MarqueePeriod - segment;
        var start = Math.Max(0, position);
        var end = Math.Min(track, position + segment);
        if (end <= start)
            return;

        g.FillRectangle(
            theme.Accent,
            this.Orientation == Orientation.Horizontal
                ? new Rectangle(1 + start, 1, end - start, this.Height - 2)
                : new Rectangle(1, this.Height - 1 - end, this.Width - 2, end - start));
    }

    /// <summary>The marquee track length along the fill axis, inside the 1px border inset.</summary>
    private int MarqueeTrack => (this.Orientation == Orientation.Horizontal ? this.Width : this.Height) - 2;

    /// <summary>The sweeping segment's length: a quarter of the track.</summary>
    private int MarqueeSegment => Math.Max(1, this.MarqueeTrack / 4);

    /// <summary>The sweep cycle length: the segment fully enters and fully leaves the track.</summary>
    private int MarqueePeriod => this.MarqueeTrack + this.MarqueeSegment;

    /// <summary>Starts, retunes or stops the marquee timer to match style, speed and realization.</summary>
    private void UpdateMarqueeTimer()
    {
        var backend = this.Backend;
        if (this.Style != ProgressBarStyle.Marquee || this.MarqueeAnimationSpeed <= 0 || backend is null)
        {
            _marqueeTimer?.Stop();
            return;
        }

        var timer = _marqueeTimer;
        if (timer is null)
        {
            _marqueeTimer = timer = new Timer(backend);
            timer.Tick += this.OnMarqueeTick;
        }

        timer.Interval = this.MarqueeAnimationSpeed;
        timer.Start();
    }

    /// <summary>Advances the sweep and repaints; allocation-free, called once per timer tick.</summary>
    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        var period = this.MarqueePeriod;
        if (period <= 0)
            return;

        _marqueePhase = (_marqueePhase + Math.Max(1, period / 50)) % period;

        // A native indicator animates from its own pulse, not from our phase; both are driven by the same
        // timer so the animation speed stays the control's property either way.
        if (_native is { } peer)
            peer.Pulse();
        else
            this.Invalidate();
    }
}
