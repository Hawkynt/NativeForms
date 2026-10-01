using System.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The thickness of a top-level window's non-client frame — caption and sizing borders — per edge, in
/// device pixels. The toolkit sizes a form by its client area on every platform; this is what the Win32
/// window peer adds to get the outer rectangle <c>SetWindowPos</c> wants, and removes again to report a
/// native resize back.
/// </summary>
/// <param name="Left">Frame width left of the client area.</param>
/// <param name="Top">Frame height above it — the caption plus the top border.</param>
/// <param name="Right">Frame width right of it.</param>
/// <param name="Bottom">Frame height below it.</param>
internal readonly record struct Win32FrameInsets(int Left, int Top, int Right, int Bottom) {
  /// <summary>Reads the insets off the rectangle <c>AdjustWindowRectEx</c> made of an empty one.</summary>
  public static Win32FrameInsets FromAdjustedEmpty(NativeMethods.RECT adjusted)
      => new(-adjusted.left, -adjusted.top, adjusted.right, adjusted.bottom);

  /// <summary>The outer window size that holds a client area of <paramref name="client"/>.</summary>
  public Size ToOuter(Size client) => new(client.Width + this.Left + this.Right, client.Height + this.Top + this.Bottom);

  /// <summary>The client area left inside an outer window of <paramref name="outer"/>; never negative.</summary>
  public Size ToClient(Size outer)
      => new(Math.Max(0, outer.Width - this.Left - this.Right), Math.Max(0, outer.Height - this.Top - this.Bottom));

  /// <summary>
  /// <see cref="ToOuter"/> for a minimum/maximum size limit, where a zero component means "no limit"
  /// and has to stay zero rather than become the frame.
  /// </summary>
  public Size ToOuterLimit(Size limit) => new(
      limit.Width > 0 ? limit.Width + this.Left + this.Right : 0,
      limit.Height > 0 ? limit.Height + this.Top + this.Bottom : 0);

  /// <summary>
  /// Measures the frame a window of the given styles has at <paramref name="dpi"/>.
  /// </summary>
  /// <remarks>
  /// <c>AdjustWindowRectExForDpi</c>, not <c>AdjustWindowRectEx</c>: under per-monitor awareness the
  /// latter answers for the system DPI the process started with, so on a 150% display it undercounts
  /// the caption and borders and the form loses that difference off its bottom edge. The plain call is
  /// only the fallback for Windows before 10 1607, which has no per-monitor frame to measure anyway.
  /// </remarks>
  public static unsafe Win32FrameInsets Measure(uint style, uint exStyle, uint dpi) {
    var rect = default(NativeMethods.RECT);
    try {
      if (dpi > 0 && NativeMethods.AdjustWindowRectExForDpi(&rect, style, false, exStyle, dpi))
        return FromAdjustedEmpty(rect);
    } catch (EntryPointNotFoundException) {
      // Before Windows 10 1607: one DPI for the whole desktop, which the plain call measures.
    }

    rect = default;
    return NativeMethods.AdjustWindowRectEx(&rect, style, false, exStyle) ? FromAdjustedEmpty(rect) : default;
  }

  /// <summary>Measures the frame <paramref name="hwnd"/> has right now, at the DPI of its display.</summary>
  public static Win32FrameInsets Measure(nint hwnd) {
    if (hwnd == 0)
      return default;

    var style = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE);
    var exStyle = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE);
    uint dpi;
    try {
      dpi = NativeMethods.GetDpiForWindow(hwnd);
    } catch (EntryPointNotFoundException) {
      dpi = 0;
    }

    return Measure(style, exStyle, dpi);
  }
}
