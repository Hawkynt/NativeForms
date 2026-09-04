using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="ProgressBar"/>, wrapping a real <c>GtkProgressBar</c> (PRD §12),
/// so the desktop's own trough, fill and animation are used rather than our approximation of them.
/// </summary>
internal sealed class GtkProgressBarPeer : GtkControlPeer, IProgressBarPeer {
  private double _fraction;
  private bool _marquee;

  /// <inheritdoc />
  protected override nint CreateWidget() => NativeMethods.gtk_progress_bar_new();

  /// <inheritdoc />
  /// <remarks>A progress bar shows no caption here; the control draws none either.</remarks>
  protected override void ApplyText(string text) { }

  /// <inheritdoc />
  public void SetFraction(double fraction) {
    _fraction = fraction;
    if (_widget != 0 && !_marquee)
      NativeMethods.gtk_progress_bar_set_fraction(_widget, fraction);
  }

  /// <inheritdoc />
  public void SetMarquee(bool marquee) {
    _marquee = marquee;
    if (_widget == 0)
      return;

    // Leaving marquee restores the determinate fill; entering it starts from an empty trough so the
    // block does not jump from wherever the fraction happened to be.
    if (marquee)
      NativeMethods.gtk_progress_bar_set_pulse_step(_widget, 0.1);
    else
      NativeMethods.gtk_progress_bar_set_fraction(_widget, _fraction);
  }

  /// <inheritdoc />
  public void Pulse() {
    if (_widget != 0 && _marquee)
      NativeMethods.gtk_progress_bar_pulse(_widget);
  }

  /// <inheritdoc />
  protected override void OnWidgetRealized() {
    NativeMethods.gtk_progress_bar_set_pulse_step(_widget, 0.1);
    if (!_marquee)
      NativeMethods.gtk_progress_bar_set_fraction(_widget, _fraction);
  }
}
