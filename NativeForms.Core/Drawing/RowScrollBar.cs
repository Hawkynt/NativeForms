using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The vertical scrollbar a row-based owner-drawn list paints down its own right edge.
/// </summary>
/// <remarks>
/// <see cref="ScrollBarRenderer"/> already knows how a scrollbar is shaped; what it does not know is
/// that a list scrolls in rows, that the bar appears only when there are more rows than fit, and that
/// a press on the trough has to be turned back into a row index. That is the same twenty lines in
/// every list-shaped control, so it lives here once rather than being copied into each — the mistake
/// that left <see cref="Hawkynt.NativeForms.ListBox"/> and <see cref="Hawkynt.NativeForms.TreeListView"/> silently
/// scrollable with nothing on screen to say so.
/// <para>
/// The quartet the renderer wants is the row count minus one as the maximum and the viewport as the
/// page, which is the conversion its container-shaped overloads document. The stepper-button shape is
/// deliberate: it matches the bar <see cref="Hawkynt.NativeForms.DataGridView"/> paints, and these lists sit
/// beside grids.
/// </para>
/// </remarks>
internal sealed class RowScrollBar {
  private bool _dragging;
  private int _grabOffset;

  /// <summary>Whether a thumb drag is in progress, so the host keeps routing the mouse here.</summary>
  public bool IsDragging => _dragging;

  /// <summary>Whether the list has more rows than it can show, which is the only time a bar is drawn.</summary>
  public static bool IsNeeded(int rowCount, int visibleRows) => rowCount > visibleRows;

  /// <summary>The width a shown bar takes from the rows; zero when there is no bar.</summary>
  public static int WidthOf(ITheme theme, int rowCount, int visibleRows)
      => IsNeeded(rowCount, visibleRows) ? theme.ScrollBarSize : 0;

  /// <summary>
  /// The strip the bar occupies: the right edge, from <paramref name="top"/> down. A list with a
  /// header starts below it, so the bar never covers a column caption.
  /// </summary>
  public static Rectangle StripOf(ITheme theme, int width, int top, int height)
      => new(width - theme.ScrollBarSize, top, theme.ScrollBarSize, Math.Max(0, height - top));

  public void Paint(IGraphics g, ITheme theme, Rectangle strip, int rowCount, int visibleRows, int position)
      => ScrollBarRenderer.Paint(
          g,
          theme,
          strip,
          vertical: true,
          0,
          Math.Max(0, rowCount - 1),
          position,
          Math.Max(1, visibleRows),
          _dragging ? ScrollBarPart.Thumb : ScrollBarPart.None);

  /// <summary>
  /// Routes a press. Arrows step a row, the channel pages, the thumb arms a drag.
  /// </summary>
  /// <returns>The row to scroll to, or -1 when the press was not on the bar at all.</returns>
  public int MouseDown(ITheme theme, Rectangle strip, int rowCount, int visibleRows, int position, Point location) {
    if (!IsNeeded(rowCount, visibleRows))
      return -1;

    var maximum = Math.Max(0, rowCount - 1);
    var page = Math.Max(1, visibleRows);
    switch (ScrollBarRenderer.HitTest(strip, vertical: true, 0, maximum, position, page, location)) {
      case ScrollBarPart.DecreaseArrow: return position - 1;
      case ScrollBarPart.IncreaseArrow: return position + 1;
      case ScrollBarPart.DecreaseChannel: return position - page;
      case ScrollBarPart.IncreaseChannel: return position + page;
      case ScrollBarPart.Thumb:
        _dragging = true;
        _grabOffset = location.Y - ScrollBarRenderer.ThumbRect(strip, vertical: true, 0, maximum, position, page).Y;
        return position;

      default: return -1;
    }
  }

  /// <summary>Scrubs a thumb drag to the pointer, mapping pixels back onto a row.</summary>
  public int Drag(Rectangle strip, int rowCount, int visibleRows, int y) {
    var maximum = Math.Max(0, rowCount - 1);
    var page = Math.Max(1, visibleRows);
    var track = ScrollBarRenderer.TrackRect(strip, vertical: true);
    return ScrollBarRenderer.ValueFromThumbOffset(strip, vertical: true, 0, maximum, page, y - _grabOffset - track.Y);
  }

  public void Release() => _dragging = false;
}
