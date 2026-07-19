using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The complete set of GTK 3, GObject and GLib entry points this backend needs, bound with
/// source-generated <see cref="LibraryImportAttribute"/> stubs so nothing is marshalled by
/// reflection at runtime — keeping the assembly trim- and NativeAOT-safe. All widget and
/// <c>gpointer</c> handles are represented as <see cref="nint"/>; GTK strings are UTF-8.
/// </summary>
internal static partial class NativeMethods
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    /// <summary>Value of <c>GTK_WINDOW_TOPLEVEL</c> — a normal, WM-decorated top-level window.</summary>
    internal const int GTK_WINDOW_TOPLEVEL = 0;

    /// <summary>Value of <c>GTK_WINDOW_POPUP</c> — an undecorated surface the WM ignores (menus, tooltips).</summary>
    internal const int GTK_WINDOW_POPUP = 1;

    /// <summary>Value of <c>GTK_STATE_FLAG_NORMAL</c> — the default, unmodified widget state.</summary>
    internal const uint GTK_STATE_FLAG_NORMAL = 0;

    // --- Library init and main loop -------------------------------------------------------------

    /// <summary>Initializes GTK. Passing <c>0, 0</c> (NULL argc/argv) is the supported no-args form.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_init(nint argc, nint argv);

    /// <summary>Runs the GTK main loop until <see cref="gtk_main_quit"/> is called.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_main();

    /// <summary>Makes the innermost invocation of <see cref="gtk_main"/> return.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_main_quit();

    // --- Windows and containers -----------------------------------------------------------------

    /// <summary>Creates a new <c>GtkWindow</c> of the given <c>GtkWindowType</c>.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_window_new(int type);

    /// <summary>Creates a new <c>GtkFixed</c> container that positions children by absolute coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_fixed_new();

    /// <summary>Adds <paramref name="widget"/> to the single-child container <paramref name="container"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_container_add(nint container, nint widget);

    /// <summary>Sets the title-bar caption of a window (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_window_set_title(nint window, string title);

    /// <summary>Sets the size a window requests when it is first shown.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_default_size(nint window, int width, int height);

    /// <summary>Resizes an already-created window to the given client size.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_resize(nint window, int width, int height);

    /// <summary>Places <paramref name="widget"/> in a <c>GtkFixed</c> at the given coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_fixed_put(nint fixed_, nint widget, int x, int y);

    /// <summary>Moves a child already inside a <c>GtkFixed</c> to new coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_fixed_move(nint fixed_, nint widget, int x, int y);

    /// <summary>Moves a top-level window to the given root-window (screen) coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_move(nint window, int x, int y);

    // --- Generic widget operations --------------------------------------------------------------

    /// <summary>Recursively shows <paramref name="widget"/> and all of its descendants.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_show_all(nint widget);

    /// <summary>Shows or hides a widget (<c>gboolean</c> is passed as 1/0).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_visible(nint widget, int visible);

    /// <summary>Enables or greys out a widget for interaction (<c>gboolean</c> is passed as 1/0).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_sensitive(nint widget, int sensitive);

    /// <summary>Requests a minimum/fixed size for a widget; -1 means "natural size".</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_size_request(nint widget, int width, int height);

    /// <summary>Hides a widget without destroying it.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_hide(nint widget);

    /// <summary>Destroys a widget and drops the toolkit's reference to it.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_destroy(nint widget);

    /// <summary>Returns the widget's <c>GdkWindow</c>, or 0 before the widget is realized.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_widget_get_window(nint widget);

    /// <summary>Whether the widget has its own <c>GdkWindow</c> rather than sharing its parent's (<c>gboolean</c>).</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_widget_get_has_window(nint widget);

    /// <summary>Reads the widget's current allocation — its position and size within its parent's window.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_get_allocation(nint widget, out GdkRectangle allocation);

    /// <summary>Makes <paramref name="widget"/> the target of all the application's events (a GTK grab).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_grab_add(nint widget);

    /// <summary>Removes the application-wide event grab added by <see cref="gtk_grab_add"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_grab_remove(nint widget);

    // --- Buttons and labels ---------------------------------------------------------------------

    /// <summary>Creates a push button carrying the given label text (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_button_new_with_label(string label);

    /// <summary>Sets the label text of a button (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_button_set_label(nint button, string label);

    /// <summary>Creates a static-text label carrying the given text (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_label_new(string str);

    /// <summary>Sets the text of a label (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_label_set_text(nint label, string str);

    /// <summary>Sets the text of a label (UTF-8), underlining the character after each <c>_</c> as a mnemonic.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_label_set_text_with_mnemonic(nint label, string str);

    /// <summary>Sets the horizontal alignment of a label's text (0 = left, 0.5 = center, 1 = right).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_label_set_xalign(nint label, float xalign);

    /// <summary>Sets the vertical alignment of a label's text (0 = top, 0.5 = middle, 1 = bottom).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_label_set_yalign(nint label, float yalign);

    // --- Owner-draw canvas ----------------------------------------------------------------------

    /// <summary>
    /// Sets whether the widget creates its own <c>GdkWindow</c> when realized. Must be called before
    /// realization; the canvas needs it so a (normally window-less) <c>GtkFixed</c> can receive input
    /// events and clip its drawing.
    /// </summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_has_window(nint widget, int hasWindow);

    /// <summary>Marks the widget as painting every pixel itself so the theme leaves its background alone.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_app_paintable(nint widget, int appPaintable);

    /// <summary>Sets whether a widget accepts keyboard focus (<c>gboolean</c> is passed as 1/0).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_can_focus(nint widget, int canFocus);

    /// <summary>Adds the given <c>GdkEventMask</c> bits to those the widget's window receives.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_add_events(nint widget, int events);

    /// <summary>Schedules a full repaint of the widget.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_queue_draw(nint widget);

    /// <summary>Schedules a repaint of the given rectangle in widget-relative coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_queue_draw_area(nint widget, int x, int y, int width, int height);

    /// <summary>Moves keyboard focus to the widget (it must be focusable and realized).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_grab_focus(nint widget);

    /// <summary>Returns the widget's current allocated width in pixels.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_widget_get_allocated_width(nint widget);

    /// <summary>Returns the widget's current allocated height in pixels.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_widget_get_allocated_height(nint widget);

    // --- Theming (GtkStyleContext / GtkSettings) ------------------------------------------------

    /// <summary>Returns the (owned-by-widget) <c>GtkStyleContext</c> used to resolve theme colors.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_widget_get_style_context(nint widget);

    /// <summary>Reads the foreground (text) color for the given <c>GtkStateFlags</c> into <paramref name="color"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_style_context_get_color(nint context, uint state, out GdkRGBA color);

    /// <summary>
    /// Looks up a named theme color (for example <c>"theme_bg_color"</c>) in the style context,
    /// writing it to <paramref name="color"/> and returning <c>TRUE</c> (1) when the name resolves.
    /// </summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gtk_style_context_lookup_color(nint context, string colorName, out GdkRGBA color);

    /// <summary>Returns the default <c>GtkSettings</c> object for the current screen.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_settings_get_default();

    // --- GObject / GLib helpers -----------------------------------------------------------------

    /// <summary>
    /// Reads a single object property. Declared with a fixed (non-variadic) signature — the trailing
    /// <c>NULL</c> terminator is passed explicitly — which matches the C ABI for the one string
    /// property we read (<c>gtk-font-name</c>) and stays <c>LibraryImport</c>-compatible.
    /// </summary>
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void g_object_get(nint @object, string firstPropertyName, out nint value, nint terminator);

    /// <summary>Drops one reference to a <c>GObject</c>.</summary>
    [LibraryImport(GObject)]
    internal static partial void g_object_unref(nint @object);

    /// <summary>Frees memory allocated by GLib (for example the string returned by <see cref="g_object_get"/>).</summary>
    [LibraryImport(GLib)]
    internal static partial void g_free(nint mem);

    // --- Timers ---------------------------------------------------------------------------------

    /// <summary>Value of <c>G_PRIORITY_DEFAULT</c> — the priority ordinary main-loop sources run at.</summary>
    internal const int G_PRIORITY_DEFAULT = 0;

    /// <summary>
    /// Registers <paramref name="function"/> (a <c>GSourceFunc</c> function pointer) to be invoked by
    /// the main loop every <paramref name="interval"/> milliseconds, threading <paramref name="data"/>
    /// through as the callback's <c>user_data</c>. The source keeps firing while the callback returns
    /// 1 (<c>G_SOURCE_CONTINUE</c>). <paramref name="notify"/> is a <c>GDestroyNotify</c> function
    /// pointer (0 = none). Returns the source id for <see cref="g_source_remove"/>.
    /// </summary>
    [LibraryImport(GLib)]
    internal static partial uint g_timeout_add_full(int priority, uint interval, nint function, nint data, nint notify);

    /// <summary>Removes the main-loop source with the given id, stopping its callbacks. Returns <c>TRUE</c> (1) when found.</summary>
    [LibraryImport(GLib)]
    internal static partial int g_source_remove(uint tag);

    // --- Signals --------------------------------------------------------------------------------

    /// <summary>
    /// Connects <paramref name="cHandler"/> (a C function pointer) to the named signal on
    /// <paramref name="instance"/>, threading <paramref name="data"/> through as the callback's
    /// <c>user_data</c>. <paramref name="destroyData"/> is a <c>GClosureNotify</c> function pointer
    /// (0 = none) and <paramref name="connectFlags"/> is <c>GConnectFlags</c> (0 = default). The
    /// returned handler id (<c>gulong</c>) is ignored here.
    /// </summary>
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint g_signal_connect_data(
        nint instance,
        string detailedSignal,
        nint cHandler,
        nint data,
        nint destroyData,
        int connectFlags);
}
