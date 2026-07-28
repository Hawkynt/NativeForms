using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Writes PNGs of what the gallery actually renders, entirely inside the process: every mapped
/// toplevel is asked to paint itself into a Cairo image surface with <c>gtk_widget_draw</c>, the
/// surfaces are composited at their screen offsets, and the result is written with
/// <c>cairo_surface_write_to_png</c>.
/// </summary>
/// <remarks>
/// The obvious alternative — shelling out to ImageMagick's <c>import</c>, <c>xwd</c> or a compositor
/// screenshot portal — is not available everywhere the walkthrough has to run: an <c>import</c> built
/// without its X11 delegate exits zero having written nothing, and a rootless Xwayland server hands
/// the actual pixels to the Wayland compositor rather than to any X client. Asking the widgets to
/// paint sidesteps the display server altogether, which also makes the capture deterministic: it is
/// the toolkit's own draw pipeline, not whatever happened to be stacked on the desktop.
/// <para>
/// Drop-downs, menus and modal dialogs are separate toplevels stacked over the gallery, so a capture
/// unions their screen rectangles with the main window's and paints each at its own offset. That is
/// what makes an open combo list or a modal message box show up in the shot at all. A popup is drawn
/// through its single child rather than through the toplevel, which renders nothing for it; an
/// ordinary window is drawn through the toplevel.
/// </para>
/// <para>
/// Each popup is drawn at the origin its own <c>GdkWindow</c> reports, never at the anchor the
/// toolkit asked for, so a shot is evidence about placement rather than a restatement of the
/// request: a drop-down that the display server put somewhere else photographs somewhere else.
/// </para>
/// Every entry point must be called on the UI thread.
/// </remarks>
internal static unsafe partial class Capture
{
    private const string _Gtk = "libgtk-3.so.0";
    private const string _Gdk = "libgdk-3.so.0";
    private const string _Cairo = "libcairo.so.2";

    /// <summary>Value of <c>CAIRO_FORMAT_RGB24</c> — 32-bit, no alpha channel, native-endian.</summary>
    private const int _CairoFormatRgb24 = 1;

    /// <summary>Value of <c>CAIRO_STATUS_SUCCESS</c>.</summary>
    private const int _CairoStatusSuccess = 0;

    /// <summary>The widest capture we will attempt, so a nonsense geometry cannot ask for gigabytes.</summary>
    private const int _MaxExtent = 8192;

    /// <summary>Value of <c>GTK_WINDOW_POPUP</c> — an undecorated floating surface.</summary>
    private const int _GtkWindowPopup = 1;

    // --- GTK / GDK / Cairo entry points --------------------------------------------------------

    /// <summary>Asks a widget to paint its whole allocation into a Cairo context, exactly the way the
    /// toolkit's own "draw" signal would — children, theme and all.</summary>
    [LibraryImport(_Gtk)]
    private static partial void gtk_widget_draw(nint widget, nint cr);

    /// <summary>Blocks until the widget's pending frame has actually been drawn, so a capture taken
    /// right after a gesture sees the finished frame rather than the one before it.</summary>
    [LibraryImport(_Gtk)]
    private static partial void gtk_test_widget_wait_for_draw(nint widget);

    [LibraryImport(_Gtk)]
    private static partial nint gtk_window_list_toplevels();

    [LibraryImport(_Gtk)]
    private static partial nint gtk_widget_get_window(nint widget);

    [LibraryImport(_Gtk)]
    private static partial int gtk_widget_get_mapped(nint widget);

    /// <summary>The single child of a <c>GtkBin</c> — every <c>GtkWindow</c> is one.</summary>
    [LibraryImport(_Gtk)]
    private static partial nint gtk_bin_get_child(nint bin);

    /// <summary>Whether a window is an ordinary toplevel or an undecorated floating surface.</summary>
    [LibraryImport(_Gtk)]
    private static partial int gtk_window_get_window_type(nint window);

    [LibraryImport(_Gdk)]
    private static partial void gdk_window_get_origin(nint window, out int x, out int y);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_get_width(nint window);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_get_height(nint window);

    [LibraryImport("libglib-2.0.so.0")]
    private static partial uint g_list_length(nint list);

    [LibraryImport("libglib-2.0.so.0")]
    private static partial nint g_list_nth_data(nint list, uint n);

    [LibraryImport("libglib-2.0.so.0")]
    private static partial void g_list_free(nint list);

    /// <summary>Allocates an image surface Cairo owns the pixels of.</summary>
    [LibraryImport(_Cairo)]
    private static partial nint cairo_image_surface_create(int format, int width, int height);

    /// <summary>Creates a drawing context targeting a surface; caller destroys it.</summary>
    [LibraryImport(_Cairo)]
    private static partial nint cairo_create(nint surface);

    /// <summary>Drops one reference to a drawing context.</summary>
    [LibraryImport(_Cairo)]
    private static partial void cairo_destroy(nint cr);

    /// <summary>Drops one reference to a surface.</summary>
    [LibraryImport(_Cairo)]
    private static partial void cairo_surface_destroy(nint surface);

    /// <summary>Flushes any pending drawing so the pixel data is coherent before it is written out.</summary>
    [LibraryImport(_Cairo)]
    private static partial void cairo_surface_flush(nint surface);

