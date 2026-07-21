using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The interactive region of a scrollbar a point falls into.</summary>
internal enum ScrollBarPart
{
    /// <summary>Outside the bar.</summary>
    None,

    /// <summary>The arrow button at the minimum end.</summary>
    DecreaseArrow,

    /// <summary>The arrow button at the maximum end.</summary>
    IncreaseArrow,

    /// <summary>The draggable thumb.</summary>
    Thumb,

    /// <summary>The channel between the decrease arrow and the thumb.</summary>
    DecreaseChannel,

    /// <summary>The channel between the thumb and the increase arrow.</summary>
    IncreaseChannel,
}

/// <summary>
/// The geometry and painting engine behind <see cref="ScrollBar"/>: arrow/track/thumb rectangles,
/// value-to-pixel mapping and the themed rendering, all parameterized by bounds and orientation.
/// Kept free of any control state so future scrolling hosts (an auto-scrolling panel, an embedded
/// grid scrollbar) can reuse it without instantiating a <see cref="ScrollBar"/>.
/// </summary>
internal static class ScrollBarRenderer
{
    /// <summary>The smallest thumb the renderer produces, so it stays grabbable on long ranges.</summary>
    private const int _MinThumbLength = 8;

    /// <summary>The number of stacked lines forming an arrow glyph.</summary>
    private const int _ArrowRows = 4;

    /// <summary>The highest value the thumb can scroll to: one page short of the maximum, like Win32.</summary>
    public static int MaximumValue(int minimum, int maximum, int largeChange)
        => Math.Max(minimum, maximum - largeChange + 1);

    /// <summary>The square arrow button at the minimum end of the bar.</summary>
    public static Rectangle DecreaseArrowRect(Rectangle bounds, bool vertical)
    {
        var length = ArrowLength(bounds, vertical);
        return vertical
            ? new(bounds.X, bounds.Y, bounds.Width, length)
            : new(bounds.X, bounds.Y, length, bounds.Height);
    }

    /// <summary>The square arrow button at the maximum end of the bar.</summary>
    public static Rectangle IncreaseArrowRect(Rectangle bounds, bool vertical)
    {
        var length = ArrowLength(bounds, vertical);
        return vertical
            ? new(bounds.X, bounds.Bottom - length, bounds.Width, length)
            : new(bounds.Right - length, bounds.Y, length, bounds.Height);
    }

    /// <summary>The channel between the two arrow buttons that the thumb travels in.</summary>
    public static Rectangle TrackRect(Rectangle bounds, bool vertical)
    {
        var length = ArrowLength(bounds, vertical);
        return vertical
            ? new(bounds.X, bounds.Y + length, bounds.Width, Math.Max(0, bounds.Height - 2 * length))
            : new(bounds.X + length, bounds.Y, Math.Max(0, bounds.Width - 2 * length), bounds.Height);
    }

    /// <summary>The thumb, sized proportionally to <paramref name="largeChange"/> over the range and
    /// positioned by <paramref name="value"/>.</summary>
    public static Rectangle ThumbRect(Rectangle bounds, bool vertical, int minimum, int maximum, int value, int largeChange)
    {
        var track = TrackRect(bounds, vertical);
        var trackLength = vertical ? track.Height : track.Width;
        var thumbLength = ThumbLength(trackLength, minimum, maximum, largeChange);
        var maximumValue = MaximumValue(minimum, maximum, largeChange);
        var travel = trackLength - thumbLength;
        var offset = maximumValue > minimum && travel > 0
            ? (int)((long)travel * (value - minimum) / (maximumValue - minimum))
            : 0;
        return vertical
            ? new(bounds.X, track.Y + offset, bounds.Width, thumbLength)
            : new(track.X + offset, bounds.Y, thumbLength, bounds.Height);
    }

    /// <summary>Maps a thumb-start offset (pixels from the track start) back to a value, rounded and
    /// clamped to the scrollable range — the inverse of <see cref="ThumbRect"/> for drag scrubbing.</summary>
    public static int ValueFromThumbOffset(Rectangle bounds, bool vertical, int minimum, int maximum, int largeChange, int thumbOffset)
    {
        var track = TrackRect(bounds, vertical);
        var trackLength = vertical ? track.Height : track.Width;
        var travel = trackLength - ThumbLength(trackLength, minimum, maximum, largeChange);
        var maximumValue = MaximumValue(minimum, maximum, largeChange);
        if (travel <= 0)
            return minimum;

        var value = minimum + (int)(((long)thumbOffset * (maximumValue - minimum) + travel / 2) / travel);
        return Math.Clamp(value, minimum, maximumValue);
    }

