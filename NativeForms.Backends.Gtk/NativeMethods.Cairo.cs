using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The Cairo and Pango entry points the GTK backend draws with. Cairo provides the immediate-mode
/// vector surface handed to each "draw" callback; Pango (via PangoCairo) lays out and renders text.
/// Bound with <see cref="LibraryImportAttribute"/> so nothing is marshalled by reflection at runtime.
/// </summary>
internal static partial class NativeMethods
{
    private const string Cairo = "libcairo.so.2";
    private const string Pango = "libpango-1.0.so.0";
    private const string PangoCairo = "libpangocairo-1.0.so.0";

    /// <summary>Value of <c>CAIRO_FORMAT_ARGB32</c> — 32-bit, premultiplied, native-endian ARGB.</summary>
    internal const int CAIRO_FORMAT_ARGB32 = 0;

    /// <summary>Points-to-Pango-units factor (<c>PANGO_SCALE</c>).</summary>
    internal const int PANGO_SCALE = 1024;

    /// <summary>Value of <c>PANGO_WEIGHT_NORMAL</c>.</summary>
    internal const int PANGO_WEIGHT_NORMAL = 400;

    /// <summary>Value of <c>PANGO_WEIGHT_BOLD</c>.</summary>
    internal const int PANGO_WEIGHT_BOLD = 700;

    /// <summary>Value of <c>PANGO_STYLE_NORMAL</c> (upright).</summary>
    internal const int PANGO_STYLE_NORMAL = 0;

    /// <summary>Value of <c>PANGO_STYLE_ITALIC</c>.</summary>
    internal const int PANGO_STYLE_ITALIC = 2;

    // --- Cairo image surfaces -------------------------------------------------------------------

    /// <summary>Wraps caller-owned pixel <paramref name="data"/> in a Cairo image surface (no copy).</summary>
    [LibraryImport(Cairo)]
    internal static partial nint cairo_image_surface_create_for_data(nint data, int format, int width, int height, int stride);

    /// <summary>Returns the byte stride a row of <paramref name="width"/> pixels needs for <paramref name="format"/>.</summary>
    [LibraryImport(Cairo)]
    internal static partial int cairo_format_stride_for_width(int format, int width);

    /// <summary>Marks the whole surface dirty after its backing pixel data was written directly.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_surface_mark_dirty(nint surface);

    /// <summary>Drops one reference to a Cairo surface, freeing it when the last reference goes.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_surface_destroy(nint surface);

    // --- Cairo drawing state --------------------------------------------------------------------

    /// <summary>Saves the current graphics state (source, clip, transform) onto Cairo's stack.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_save(nint cr);

    /// <summary>Restores the graphics state saved by the matching <see cref="cairo_save"/>.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_restore(nint cr);

    /// <summary>Sets the current source to a solid RGBA color (components in 0..1).</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_set_source_rgba(nint cr, double red, double green, double blue, double alpha);

    /// <summary>Uses <paramref name="surface"/> as the current source, offset by (<paramref name="x"/>, <paramref name="y"/>).</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_set_source_surface(nint cr, nint surface, double x, double y);

    /// <summary>Sets the line width used by <see cref="cairo_stroke"/>.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_set_line_width(nint cr, double width);

    /// <summary>Adds a rectangle sub-path to the current path.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_rectangle(nint cr, double x, double y, double width, double height);

    /// <summary>Begins a new sub-path at the given point.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_move_to(nint cr, double x, double y);

    /// <summary>Adds a line to the current path from the current point to the given point.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_line_to(nint cr, double x, double y);

    /// <summary>Adds a circular arc sub-path centered at (<paramref name="xc"/>, <paramref name="yc"/>).</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_arc(nint cr, double xc, double yc, double radius, double angle1, double angle2);

    /// <summary>Closes the current sub-path with a straight line back to its starting point.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_close_path(nint cr);

    /// <summary>Fills the current path with the current source and clears the path.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_fill(nint cr);

    /// <summary>Strokes the current path with the current source/line width and clears the path.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_stroke(nint cr);

    /// <summary>Intersects the clip region with the current path, then clears the path.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_clip(nint cr);

    /// <summary>Paints the current source everywhere within the clip region.</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_paint(nint cr);

    /// <summary>Translates the coordinate system by (<paramref name="tx"/>, <paramref name="ty"/>).</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_translate(nint cr, double tx, double ty);

    /// <summary>Scales the coordinate system by (<paramref name="sx"/>, <paramref name="sy"/>).</summary>
    [LibraryImport(Cairo)]
    internal static partial void cairo_scale(nint cr, double sx, double sy);

    // --- Pango text layout ----------------------------------------------------------------------

    /// <summary>Creates a <c>PangoLayout</c> wired to the Cairo context's font options and resolution.</summary>
    [LibraryImport(PangoCairo)]
    internal static partial nint pango_cairo_create_layout(nint cr);

    /// <summary>Renders a laid-out <c>PangoLayout</c> at the Cairo current point using the current source.</summary>
    [LibraryImport(PangoCairo)]
    internal static partial void pango_cairo_show_layout(nint cr, nint layout);

    /// <summary>Re-syncs a layout with a Cairo context's font options and resolution — the call that
    /// lets one long-lived layout be reused across draw callbacks with differing contexts.</summary>
    [LibraryImport(PangoCairo)]
    internal static partial void pango_cairo_update_layout(nint cr, nint layout);

    /// <summary>Returns the default PangoCairo font map (owned by Pango; do not unref).</summary>
    [LibraryImport(PangoCairo)]
    internal static partial nint pango_cairo_font_map_get_default();

    /// <summary>Creates a new <c>PangoContext</c> connected to the font map; caller unrefs it.</summary>
    [LibraryImport(Pango)]
    internal static partial nint pango_font_map_create_context(nint fontMap);

    /// <summary>Creates an empty <c>PangoLayout</c> for the given context; caller unrefs it.</summary>
    [LibraryImport(Pango)]
    internal static partial nint pango_layout_new(nint context);

    /// <summary>Sets the layout's text; <paramref name="length"/> of -1 means the whole NUL-terminated string.</summary>
    [LibraryImport(Pango, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void pango_layout_set_text(nint layout, string text, int length);

    /// <summary>Sets the font a layout renders with (a copy is taken; the description may be freed after).</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_layout_set_font_description(nint layout, nint description);

    /// <summary>Reads the layout's rendered size, rounded up to whole device pixels.</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_layout_get_pixel_size(nint layout, out int width, out int height);

    /// <summary>Allocates an empty, mutable <c>PangoFontDescription</c>.</summary>
    [LibraryImport(Pango)]
    internal static partial nint pango_font_description_new();

    /// <summary>Frees a <c>PangoFontDescription</c>.</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_font_description_free(nint description);

    /// <summary>Sets the font family name (UTF-8).</summary>
    [LibraryImport(Pango, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void pango_font_description_set_family(nint description, string family);

    /// <summary>Sets the font size in Pango units (points * <see cref="PANGO_SCALE"/>).</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_font_description_set_size(nint description, int size);

    /// <summary>Sets the font weight (for example <see cref="PANGO_WEIGHT_BOLD"/>).</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_font_description_set_weight(nint description, int weight);

    /// <summary>Sets the font slant (for example <see cref="PANGO_STYLE_ITALIC"/>).</summary>
    [LibraryImport(Pango)]
    internal static partial void pango_font_description_set_style(nint description, int style);
}
