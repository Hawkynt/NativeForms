using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

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
/// How a scrollbar is proportioned. The geometry is the same everywhere; these are the few numbers that
/// legitimately differ between a bar a container paints inside itself and one the user grabs as a control
/// in its own right.
/// </summary>
/// <param name="MinimumThumbLength">The shortest thumb the renderer produces, so it stays grabbable.</param>
/// <param name="ThumbMargin">The inset from the track's long edges to the thumb.</param>
/// <param name="HasArrows">Whether the bar reserves a stepper button at each end.</param>
internal readonly record struct ScrollBarMetrics(int MinimumThumbLength, int ThumbMargin, bool HasArrows)
{
    /// <summary>The proportions a scrolling container uses for the bars it paints inside itself: no
    /// stepper buttons, a slightly inset thumb, and a longer minimum because those bars run the full
    /// height of a page.</summary>
    public static readonly ScrollBarMetrics Container = new(16, 2, HasArrows: false);

    /// <summary>The proportions of a bar the user operates as a control: stepper buttons at both ends and
    /// a thumb that fills the track's width.</summary>
    public static readonly ScrollBarMetrics Standalone = new(8, 0, HasArrows: true);
}

/// <summary>
/// The one geometry and painting engine behind every scrollbar the toolkit draws — the standalone
/// <see cref="ScrollBar"/> control, the bars <see cref="Panel"/> paints when it scrolls, and the ones
/// inside <see cref="CalendarView"/> and <see cref="DataGridView"/>.
/// </summary>
/// <remarks>
/// Everything is expressed in the Windows Forms quartet — minimum, maximum, value and the page size
/// (<c>largeChange</c>) — because that is the richer model: a scrolling container's extent/viewport/offset
/// triple converts into it exactly (<c>minimum = 0</c>, <c>maximum = extent - 1</c>,
/// <c>largeChange = viewport</c>), which is why the two used to be separate implementations of the same
/// arithmetic. The container-shaped overloads below do that conversion and nothing else.
/// <para>
/// Purely static and free of control state, so a host reuses it without instantiating a
/// <see cref="ScrollBar"/>.
/// </para>
/// </remarks>
internal static class ScrollBarRenderer
{
    /// <summary>The number of stacked lines forming an arrow glyph.</summary>
    private const int _ArrowRows = 4;

    /// <summary>
    /// The channel tone every scrollbar the toolkit draws shares — the standalone control's as well
    /// as the ones a scrolling container paints.
    ///
    /// It has to be derived rather than read straight off the palette because not every theme
    /// publishes a distinct trough colour: GTK answers the same value for the window background and
    /// the header band, so a channel painted in either is invisible against the page behind it and
    /// the bar's arrows and thumb read as loose parts. Carrying the control background half-way to
    /// the border gives a recess that shows on every theme yet stays lighter than the thumb, and it
    /// still follows the OS palette because both ends of the blend do.
    /// </summary>
    public static Color TroughColor(ITheme theme)
    {
        var background = theme.ControlBackground;
        var border = theme.Border;
        return Color.FromArgb(
            (background.A + border.A) / 2,
            (background.R + border.R) / 2,
            (background.G + border.G) / 2,
            (background.B + border.B) / 2);
    }

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
        => ThumbIn(TrackRect(bounds, vertical), vertical, minimum, maximum, value, largeChange, ScrollBarMetrics.Standalone);