    /// <summary>Classifies which interactive part of the bar <paramref name="location"/> hits.</summary>
    public static ScrollBarPart HitTest(Rectangle bounds, bool vertical, int minimum, int maximum, int value, int largeChange, Point location)
    {
        if (!bounds.Contains(location))
            return ScrollBarPart.None;

        if (DecreaseArrowRect(bounds, vertical).Contains(location))
            return ScrollBarPart.DecreaseArrow;

        if (IncreaseArrowRect(bounds, vertical).Contains(location))
            return ScrollBarPart.IncreaseArrow;

        var thumb = ThumbRect(bounds, vertical, minimum, maximum, value, largeChange);
        if (thumb.Contains(location))
            return ScrollBarPart.Thumb;

        var position = vertical ? location.Y : location.X;
        var thumbStart = vertical ? thumb.Y : thumb.X;
        return position < thumbStart ? ScrollBarPart.DecreaseChannel : ScrollBarPart.IncreaseChannel;
    }

    /// <summary>Paints the whole bar — trough, arrows and thumb — through the theme, highlighting
    /// <paramref name="pressed"/>.</summary>
    public static void Paint(IGraphics g, ITheme theme, Rectangle bounds, bool vertical, int minimum, int maximum, int value, int largeChange, ScrollBarPart pressed)
    {
        g.FillRectangle(theme.ControlBackground, bounds);

        // The channel the thumb travels in, in the trough tone the scrolling containers share.
        // Without it the bar's rectangle keeps the control background — the page behind it — so the
        // two arrow glyphs and the thumb float as disconnected parts with bare page showing through
        // the gap between them, and the control reads as broken rather than as a scrollbar. The
        // trough is what joins the parts into one control.
        g.FillRectangle(Drawing.ScrollBarRenderer.TroughColor(theme), TrackRect(bounds, vertical));

        var decrease = DecreaseArrowRect(bounds, vertical);
        var increase = IncreaseArrowRect(bounds, vertical);
        if (pressed == ScrollBarPart.DecreaseArrow)
            g.FillRectangle(theme.HeaderBackground, decrease);
        else if (pressed == ScrollBarPart.IncreaseArrow)
            g.FillRectangle(theme.HeaderBackground, increase);

        DrawArrow(g, theme.ControlText, decrease, vertical, towardMinimum: true);
        DrawArrow(g, theme.ControlText, increase, vertical, towardMinimum: false);

        var thumb = ThumbRect(bounds, vertical, minimum, maximum, value, largeChange);
        if ((vertical ? thumb.Height : thumb.Width) > 0)
            g.FillRectangle(pressed == ScrollBarPart.Thumb ? theme.Accent : theme.Border, thumb);
    }

    /// <summary>The arrow-button edge length: the bar's thickness, shrunk when the bar is too short
    /// for two full buttons.</summary>
    private static int ArrowLength(Rectangle bounds, bool vertical)
    {
        var length = vertical ? bounds.Height : bounds.Width;
        var thickness = vertical ? bounds.Width : bounds.Height;
        return Math.Min(thickness, length / 2);
    }

    /// <summary>The thumb's extent along the track: proportional to the page size, never smaller than
    /// the grab minimum, never longer than the track.</summary>
    private static int ThumbLength(int trackLength, int minimum, int maximum, int largeChange)
    {
        var range = maximum - minimum + 1;
        return range <= 0
            ? trackLength
            : Math.Clamp((int)((long)trackLength * largeChange / range), Math.Min(_MinThumbLength, trackLength), trackLength);
    }

    /// <summary>Draws a triangle glyph of stacked lines pointing along the bar's axis.</summary>
    private static void DrawArrow(IGraphics g, Color color, Rectangle rect, bool vertical, bool towardMinimum)
    {
        if (vertical)
        {
            var centerX = rect.X + rect.Width / 2;
            var top = rect.Y + (rect.Height - _ArrowRows) / 2;
            for (var i = 0; i < _ArrowRows; ++i)
            {
                var half = towardMinimum ? i : _ArrowRows - 1 - i;
                g.DrawLine(color, centerX - half, top + i, centerX + half, top + i);
            }

            return;
        }

        var centerY = rect.Y + rect.Height / 2;
        var left = rect.X + (rect.Width - _ArrowRows) / 2;
        for (var i = 0; i < _ArrowRows; ++i)
        {
            var half = towardMinimum ? i : _ArrowRows - 1 - i;
            g.DrawLine(color, left + i, centerY - half, left + i, centerY + half);
        }
    }
}
