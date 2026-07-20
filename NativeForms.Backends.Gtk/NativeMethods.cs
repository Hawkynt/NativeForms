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

    // --- Window management ----------------------------------------------------------------------

    /// <summary>Value of <c>GDK_WINDOW_TYPE_HINT_NORMAL</c> — an ordinary application window.</summary>
    internal const int GDK_WINDOW_TYPE_HINT_NORMAL = 0;

    /// <summary>Value of <c>GDK_WINDOW_TYPE_HINT_DIALOG</c> — window managers typically drop the
    /// minimize/maximize buttons for it.</summary>
    internal const int GDK_WINDOW_TYPE_HINT_DIALOG = 1;

    /// <summary>Value of <c>GDK_WINDOW_TYPE_HINT_UTILITY</c> — a small persistent tool window.</summary>
    internal const int GDK_WINDOW_TYPE_HINT_UTILITY = 5;

    /// <summary>Value of <c>GDK_HINT_MIN_SIZE</c> — the geometry's minimum size is valid.</summary>
    internal const int GDK_HINT_MIN_SIZE = 1 << 1;

    /// <summary>Value of <c>GDK_HINT_MAX_SIZE</c> — the geometry's maximum size is valid.</summary>
    internal const int GDK_HINT_MAX_SIZE = 1 << 2;

    /// <summary>Whether the user can resize the window (<c>gboolean</c> is passed as 1/0).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_resizable(nint window, int resizable);

    /// <summary>Whether the window manager decorates the window with a frame and title bar.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_decorated(nint window, int setting);

    /// <summary>Advises the window manager what kind of window this is (a <c>GdkWindowTypeHint</c> value).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_type_hint(nint window, int hint);

    /// <summary>Asks the window manager to minimize (iconify) the window.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_iconify(nint window);

    /// <summary>Asks the window manager to restore the window from its minimized state.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_deiconify(nint window);

    /// <summary>Asks the window manager to maximize the window.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_maximize(nint window);

    /// <summary>Asks the window manager to restore the window from its maximized state.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_unmaximize(nint window);

    /// <summary>Asks the window manager to keep the window above all normal windows.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_keep_above(nint window, int setting);

    /// <summary>Sets the widget's overall opacity (0..1); on a top-level this needs a compositor.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_set_opacity(nint widget, double opacity);

    /// <summary>Sets the window's icon from a <c>GdkPixbuf</c> (the window takes its own reference).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_icon(nint window, nint icon);

    /// <summary>Constrains interactive resizing with the valid fields of <paramref name="geometry"/>
    /// (per <paramref name="geomMask"/>, a <c>GdkWindowHints</c> bitmask).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_geometry_hints(nint window, nint geometryWidget, in GdkGeometry geometry, int geomMask);

    /// <summary>
    /// The window-geometry constraints <see cref="gtk_window_set_geometry_hints"/> consumes. Only the
    /// fields whose <c>GdkWindowHints</c> bit is set in the mask are read; layout mirrors the C
    /// struct (eight <c>gint</c>s, two <c>gdouble</c>s, one <c>GdkGravity</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GdkGeometry
    {
        /// <summary>The smallest width the user can resize to.</summary>
        public int MinWidth;

        /// <summary>The smallest height the user can resize to.</summary>
        public int MinHeight;

        /// <summary>The largest width the user can resize to.</summary>
        public int MaxWidth;

        /// <summary>The largest height the user can resize to.</summary>
        public int MaxHeight;

        /// <summary>Ignored here (base size hint).</summary>
        public int BaseWidth;

        /// <summary>Ignored here (base size hint).</summary>
        public int BaseHeight;

        /// <summary>Ignored here (resize increment hint).</summary>
        public int WidthInc;

        /// <summary>Ignored here (resize increment hint).</summary>
        public int HeightInc;

        /// <summary>Ignored here (aspect-ratio hint).</summary>
        public double MinAspect;

        /// <summary>Ignored here (aspect-ratio hint).</summary>
        public double MaxAspect;

        /// <summary>Ignored here (<c>GdkGravity</c> placement hint).</summary>
        public int WinGravity;
    }

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

    // --- Widget images ---------------------------------------------------------------------------

    /// <summary>The image sits left of the button's label (a <c>GtkPositionType</c> value).</summary>
    internal const int GTK_POS_LEFT = 0;

    /// <summary>The image sits right of the button's label.</summary>
    internal const int GTK_POS_RIGHT = 1;

    /// <summary>The image sits above the button's label.</summary>
    internal const int GTK_POS_TOP = 2;

    /// <summary>The image sits below the button's label.</summary>
    internal const int GTK_POS_BOTTOM = 3;

    /// <summary>Creates a <c>GtkImage</c> widget displaying the given Cairo surface.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_image_new_from_surface(nint surface);

    /// <summary>Sets (or with 0 clears) the image widget shown beside a button's label.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_button_set_image(nint button, nint image);

    /// <summary>Positions the button's image relative to its label (a <c>GtkPositionType</c> value).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_button_set_image_position(nint button, int position);

    /// <summary>Shows the button's image even when the theme's <c>gtk-button-images</c> setting is off.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_button_set_always_show_image(nint button, int alwaysShow);

    // --- Text entry (single-line) ---------------------------------------------------------------

    /// <summary>Creates an empty single-line text entry.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_entry_new();

    /// <summary>Replaces the entry's content (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_entry_set_text(nint entry, string text);

    /// <summary>Returns the entry's current text as a UTF-8 pointer owned by the widget — do not free.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_entry_get_text(nint entry);

    /// <summary>Sets the greyed hint shown while the entry is empty and unfocused (UTF-8).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_entry_set_placeholder_text(nint entry, string text);

    /// <summary>Shows the real text (1) or the invisible char (0) — the password-mode switch.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_entry_set_visibility(nint entry, int visible);

    /// <summary>Sets the Unicode code point drawn instead of each character while visibility is off.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_entry_set_invisible_char(nint entry, uint ch);

    /// <summary>Caps the number of characters the entry accepts; 0 means unlimited.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_entry_set_max_length(nint entry, int max);

    /// <summary>Toggles whether the user can edit the widget's text (<c>GtkEditable</c>).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_editable_set_editable(nint editable, int isEditable);

    /// <summary>Selects the characters between the two offsets; -1 means the end of the text.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_editable_select_region(nint editable, int startPos, int endPos);

    /// <summary>Reads the selection bounds in characters (both equal the caret when nothing is selected);
    /// returns <c>TRUE</c> (1) when a non-empty selection exists.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_editable_get_selection_bounds(nint editable, out int startPos, out int endPos);

    // --- Text view (multiline) ------------------------------------------------------------------

    /// <summary>Creates a scrolled window; 0/0 lets it create its own adjustments.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_scrolled_window_new(nint hadjustment, nint vadjustment);

    /// <summary>Creates an empty multiline text view (with its own buffer).</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_text_view_new();

    /// <summary>Returns the <c>GtkTextBuffer</c> the view displays (owned by the view).</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_text_view_get_buffer(nint textView);

    /// <summary>Toggles whether the user can edit the view's buffer.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_view_set_editable(nint textView, int setting);

    /// <summary>Replaces the buffer's content (UTF-8); a length of -1 means NUL-terminated.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_text_buffer_set_text(nint buffer, string text, int len);

    /// <summary>Writes iterators for the very start and very end of the buffer.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_get_bounds(nint buffer, out GtkTextIter start, out GtkTextIter end);

    /// <summary>Returns the text between two iterators as a newly allocated UTF-8 string — free with <see cref="g_free"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_text_buffer_get_text(nint buffer, in GtkTextIter start, in GtkTextIter end, int includeHiddenChars);

    /// <summary>Writes an iterator at the given character offset into the buffer.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_get_iter_at_offset(nint buffer, out GtkTextIter iter, int charOffset);

    /// <summary>Moves the caret (<paramref name="ins"/>) and the selection bound in one operation.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_select_range(nint buffer, in GtkTextIter ins, in GtkTextIter bound);

    /// <summary>Reads the selection bounds (both at the caret when nothing is selected); returns <c>TRUE</c> (1)
    /// when a non-empty selection exists.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_text_buffer_get_selection_bounds(nint buffer, out GtkTextIter start, out GtkTextIter end);

    /// <summary>Returns the character offset of an iterator within its buffer.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_text_iter_get_offset(in GtkTextIter iter);

    /// <summary>
    /// An opaque, stack-allocatable position inside a <c>GtkTextBuffer</c>. The fields are private
    /// implementation details of GTK; only the overall size — as declared in <c>gtktextiter.h</c>
    /// (14 dummy fields: pointers and ints) — matters, so the struct can live on the managed stack
    /// and be passed by reference.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GtkTextIter
    {
        private nint _dummy1, _dummy2;
        private int _dummy3, _dummy4, _dummy5, _dummy6, _dummy7, _dummy8;
        private nint _dummy9, _dummy10;
        private int _dummy11, _dummy12, _dummy13;
        private nint _dummy14;
    }

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

    /// <summary>
    /// Registers <paramref name="function"/> (a <c>GSourceFunc</c> function pointer) to be invoked
    /// once by the main loop when it is idle, threading <paramref name="data"/> through as the
    /// callback's <c>user_data</c>. The callback returns 0 (<c>G_SOURCE_REMOVE</c>) to run exactly
    /// once. <paramref name="notify"/> is a <c>GDestroyNotify</c> function pointer (0 = none).
    /// Returns the source id.
    /// </summary>
    [LibraryImport(GLib)]
    internal static partial uint g_idle_add_full(int priority, nint function, nint data, nint notify);

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
