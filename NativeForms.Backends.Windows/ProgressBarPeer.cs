using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="ProgressBar"/> — a common-controls
/// <c>msctls_progress32</c> (PRD §12), so the OS supplies the trough, the fill and the marquee.
/// </summary>
/// <remarks>
/// The control works in integer units, so the core's 0..1 fraction is mapped onto a fixed 0..10000 range
/// — fine enough that a pixel of fill never quantises visibly. <c>PBS_MARQUEE</c> is a creation-time
/// style, so switching modes recreates the window, and the marquee animates itself once started rather
/// than needing per-tick pulses.
/// </remarks>
internal sealed class ProgressBarPeer : Win32ChildPeer, IProgressBarPeer {
  private const int _Scale = 10000;

  private double _fraction;
  private bool _marquee;

  /// <inheritdoc/>
  protected override string WindowClass => NativeMethods.PROGRESS_CLASS;

  /// <inheritdoc/>
  protected override uint ExtraStyle => _marquee ? NativeMethods.PBS_MARQUEE : 0;

  /// <inheritdoc/>
  public void SetFraction(double fraction) {
    _fraction = fraction;
    if (Handle != 0 && !_marquee)
      NativeMethods.SendMessageW(Handle, NativeMethods.PBM_SETPOS, (nint)(fraction * _Scale), 0);
  }

  /// <inheritdoc/>
  public void SetMarquee(bool marquee) {
    if (_marquee == marquee)
      return;

    _marquee = marquee;
    this.RecreateHandle(); // PBS_MARQUEE is a creation-time style
  }

  /// <inheritdoc/>
  /// <remarks>A Win32 marquee animates on its own timer once started, so a pulse is a no-op here.</remarks>
  public void Pulse() { }

  /// <inheritdoc/>
  internal override void CreateChildHandle(nint parent, int controlId) {
    base.CreateChildHandle(parent, controlId);

    if (_marquee) {
      NativeMethods.SendMessageW(Handle, NativeMethods.PBM_SETMARQUEE, 1, 30);
      return;
    }

    NativeMethods.SendMessageW(Handle, NativeMethods.PBM_SETRANGE32, 0, _Scale);
    NativeMethods.SendMessageW(Handle, NativeMethods.PBM_SETPOS, (nint)(_fraction * _Scale), 0);
  }
}
