using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="GroupBox"/> (PRD §12): the toolkit's own container window
/// carrying the control's coordinate system, with a stock <c>BUTTON</c> in the <c>BS_GROUPBOX</c> style
/// behind everything else, filling it — so the frame and caption are drawn by the real control in the
/// real theme.
/// </summary>
/// <remarks>
/// Parenting the children to the stock group box directly would look simpler and be wrong twice over:
/// their bounds would shift by whatever the control reserves, and their <c>WM_COMMAND</c> notifications
/// would arrive at a stock window procedure that discards them, so a button inside a group box would go
/// dead. Hosting the frame instead of being hosted by it keeps both the layout and the routing that the
/// container already implements.
/// </remarks>
internal sealed class GroupBoxPeer : Win32CanvasPeer, IGroupBoxPeer {
  private nint _frame;
  private Size _size;

  /// <summary>A container is not a tab stop; the frame it hosts takes no focus either.</summary>
  public GroupBoxPeer() => this.SetFocusable(false);

  /// <inheritdoc/>
  public override void SetText(string text) {
    base.SetText(text);
    if (_frame != 0)
      NativeMethods.SetWindowTextW(_frame, text);
  }

  /// <inheritdoc/>
  public override void SetBounds(Rectangle bounds) {
    base.SetBounds(bounds);
    _size = bounds.Size;
    this.SizeFrame();
  }

  /// <inheritdoc/>
  internal override void CreateChildHandle(nint parent, int controlId) {
    base.CreateChildHandle(parent, controlId);
    if (Handle == 0)
      return;

    // Created after the container but placed at the bottom of the z-order, so the children this
    // surface hosts stay on top of it.
    _frame = NativeMethods.CreateWindowExW(
        0,
        "BUTTON",
        _text,
        NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_GROUPBOX,
        0,
        0,
        _size.Width,
        _size.Height,
        Handle,
        0,
        NativeMethods.GetModuleHandleW(null),
        0);

    NativeMethods.SetWindowPos(
        _frame,
        NativeMethods.HWND_BOTTOM,
        0,
        0,
        0,
        0,
        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
  }

  /// <inheritdoc/>
  public override void Dispose() {
    if (_frame != 0) {
      NativeMethods.DestroyWindow(_frame);
      _frame = 0;
    }

    base.Dispose();
  }

  /// <summary>Stretches the frame over the whole surface.</summary>
  private void SizeFrame() {
    if (_frame != 0 && !_size.IsEmpty)
      NativeMethods.MoveWindow(_frame, 0, 0, _size.Width, _size.Height, true);
  }
}