    /// <summary>Maps a thumb-start offset (pixels from the track start) back to a value, rounded and
    /// clamped to the scrollable range — the inverse of <see cref="ThumbRect"/> for drag scrubbing.</summary>
    public static int ValueFromThumbOffset(Rectangle bounds, bool vertical, int minimum, int maximum, int largeChange, int thumbOffset)
    {
        var track = TrackRect(bounds, vertical);
        var trackLength = vertical ? track.Height : track.Width;
        var travel = trackLength - ThumbLength(trackLength, minimum, maximum, largeChange, ScrollBarMetrics.Standalone);
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
        g.FillRectangle(TroughColor(theme), TrackRect(bounds, vertical));

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

    // --- The container-shaped face ------------------------------------------------------------------
    //
    // A scrolling host thinks in content extent, viewport length and scroll offset. That triple is the
    // same quartet under another name, so these three convert and delegate rather than computing
    // anything of their own.

    /// <summary>Paints a container's bar (trough plus proportional thumb) into <paramref name="track"/>.</summary>
    public static void Paint(IGraphics g, ITheme theme, Rectangle track, bool vertical, int extent, int viewport, int position)
    {
        g.FillRectangle(TroughColor(theme), track);
        g.FillRectangle(theme.Border, GetThumb(track, vertical, extent, viewport, position));
    }

    /// <summary>The thumb rectangle for the given scroll state, inside <paramref name="track"/>.</summary>
    public static Rectangle GetThumb(Rectangle track, bool vertical, int extent, int viewport, int position)
        => ThumbIn(track, vertical, 0, extent - 1, position, viewport, ScrollBarMetrics.Container);

    /// <summary>
    /// Converts a thumb drag of <paramref name="pixelDelta"/> pixels along the track into the scroll
    /// position it lands on, starting from <paramref name="startPosition"/>.
    /// </summary>
    public static int PositionFromThumbDelta(Rectangle track, bool vertical, int extent, int viewport, int startPosition, int pixelDelta)
    {
        var trackLength = vertical ? track.Height : track.Width;
        var thumbRange = trackLength - ThumbLength(trackLength, 0, extent - 1, viewport, ScrollBarMetrics.Container);
        if (thumbRange <= 0)
            return startPosition;

        return startPosition + (pixelDelta * (extent - viewport) / thumbRange);
    }

    // --- The shared arithmetic ----------------------------------------------------------------------

    /// <summary>
    /// The thumb inside a track already reduced to the channel, under the given proportions. This is the
    /// single place the position-to-pixel mapping lives.
    /// </summary>
    private static Rectangle ThumbIn(
        Rectangle track,
        bool vertical,
        int minimum,
        int maximum,
        int value,
        int largeChange,
        ScrollBarMetrics metrics)
    {
        var trackLength = vertical ? track.Height : track.Width;
        var thumbLength = ThumbLength(trackLength, minimum, maximum, largeChange, metrics);
        var maximumValue = MaximumValue(minimum, maximum, largeChange);
        var travel = trackLength - thumbLength;
        var offset = maximumValue > minimum && travel > 0
            ? (int)((long)travel * (Math.Clamp(value, minimum, maximumValue) - minimum) / (maximumValue - minimum))
            : 0;

        var margin = metrics.ThumbMargin;
        return vertical
            ? new(track.X + margin, track.Y + offset, track.Width - (2 * margin), thumbLength)
            : new(track.X + offset, track.Y + margin, thumbLength, track.Height - (2 * margin));
    }

    /// <summary>The thumb's extent along the track: proportional to the page size, never smaller than
    /// the grab minimum, never longer than the track.</summary>
    private static int ThumbLength(int trackLength, int minimum, int maximum, int largeChange, ScrollBarMetrics metrics)
    {
        var range = maximum - minimum + 1;
        if (range <= 0)
            return metrics.HasArrows ? trackLength : Math.Max(0, trackLength);

        // A track too short to hold the minimum thumb yields the whole track rather than an overhang.
        if (trackLength <= metrics.MinimumThumbLength)
            return Math.Max(0, trackLength);

        return Math.Clamp((int)((long)trackLength * largeChange / range), metrics.MinimumThumbLength, trackLength);
    }

    /// <summary>The arrow-button edge length: the bar's thickness, shrunk when the bar is too short
    /// for two full buttons.</summary>
    private static int ArrowLength(Rectangle bounds, bool vertical)
    {
        var length = vertical ? bounds.Height : bounds.Width;
        var thickness = vertical ? bounds.Width : bounds.Height;
        return Math.Min(thickness, length / 2);
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
