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

    /// <summary>Value of <c>GTK_WINDOW_TOPLEVEL</c> — a normal, WM-decorated top-level window.</summary>
    internal const int GTK_WINDOW_TOPLEVEL = 0;

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

    /// <summary>Destroys a widget and drops the toolkit's reference to it.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_widget_destroy(nint widget);

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
