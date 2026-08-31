using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="TrackBar"/>, wrapping a real <c>GtkScale</c> (PRD §12), so the
/// desktop draws the groove and thumb, and its own keyboard and scroll conventions apply.
/// </summary>
/// <remarks>
/// GTK fixes a scale's orientation at construction, so the orientation is passed in and a control that
/// turns re-realizes rather than mutating the widget. "value-changed" fires for programmatic writes as
/// well, so <see cref="SetValue"/> suppresses the callback. GtkScale marks can be placed independently
/// on either side, so this peer can preserve every <see cref="TickStyle"/> configuration exactly.
/// </remarks>
internal sealed class GtkTrackBarPeer(bool vertical) : GtkControlPeer, ITrackBarPeer, ITrackBarTickPeer
{
    private const int _Horizontal = 0;
    private const int _Vertical = 1;
    private const int _Left = 0;
    private const int _Right = 1;
    private const int _Top = 2;
    private const int _Bottom = 3;

    private double _value;
    private double _minimum;
    private double _maximum = 10;
    private double _step = 1;
    private double _page = 5;
    private int _tickFrequency = 1;
    private TickStyle _tickStyle = TickStyle.None;
    private bool _suppress;

    /// <inheritdoc />
    public event EventHandler? ValueChanged;

    /// <inheritdoc />
    protected override nint CreateWidget()
        => NativeMethods.gtk_scale_new_with_range(vertical ? _Vertical : _Horizontal, _minimum, _maximum, _step);

    /// <inheritdoc />
    /// <remarks>A slider carries no caption; the control draws none either.</remarks>
    protected override void ApplyText(string text) { }

    /// <inheritdoc />
    public void SetRange(int minimum, int maximum)
    {
        _minimum = minimum;
        _maximum = maximum;
        if (_widget == 0)
            return;

        NativeMethods.gtk_range_set_range(_widget, minimum, maximum);
        this.ApplyTicks();
    }

    /// <inheritdoc />
    public void SetValue(int value)
    {
        _value = value;
        if (_widget == 0)
            return;

        _suppress = true;
        try
        {
            NativeMethods.gtk_range_set_value(_widget, value);
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <inheritdoc />
    public int GetValue()
        => _widget == 0 ? (int)_value : (int)Math.Round(NativeMethods.gtk_range_get_value(_widget));

    /// <inheritdoc />
    public void SetSteps(int smallChange, int largeChange)
    {
        _step = Math.Max(1, smallChange);
        _page = Math.Max(1, largeChange);
        if (_widget != 0)
            NativeMethods.gtk_range_set_increments(_widget, _step, _page);
    }

    /// <inheritdoc/>
    public bool SupportsTicks(int minimum, int maximum, int frequency, TickStyle style)
        => frequency > 0 && style is TickStyle.None or TickStyle.TopLeft or TickStyle.BottomRight or TickStyle.Both;

    /// <inheritdoc/>
    public void SetTicks(int minimum, int maximum, int frequency, TickStyle style)
    {
        _minimum = minimum;
        _maximum = maximum;
        _tickFrequency = Math.Max(1, frequency);
        _tickStyle = style;
        if (_widget != 0)
            this.ApplyTicks();
    }

    /// <summary>Rebuilds the GtkScale marks from the managed range and style.</summary>
    private void ApplyTicks()
    {
        NativeMethods.gtk_scale_clear_marks(_widget);
        if (_tickStyle == TickStyle.None)
            return;

        var firstPosition = vertical ? _Left : _Top;
        var secondPosition = vertical ? _Right : _Bottom;
        var first = _tickStyle is TickStyle.TopLeft or TickStyle.Both;
        var second = _tickStyle is TickStyle.BottomRight or TickStyle.Both;
        var frequency = (long)_tickFrequency;

        for (long value = (long)_minimum; value <= (long)_maximum; value += frequency)
            this.AddMark(value, firstPosition, secondPosition, first, second);

        var span = (long)_maximum - (long)_minimum;
        if (span > 0 && span % frequency != 0)
            this.AddMark((long)_maximum, firstPosition, secondPosition, first, second);
    }

    /// <summary>Adds one logical tick on each requested side of the scale.</summary>
    private void AddMark(long value, int firstPosition, int secondPosition, bool first, bool second)
    {
        if (first)
            NativeMethods.gtk_scale_add_mark(_widget, value, firstPosition, 0);
        if (second)
            NativeMethods.gtk_scale_add_mark(_widget, value, secondPosition, 0);
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        // The control paints its own value read-out where it wants one, so the scale must not add a second.
        NativeMethods.gtk_scale_set_draw_value(_widget, 0);
        NativeMethods.gtk_range_set_range(_widget, _minimum, _maximum);
        NativeMethods.gtk_range_set_increments(_widget, _step, _page);
        NativeMethods.gtk_range_set_value(_widget, _value);
        this.ApplyTicks();

        var data = this.PinSelf();
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnValueChanged;
            NativeMethods.g_signal_connect_data(_widget, "value-changed", callback, data, 0, 0);
        }
    }

    private void RaiseValueChanged()
    {
        if (_suppress)
            return;

        _value = this.GetValue();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Native handler for the scale's "value-changed" signal, shaped as
    /// <c>void (GtkRange *range, gpointer user_data)</c>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnValueChanged(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkTrackBarPeer peer)
            peer.RaiseValueChanged();
    }
}
