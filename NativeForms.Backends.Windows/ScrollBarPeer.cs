using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="ScrollBar"/> — a stand-alone <c>SCROLLBAR</c> window
/// (PRD §12), so the OS supplies the trough, the thumb, the arrow autorepeat and the theme.
/// </summary>
/// <remarks>
/// A stand-alone scroll bar does not move itself: it reports the gesture through
/// <c>WM_HSCROLL</c>/<c>WM_VSCROLL</c> and leaves the owner to work out the new position and write it
/// back. This peer therefore computes the position from the notification code — reading the live drag
/// position out of the control rather than the message, whose 16-bit field would truncate a long range —
/// applies it, and then reports the gesture so the core can clamp and raise its own events.
/// </remarks>
internal sealed class ScrollBarPeer : Win32ChildPeer, IScrollBarPeer
{
    private readonly bool _vertical;
    private int _minimum;
    private int _maximum = 100;
    private int _largeChange = 10;
    private int _smallChange = 1;
    private int _value;

    /// <summary>Creates a peer for a bar running along the given axis.</summary>
    /// <param name="vertical">Whether the bar is vertical; <c>SBS_VERT</c> is a creation-time style.</param>
    public ScrollBarPeer(bool vertical) => _vertical = vertical;

    /// <inheritdoc/>
    public event EventHandler<ScrollEventType>? Scrolled;

    /// <inheritdoc/>
    protected override string WindowClass => "SCROLLBAR";

    /// <inheritdoc/>
    protected override uint ExtraStyle => _vertical ? NativeMethods.SBS_VERT : NativeMethods.SBS_HORZ;

    /// <inheritdoc/>
    public void SetRange(int minimum, int maximum, int largeChange, int smallChange)
    {
        _minimum = minimum;
        _maximum = maximum;
        _largeChange = largeChange;
        _smallChange = smallChange;
        this.PushInfo(NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS);
    }

    /// <inheritdoc/>
    public void SetValue(int value)
    {
        _value = value;
        this.PushInfo(NativeMethods.SIF_POS);
    }

    /// <inheritdoc/>
    public int GetValue()
    {
        if (Handle == 0)
            return _value;

        var info = Query(NativeMethods.SIF_POS);
        return info.nPos;
    }

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        base.CreateChildHandle(parent, controlId);
        this.PushInfo(NativeMethods.SIF_RANGE | NativeMethods.SIF_PAGE | NativeMethods.SIF_POS);
    }

    /// <inheritdoc/>
    internal override void OnScroll(int scrollCode)
    {
        if (scrollCode == NativeMethods.SB_ENDSCROLL)
        {
            Scrolled?.Invoke(this, ScrollEventType.EndScroll);
            return;
        }

        var current = this.GetValue();
        var (position, type) = scrollCode switch
        {
            NativeMethods.SB_LINEUP => (current - _smallChange, ScrollEventType.SmallDecrement),
            NativeMethods.SB_LINEDOWN => (current + _smallChange, ScrollEventType.SmallIncrement),
            NativeMethods.SB_PAGEUP => (current - _largeChange, ScrollEventType.LargeDecrement),
            NativeMethods.SB_PAGEDOWN => (current + _largeChange, ScrollEventType.LargeIncrement),
            NativeMethods.SB_THUMBTRACK => (Query(NativeMethods.SIF_TRACKPOS).nTrackPos, ScrollEventType.ThumbTrack),
            NativeMethods.SB_THUMBPOSITION => (Query(NativeMethods.SIF_TRACKPOS).nTrackPos, ScrollEventType.ThumbPosition),
            NativeMethods.SB_TOP => (_minimum, ScrollEventType.First),
            NativeMethods.SB_BOTTOM => (_maximum, ScrollEventType.Last),
            _ => (current, ScrollEventType.ThumbTrack),
        };

        // Clamp the way the control itself would, so what the core reads back is what the user sees.
        _value = Math.Clamp(position, _minimum, Math.Max(_minimum, _maximum - _largeChange + 1));
        this.PushInfo(NativeMethods.SIF_POS);
        Scrolled?.Invoke(this, type);
    }

    /// <summary>Writes the selected members of the buffered state into the control.</summary>
    private void PushInfo(uint mask)
    {
        if (Handle == 0)
            return;

        var info = new NativeMethods.SCROLLINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = mask,
            nMin = _minimum,
            nMax = _maximum,
            nPage = (uint)Math.Max(1, _largeChange),
            nPos = _value,
        };

        NativeMethods.SetScrollInfo(Handle, NativeMethods.SB_CTL, ref info, true);
    }

    /// <summary>Reads the selected members out of the control.</summary>
    private NativeMethods.SCROLLINFO Query(uint mask)
    {
        var info = new NativeMethods.SCROLLINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = mask,
        };

        NativeMethods.GetScrollInfo(Handle, NativeMethods.SB_CTL, ref info);
        return info;
    }
}