    /// <summary>Writes a surface out as a PNG, returning a <c>cairo_status_t</c>.</summary>
    [LibraryImport(_Cairo, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int cairo_surface_write_to_png(nint surface, string filename);

    [LibraryImport(_Cairo)]
    private static partial void cairo_save(nint cr);

    [LibraryImport(_Cairo)]
    private static partial void cairo_restore(nint cr);

    [LibraryImport(_Cairo)]
    private static partial void cairo_translate(nint cr, double tx, double ty);

    [LibraryImport(_Cairo)]
    private static partial void cairo_set_source_rgb(nint cr, double red, double green, double blue);

    [LibraryImport(_Cairo)]
    private static partial void cairo_paint(nint cr);

    // --- Capture ---------------------------------------------------------------------------------

    /// <summary>One toplevel to composite: the widget that paints and where it sits on screen.</summary>
    private readonly record struct Layer(nint Widget, Rectangle Bounds);

    /// <summary>
    /// Renders every mapped toplevel into one PNG at <paramref name="path"/> and reports the pixel
    /// size written, or <see langword="null"/> when there was nothing mappable to draw.
    /// </summary>
    /// <param name="mainWindow">The gallery's own <c>GdkWindow</c>, which anchors the composition —
    /// a popup that never overlaps it still widens the frame rather than replacing it.</param>
    internal static Size? Toplevels(nint mainWindow, string path)
    {
        var layers = MappedLayers(mainWindow);
        if (layers.Count == 0)
            return null;

        // Anchor on the main window — it sorts first — so a shot always frames the gallery, then
        // widen to whatever the stacked popups need: a drop-down may hang below the bottom edge.
        var frame = layers[0].Bounds;
        foreach (var layer in layers)
            frame = Rectangle.Union(frame, layer.Bounds);

        if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > _MaxExtent || frame.Height > _MaxExtent)
            return null;

        // Let each toplevel finish the frame it owes before anyone reads its pixels; otherwise a
        // capture taken straight after a gesture records the state before the relayout.
        foreach (var layer in layers)
            gtk_test_widget_wait_for_draw(layer.Widget);

        var surface = cairo_image_surface_create(_CairoFormatRgb24, frame.Width, frame.Height);
        var cr = cairo_create(surface);
        try
        {
            // RGB24 starts out black and a widget that paints no background would keep it, so lay
            // down the neutral grey a desktop would have shown behind the window.
            cairo_set_source_rgb(cr, 0.36, 0.36, 0.38);
            cairo_paint(cr);

            foreach (var layer in layers)
            {
                cairo_save(cr);
                cairo_translate(cr, layer.Bounds.X - frame.X, layer.Bounds.Y - frame.Y);
                // A GTK_WINDOW_POPUP renders nothing through its own toplevel — no warning, just an
                // empty rectangle where the drop-down should be — so for those the surface that
                // actually holds the menu, the window's single child, is drawn instead. An ordinary
                // toplevel paints itself correctly and must not be drawn twice: its child sits at a
                // different offset inside it, so a second pass stamps a ghost copy of every widget a
                // few pixels away, which is what a message box's buttons used to look like.
                if (gtk_window_get_window_type(layer.Widget) == _GtkWindowPopup)
                {
                    var content = gtk_bin_get_child(layer.Widget);
                    if (content != 0)
                        gtk_widget_draw(content, cr);
                }
                else
                    gtk_widget_draw(layer.Widget, cr);

                cairo_restore(cr);
            }

            cairo_surface_flush(surface);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            return cairo_surface_write_to_png(surface, path) == _CairoStatusSuccess
                ? frame.Size
                : null;
        }
        finally
        {
            cairo_destroy(cr);
            cairo_surface_destroy(surface);
        }
    }

    /// <summary>
    /// Every mapped toplevel with its screen rectangle, bottom of the stack first — the gallery, then
    /// whatever is floating over it.
    /// </summary>
    /// <remarks>
    /// The order matters more than it looks: the layers are painted in sequence onto one surface, so
    /// the last one drawn wins the overlap. <c>gtk_window_list_toplevels</c> reports oldest first,
    /// which already puts the gallery ahead of the drop-downs and menus opened later — walking it
    /// backwards, as this did, painted the gallery <em>over</em> every popup and erased it from the
    /// shot, which is why an open combo list photographed as a closed one. Rather than trust either
    /// order, the window named by <paramref name="mainWindow"/> is moved to the front explicitly and
    /// the rest keep the order GTK gave, so a cascade's parent still precedes its submenu.
    /// </remarks>
    private static List<Layer> MappedLayers(nint mainWindow)
    {
        var result = new List<Layer>();
        var list = gtk_window_list_toplevels();
        var count = g_list_length(list);
        for (var i = 0u; i < count; ++i)
        {
            var widget = g_list_nth_data(list, i);
            if (widget == 0 || gtk_widget_get_mapped(widget) == 0)
                continue;

            var window = gtk_widget_get_window(widget);
            if (window == 0)
                continue;

            gdk_window_get_origin(window, out var x, out var y);
            var bounds = new Rectangle(x, y, gdk_window_get_width(window), gdk_window_get_height(window));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            var layer = new Layer(widget, bounds);
            if (window == mainWindow)
                result.Insert(0, layer);
            else
                result.Add(layer);
        }

        g_list_free(list);
        return result;
    }
}
