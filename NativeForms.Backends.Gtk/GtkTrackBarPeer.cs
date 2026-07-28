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
/// well, so <see cref="SetValue"/> suppresses the callback.
/// </remarks>
internal sealed class GtkTrackBarPeer(bool vertical) : GtkControlPeer, ITrackBarPeer
{
    private const int _Horizontal = 0;
    private const int _Vertical = 1;

    private double _value;
    private double _minimum;
    private double _maximum = 10;
    private double _step = 1;
    private double _page = 5;
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
        if (_widget != 0)
            NativeMethods.gtk_range_set_range(_widget, minimum, maximum);
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

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        // The control paints its own value read-out where it wants one, so the scale must not add a second.
        NativeMethods.gtk_scale_set_draw_value(_widget, 0);
        NativeMethods.gtk_range_set_range(_widget, _minimum, _maximum);
        NativeMethods.gtk_range_set_increments(_widget, _step, _page);
        NativeMethods.gtk_range_set_value(_widget, _value);

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
