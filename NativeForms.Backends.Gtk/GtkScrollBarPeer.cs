using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="ScrollBar"/>, wrapping a real <c>GtkScrollbar</c> (PRD §12) so
/// the desktop supplies the trough, the thumb, the overlay-scrolling behaviour and the autorepeat.
/// </summary>
/// <remarks>
/// GTK expresses the reachable end of a range as <c>upper - page_size</c>, where Windows Forms — and this
/// control — say <c>Maximum - LargeChange + 1</c>. The two agree once <c>upper</c> is set one past the
/// maximum, which is what <see cref="SetRange"/> does.
/// <para>
/// Two signals are needed to report a scroll faithfully: "change-value" names the gesture but runs before
/// the value moves, and "value-changed" runs after but says nothing about how. The type is therefore
/// latched by the first and consumed by the second.
/// </para>
/// </remarks>
internal sealed class GtkScrollBarPeer : GtkControlPeer, IScrollBarPeer {
  private readonly bool _vertical;
  private int _minimum;
  private int _maximum = 100;
  private int _largeChange = 10;
  private int _smallChange = 1;
  private int _value;
  private bool _suppress;
  private ScrollEventType _pendingType = ScrollEventType.ThumbTrack;

  /// <summary>Creates a peer for a bar running along the given axis.</summary>
  /// <param name="vertical">Whether the bar is vertical; GTK fixes this at construction.</param>
  public GtkScrollBarPeer(bool vertical) => _vertical = vertical;

  /// <inheritdoc />
  public event EventHandler<ScrollEventType>? Scrolled;

  /// <inheritdoc />
  protected override nint CreateWidget() => NativeMethods.gtk_scrollbar_new(_vertical ? 1 : 0, 0);

  /// <inheritdoc />
  protected override void ApplyText(string text) { }

  /// <inheritdoc />
  public void SetRange(int minimum, int maximum, int largeChange, int smallChange) {
    _minimum = minimum;
    _maximum = maximum;
    _largeChange = largeChange;
    _smallChange = smallChange;
    this.PushRange();
  }

  /// <inheritdoc />
  public void SetValue(int value) {
    _value = value;
    if (_widget == 0)
      return;

    _suppress = true;
    try {
      NativeMethods.gtk_range_set_value(_widget, value);
    } finally {
      _suppress = false;
    }
  }

  /// <inheritdoc />
  public int GetValue() => _widget == 0 ? _value : (int)Math.Round(NativeMethods.gtk_range_get_value(_widget));

  /// <inheritdoc />
  protected override void OnWidgetRealized() {
    this.PushRange();
    this.SetValue(_value);

    var data = this.PinSelf();
    unsafe {
      var change = (nint)(delegate* unmanaged[Cdecl]<nint, int, double, nint, int>)&OnChangeValue;
      NativeMethods.g_signal_connect_data(_widget, "change-value", change, data, 0, 0);

      var changed = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnValueChanged;
      NativeMethods.g_signal_connect_data(_widget, "value-changed", changed, data, 0, 0);
    }
  }

  /// <summary>Writes the whole adjustment in one go; the members are interdependent.</summary>
  private void PushRange() {
    if (_widget == 0)
      return;

    var adjustment = NativeMethods.gtk_range_get_adjustment(_widget);
    if (adjustment == 0)
      return;

    _suppress = true;
    try {
      NativeMethods.gtk_adjustment_configure(
          adjustment,
          _value,
          _minimum,
          _maximum + 1,
          _smallChange,
          _largeChange,
          _largeChange);
    } finally {
      _suppress = false;
    }
  }

  /// <summary>Latches the gesture the pending value change came from.</summary>
  private void LatchScrollType(int scrollType) => _pendingType = Translate(scrollType);

  /// <summary>Reports the completed value change, consuming the latched gesture.</summary>
  private void RaiseValueChanged() {
    if (_suppress)
      return;

    var type = _pendingType;
    _pendingType = ScrollEventType.ThumbTrack;
    _value = this.GetValue();
    Scrolled?.Invoke(this, type);
  }

  /// <summary>Maps a <c>GtkScrollType</c> onto the Windows Forms gesture the core reports.</summary>
  private static ScrollEventType Translate(int scrollType)
      => scrollType switch {
        2 or 6 or 10 => ScrollEventType.SmallDecrement, // STEP_BACKWARD, STEP_UP, STEP_LEFT
        3 or 7 or 11 => ScrollEventType.SmallIncrement, // STEP_FORWARD, STEP_DOWN, STEP_RIGHT
        4 or 8 or 12 => ScrollEventType.LargeDecrement, // PAGE_BACKWARD, PAGE_UP, PAGE_LEFT
        5 or 9 or 13 => ScrollEventType.LargeIncrement, // PAGE_FORWARD, PAGE_DOWN, PAGE_RIGHT
        14 => ScrollEventType.First,
        15 => ScrollEventType.Last,
        _ => ScrollEventType.ThumbTrack,                // NONE, JUMP — a drag or a trough click
      };

  /// <summary>
  /// Native handler for "change-value", shaped as
  /// <c>gboolean (GtkRange *, GtkScrollType, gdouble, gpointer)</c>. Returns <c>FALSE</c> so GTK still
  /// applies the move; this only records how it was asked for.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static int OnChangeValue(nint range, int scrollType, double value, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkScrollBarPeer peer)
      peer.LatchScrollType(scrollType);

    return 0;
  }

  /// <summary>
  /// Native handler for "value-changed", shaped as <c>void (GtkRange *, gpointer)</c>.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnValueChanged(nint range, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkScrollBarPeer peer)
      peer.RaiseValueChanged();
  }
}
