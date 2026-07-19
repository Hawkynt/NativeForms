using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// An <see cref="IGraphics"/> over one <c>cairo_t*</c> — the surface handed to a single "draw"
/// callback. Shapes go straight to Cairo; text is laid out and rendered with Pango. The clip stack
/// is Cairo's own save/restore, so <see cref="PushClip"/>/<see cref="PopClip"/> must be balanced.
/// </summary>
internal sealed class GtkGraphics : IGraphics
{
    private readonly nint _cr;

    /// <summary>Wraps the Cairo context for the lifetime of one paint.</summary>
    internal GtkGraphics(nint cr) => _cr = cr;

    /// <inheritdoc />
    public void FillRectangle(Color color, Rectangle bounds)
    {
        SetSource(color);
        NativeMethods.cairo_rectangle(_cr, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        NativeMethods.cairo_fill(_cr);
    }

    /// <inheritdoc />
    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
    {
        SetSource(color);
        NativeMethods.cairo_set_line_width(_cr, thickness);

        // Offset by half a pixel so a 1px stroke lands on the pixel grid instead of straddling it.
        NativeMethods.cairo_rectangle(_cr, bounds.X + 0.5, bounds.Y + 0.5, bounds.Width - 1, bounds.Height - 1);
        NativeMethods.cairo_stroke(_cr);
    }

    /// <inheritdoc />
    public void FillEllipse(Color color, Rectangle bounds)
    {
        SetSource(color);
        AddEllipsePath(bounds);
        NativeMethods.cairo_fill(_cr);
    }

    /// <inheritdoc />
    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
    {
        SetSource(color);
        NativeMethods.cairo_set_line_width(_cr, thickness);
        AddEllipsePath(bounds);
        NativeMethods.cairo_stroke(_cr);
    }

    /// <inheritdoc />
    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
    {
        SetSource(color);
        NativeMethods.cairo_set_line_width(_cr, thickness);
        NativeMethods.cairo_move_to(_cr, x1 + 0.5, y1 + 0.5);
        NativeMethods.cairo_line_to(_cr, x2 + 0.5, y2 + 0.5);
        NativeMethods.cairo_stroke(_cr);
    }

    /// <inheritdoc />
    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var layout = CreateLayout(text, font);
        try
        {
            NativeMethods.pango_layout_get_pixel_size(layout, out var textWidth, out var textHeight);
            var x = HorizontalOrigin(alignment, bounds, textWidth);
            var y = VerticalOrigin(alignment, bounds, textHeight);

            SetSource(color);
            NativeMethods.cairo_move_to(_cr, x, y);
            NativeMethods.pango_cairo_show_layout(_cr, layout);
        }
        finally
        {
            NativeMethods.g_object_unref(layout);
        }
    }

    /// <inheritdoc />
    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Empty;

        var layout = CreateLayout(text, font);
        try
        {
            NativeMethods.pango_layout_get_pixel_size(layout, out var width, out var height);
            return new Size(width, height);
        }
        finally
        {
            NativeMethods.g_object_unref(layout);
        }
    }

    /// <inheritdoc />
    public void DrawImage(IImage image, Rectangle bounds)
    {
        if (image is not GtkImage native || native.Surface == 0)
            return;

        NativeMethods.cairo_save(_cr);
        NativeMethods.cairo_translate(_cr, bounds.X, bounds.Y);
        if (native.Width > 0 && native.Height > 0)
            NativeMethods.cairo_scale(_cr, (double)bounds.Width / native.Width, (double)bounds.Height / native.Height);

        NativeMethods.cairo_set_source_surface(_cr, native.Surface, 0, 0);
        NativeMethods.cairo_paint(_cr);
        NativeMethods.cairo_restore(_cr);
    }

    /// <inheritdoc />
    public void PushClip(Rectangle bounds)
    {
        NativeMethods.cairo_save(_cr);
        NativeMethods.cairo_rectangle(_cr, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        NativeMethods.cairo_clip(_cr);
    }

    /// <inheritdoc />
    public void PopClip() => NativeMethods.cairo_restore(_cr);

    /// <summary>Adds an ellipse inscribed in <paramref name="bounds"/> to the current path via a scaled unit-circle arc.</summary>
    private void AddEllipsePath(Rectangle bounds)
    {
        NativeMethods.cairo_save(_cr);
        NativeMethods.cairo_translate(_cr, bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        NativeMethods.cairo_scale(_cr, bounds.Width / 2.0, bounds.Height / 2.0);
        NativeMethods.cairo_arc(_cr, 0, 0, 1, 0, 2 * Math.PI);
        NativeMethods.cairo_restore(_cr);
    }

    /// <summary>Sets the Cairo source to a solid color, mapping 0..255 channels onto 0..1.</summary>
    private void SetSource(Color color)
        => NativeMethods.cairo_set_source_rgba(_cr, color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

    /// <summary>Builds a Pango layout for the text in the given font; caller unrefs it.</summary>
    private nint CreateLayout(string text, Font font)
    {
        var layout = NativeMethods.pango_cairo_create_layout(_cr);
        ConfigureLayout(layout, text, font);
        return layout;
    }

    /// <summary>
    /// Loads text and a font description into an existing layout — shared by the per-paint
    /// <see cref="CreateLayout"/> and <see cref="GtkBackend.MeasureText"/>, which brings a layout on a
    /// default Pango context so it can measure without a Cairo surface.
    /// </summary>
    internal static void ConfigureLayout(nint layout, string text, Font font)
    {
        NativeMethods.pango_layout_set_text(layout, text, -1);

        var description = NativeMethods.pango_font_description_new();
        NativeMethods.pango_font_description_set_family(description, font.Family);
        NativeMethods.pango_font_description_set_size(description, (int)(font.SizeInPoints * NativeMethods.PANGO_SCALE));
        NativeMethods.pango_font_description_set_weight(
            description,
            (font.Style & FontStyle.Bold) != 0 ? NativeMethods.PANGO_WEIGHT_BOLD : NativeMethods.PANGO_WEIGHT_NORMAL);
        NativeMethods.pango_font_description_set_style(
            description,
            (font.Style & FontStyle.Italic) != 0 ? NativeMethods.PANGO_STYLE_ITALIC : NativeMethods.PANGO_STYLE_NORMAL);

        NativeMethods.pango_layout_set_font_description(layout, description);
        NativeMethods.pango_font_description_free(description);
    }

    /// <summary>Left edge of text of the given width within <paramref name="bounds"/> for the alignment.</summary>
    private static int HorizontalOrigin(ContentAlignment alignment, Rectangle bounds, int textWidth)
        => alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => bounds.X + (bounds.Width - textWidth) / 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => bounds.Right - textWidth,
            _ => bounds.X,
        };

    /// <summary>Top edge of text of the given height within <paramref name="bounds"/> for the alignment.</summary>
    private static int VerticalOrigin(ContentAlignment alignment, Rectangle bounds, int textHeight)
        => alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => bounds.Y + (bounds.Height - textHeight) / 2,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => bounds.Bottom - textHeight,
            _ => bounds.Y,
        };
}
