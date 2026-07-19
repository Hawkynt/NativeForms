using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Paints and hit-tests the themed scrollbars shared by scrolling containers (and, later, the
/// standalone <c>ScrollBar</c> control). Everything is expressed in scroll units: <c>extent</c> is
/// the total content length, <c>viewport</c> the visible length and <c>position</c> the current
/// scroll offset, all along the bar's axis. Purely static so it costs nothing per control.
/// </summary>
internal static class ScrollBarRenderer
{
    private const int _MinThumbLength = 16;
    private const int _ThumbMargin = 2;

    /// <summary>Paints a scrollbar (track plus proportional thumb) into <paramref name="track"/>.</summary>
    public static void Paint(IGraphics g, ITheme theme, Rectangle track, bool vertical, int extent, int viewport, int position)
    {
        g.FillRectangle(theme.HeaderBackground, track);
        g.FillRectangle(theme.Border, GetThumb(track, vertical, extent, viewport, position));
    }

    /// <summary>The thumb rectangle for the given scroll state, inside <paramref name="track"/>.</summary>
    public static Rectangle GetThumb(Rectangle track, bool vertical, int extent, int viewport, int position)
    {
        var trackLength = vertical ? track.Height : track.Width;
        var thumbLength = GetThumbLength(trackLength, extent, viewport);
        var scrollRange = Math.Max(1, extent - viewport);
        var thumbOffset = (trackLength - thumbLength) * Math.Clamp(position, 0, scrollRange) / scrollRange;

        return vertical
            ? new(track.X + _ThumbMargin, track.Y + thumbOffset, track.Width - (2 * _ThumbMargin), thumbLength)
            : new(track.X + thumbOffset, track.Y + _ThumbMargin, thumbLength, track.Height - (2 * _ThumbMargin));
    }

    /// <summary>
    /// Converts a thumb drag of <paramref name="pixelDelta"/> pixels along the track into the scroll
    /// position it lands on, starting from <paramref name="startPosition"/>.
    /// </summary>
    public static int PositionFromThumbDelta(Rectangle track, bool vertical, int extent, int viewport, int startPosition, int pixelDelta)
    {
        var trackLength = vertical ? track.Height : track.Width;
        var thumbRange = trackLength - GetThumbLength(trackLength, extent, viewport);
        if (thumbRange <= 0)
            return startPosition;

        return startPosition + (pixelDelta * (extent - viewport) / thumbRange);
    }

    /// <summary>
    /// The thumb's length along the track: proportional to the visible share of the content, at
    /// least <see cref="_MinThumbLength"/>, but never longer than a (possibly degenerate) track.
    /// </summary>
    private static int GetThumbLength(int trackLength, int extent, int viewport)
    {
        if (trackLength <= _MinThumbLength || extent <= 0)
            return Math.Max(0, trackLength);

        return Math.Clamp(trackLength * viewport / extent, _MinThumbLength, trackLength);
    }
}
