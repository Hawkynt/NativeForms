using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>The direction a triangular glyph points.</summary>
internal enum GlyphDirection
{
    /// <summary>Apex on the left edge.</summary>
    Left,

    /// <summary>Apex on the right edge.</summary>
    Right,

    /// <summary>Apex on the top edge.</summary>
    Up,

    /// <summary>Apex on the bottom edge.</summary>
    Down,
}

/// <summary>
/// Draws the small vector glyphs owner-drawn chrome needs (expander arrows, tab-strip scroll
/// triangles) with plain line strokes, so no image assets or per-frame allocations are involved.
/// </summary>
internal static class Glyphs
{
    /// <summary>Fills a solid triangle inscribed in <paramref name="bounds"/>, pointing <paramref name="direction"/>.</summary>
    public static void PaintTriangle(IGraphics g, Color color, Rectangle bounds, GlyphDirection direction)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        switch (direction)
        {
            case GlyphDirection.Right:
                for (var x = 0; x < width; ++x)
                {
                    var inset = width > 1 ? x * height / (2 * (width - 1)) : 0;
                    g.DrawLine(color, bounds.X + x, bounds.Y + inset, bounds.X + x, bounds.Bottom - 1 - inset);
                }

                break;

            case GlyphDirection.Left:
                for (var x = 0; x < width; ++x)
                {
                    var inset = width > 1 ? (width - 1 - x) * height / (2 * (width - 1)) : 0;
                    g.DrawLine(color, bounds.X + x, bounds.Y + inset, bounds.X + x, bounds.Bottom - 1 - inset);
                }

                break;

            case GlyphDirection.Down:
                for (var y = 0; y < height; ++y)
                {
                    var inset = height > 1 ? y * width / (2 * (height - 1)) : 0;
                    g.DrawLine(color, bounds.X + inset, bounds.Y + y, bounds.Right - 1 - inset, bounds.Y + y);
                }

                break;

            case GlyphDirection.Up:
            default:
                for (var y = 0; y < height; ++y)
                {
                    var inset = height > 1 ? (height - 1 - y) * width / (2 * (height - 1)) : 0;
                    g.DrawLine(color, bounds.X + inset, bounds.Y + y, bounds.Right - 1 - inset, bounds.Y + y);
                }

                break;
        }
    }
}
