using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="TrackBar"/> — a common-controls
/// <c>msctls_trackbar32</c> (PRD §12), so the OS supplies the trough, the thumb, the keyboard handling
/// and the theme animation.
/// </summary>
/// <remarks>
/// The trackbar reports every movement — drag, arrow key, page, wheel — as a <c>WM_HSCROLL</c> or
/// <c>WM_VSCROLL</c> to its parent, which <see cref="WindowPeer"/> routes back here by HWND. The
/// notification carries no reliable position for a thumb drag in progress, so the value is always read
/// back with <c>TBM_GETPOS</c> rather than taken from <c>wParam</c>. <c>TBS_VERT</c> and tick placement
/// are creation-time styles, which is why changing either causes the core to re-realize the peer.
/// </remarks>
internal sealed class TrackBarPeer : Win32ChildPeer, ITrackBarPeer, ITrackBarTickPeer
{
    private const uint _TbsAutoTicks = 0x0001;
    private const uint _TbsTop = 0x0004;
    private const uint _TbsBoth = 0x0008;
    private const uint _TbmSetTicFrequency = 0x0414;

    private readonly bool _vertical;
    private int _minimum;
    private int _maximum = 10;
    private int _value;
    private int _smallChange = 1;
    private int _largeChange = 5;
    private int _tickFrequency = 1;
    private TickStyle _tickStyle = TickStyle.None;

    /// <summary>Creates a peer for a slider running along the given axis.</summary>
    /// <param name="vertical">Whether the slider is vertical.</param>
    public TrackBarPeer(bool vertical) => _vertical = vertical;

    /// <inheritdoc/>
    public event EventHandler? ValueChanged;

    /// <inheritdoc/>
    protected override string WindowClass => NativeMethods.TRACKBAR_CLASS;

    /// <inheritdoc/>
    protected override uint ExtraStyle
        => NativeMethods.WS_TABSTOP | this.TickStyleFlags | (_vertical ? NativeMethods.TBS_VERT : 0);

    /// <summary>The native style bits that place or suppress ticks.</summary>
    private uint TickStyleFlags
        => _tickStyle switch
        {
            TickStyle.TopLeft => _TbsAutoTicks | _TbsTop,
            TickStyle.BottomRight => _TbsAutoTicks,
            TickStyle.Both => _TbsAutoTicks | _TbsBoth,
            _ => NativeMethods.TBS_NOTICKS,
        };

    /// <inheritdoc/>
    public void SetRange(int minimum, int maximum)
    {
        _minimum = minimum;
        _maximum = maximum;
        if (Handle == 0)
            return;

        NativeMethods.SendMessageW(Handle, NativeMethods.TBM_SETRANGEMIN, 0, minimum);
        NativeMethods.SendMessageW(Handle, NativeMethods.TBM_SETRANGEMAX, 1, maximum);
    }

    /// <inheritdoc/>
    public void SetSteps(int smallChange, int largeChange)
    {
        _smallChange = smallChange;
        _largeChange = largeChange;
        if (Handle == 0)
            return;

        NativeMethods.SendMessageW(Handle, NativeMethods.TBM_SETLINESIZE, 0, smallChange);
        NativeMethods.SendMessageW(Handle, NativeMethods.TBM_SETPAGESIZE, 0, largeChange);
    }

    /// <inheritdoc/>
    public void SetValue(int value)
    {
        _value = value;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.TBM_SETPOS, 1, value);
    }

    /// <inheritdoc/>
    public int GetValue() => Handle == 0 ? _value : (int)NativeMethods.SendMessageW(Handle, NativeMethods.TBM_GETPOS, 0, 0);

    /// <inheritdoc/>
    public bool SupportsTicks(int minimum, int maximum, int frequency, TickStyle style)
        => frequency > 0 && style is TickStyle.None or TickStyle.TopLeft or TickStyle.BottomRight or TickStyle.Both;

    /// <inheritdoc/>
    public void SetTicks(int minimum, int maximum, int frequency, TickStyle style)
    {
        _tickFrequency = Math.Max(1, frequency);
        _tickStyle = style;
        if (Handle != 0 && style != TickStyle.None)
            NativeMethods.SendMessageW(Handle, _TbmSetTicFrequency, _tickFrequency, 0);
    }

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        base.CreateChildHandle(parent, controlId);

        // Flush the state buffered before the window existed; the range has to land before the value,
        // or the control clamps it against the stock 0..100. Tick frequency likewise follows the range,
        // because the common control inserts the first and last ticks automatically.
        this.SetRange(_minimum, _maximum);
        this.SetSteps(_smallChange, _largeChange);
        this.SetTicks(_minimum, _maximum, _tickFrequency, _tickStyle);
        this.SetValue(_value);
    }

    /// <inheritdoc/>
    internal override void OnScroll(int scrollCode)
    {
        var current = this.GetValue();
        if (current == _value)
            return;

        _value = current;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
