using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The rich-text slice of the GTK binding: <c>GtkTextTag</c> creation and application, the typed
/// <c>GValue</c> property machinery (used instead of the variadic <c>g_object_set</c>, whose
/// double-typed varargs are off-limits to source-generated P/Invoke), and the tag-toggle iteration
/// the RTF export walks. Kept in a separate partial so the widget surface stays focused.
/// </summary>
internal static partial class NativeMethods
{
    // --- Fundamental GTypes (gtype.h: fundamental id << 2) ---

    /// <summary>Value of <c>G_TYPE_BOOLEAN</c>.</summary>
    internal const nuint G_TYPE_BOOLEAN = 5 << 2;

    /// <summary>Value of <c>G_TYPE_INT</c>.</summary>
    internal const nuint G_TYPE_INT = 6 << 2;

    /// <summary>Value of <c>G_TYPE_DOUBLE</c>.</summary>
    internal const nuint G_TYPE_DOUBLE = 15 << 2;

    /// <summary>Value of <c>G_TYPE_STRING</c>.</summary>
    internal const nuint G_TYPE_STRING = 16 << 2;

    // --- Pango / GTK enum values used as tag properties ---

    /// <summary>Value of <c>PANGO_UNDERLINE_SINGLE</c>.</summary>
    internal const int PANGO_UNDERLINE_SINGLE = 1;

    /// <summary>Value of <c>GTK_JUSTIFY_LEFT</c>.</summary>
    internal const int GTK_JUSTIFY_LEFT = 0;

    /// <summary>Value of <c>GTK_JUSTIFY_RIGHT</c>.</summary>
    internal const int GTK_JUSTIFY_RIGHT = 1;

    /// <summary>Value of <c>GTK_JUSTIFY_CENTER</c>.</summary>
    internal const int GTK_JUSTIFY_CENTER = 2;

    /// <summary>
    /// A <c>GValue</c>: the type tag plus two data words. Only ever touched through the
    /// <c>g_value_*</c> accessors below, so the union stays opaque.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GValue
    {
        /// <summary>The <c>GType</c> of the held value (0 = unset).</summary>
        public nuint g_type;

        /// <summary>First data word of the value union.</summary>
        public long data0;

        /// <summary>Second data word of the value union.</summary>
        public long data1;
    }

    /// <summary>A singly-linked GLib list cell (<c>GSList</c>), read in place from native memory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GSList
    {
        /// <summary>The element carried by this cell.</summary>
        public nint data;

        /// <summary>The next cell, or 0 at the end.</summary>
        public nint next;
    }

    // --- GValue accessors (all non-variadic — the AOT-safe property path) ---

    /// <summary>Initializes a zeroed <c>GValue</c> to hold the given type; returns the value itself.</summary>
    [LibraryImport(GObject)]
    internal static partial nint g_value_init(ref GValue value, nuint gType);

    /// <summary>Stores an <c>int</c> in the value.</summary>
    [LibraryImport(GObject)]
    internal static partial void g_value_set_int(ref GValue value, int v);

    /// <summary>Stores a <c>double</c> in the value.</summary>
    [LibraryImport(GObject)]
    internal static partial void g_value_set_double(ref GValue value, double v);

    /// <summary>Stores a <c>gboolean</c> in the value (1/0).</summary>
    [LibraryImport(GObject)]
    internal static partial void g_value_set_boolean(ref GValue value, int v);

    /// <summary>Stores a copied string in the value (UTF-8).</summary>
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void g_value_set_string(ref GValue value, string v);

    /// <summary>Clears the value, releasing anything it holds (the copied string).</summary>
    [LibraryImport(GObject)]
    internal static partial void g_value_unset(ref GValue value);

    /// <summary>Sets one object property from a <c>GValue</c> — the fixed-signature alternative to <c>g_object_set</c>.</summary>
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void g_object_set_property(nint @object, string propertyName, in GValue value);

    /// <summary>Frees the cells of a <c>GSList</c> (not the elements they point to).</summary>
    [LibraryImport(GLib)]
    internal static partial void g_slist_free(nint list);

    // --- Text tags -------------------------------------------------------------------------------

    /// <summary>Returns the buffer's tag table (owned by the buffer).</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_text_buffer_get_tag_table(nint buffer);

    /// <summary>Looks a tag up by name; returns 0 when the table has none of that name.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_text_tag_table_lookup(nint table, string name);

    /// <summary>
    /// Creates a named tag in the buffer's table. Declared with an explicit <c>NULL</c> terminator
    /// and no further varargs — properties are set afterwards through
    /// <see cref="g_object_set_property"/>, keeping doubles out of the variadic call.
    /// </summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_text_buffer_create_tag(nint buffer, string name, nint terminator);

    /// <summary>Applies a tag to the characters between the two iterators.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_apply_tag(nint buffer, nint tag, in GtkTextIter start, in GtkTextIter end);

    /// <summary>Removes a tag from the characters between the two iterators.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_remove_tag(nint buffer, nint tag, in GtkTextIter start, in GtkTextIter end);

    /// <summary>Inserts text at the iterator (which afterwards points past the insertion); -1 length means NUL-terminated.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_text_buffer_insert(nint buffer, ref GtkTextIter iter, string text, int len);

    /// <summary>Deletes the characters between the two iterators (both are revalidated to the deletion point).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_buffer_delete(nint buffer, ref GtkTextIter start, ref GtkTextIter end);

    /// <summary>
    /// Advances the iterator to the next spot where any tag (0) toggles on or off; returns
    /// <c>FALSE</c> (0) — leaving the iterator at the buffer end — when there is none.
    /// </summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_text_iter_forward_to_tag_toggle(ref GtkTextIter iter, nint tag);

    /// <summary>Returns the tags active at the iterator as a newly allocated <see cref="GSList"/> — free with <see cref="g_slist_free"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_text_iter_get_tags(in GtkTextIter iter);

    /// <summary>Whether the iterator is inside a range tagged with <paramref name="tag"/> (<c>gboolean</c>).</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_text_iter_has_tag(in GtkTextIter iter, nint tag);

    // --- Text-view hit testing -------------------------------------------------------------------

    /// <summary>Value of <c>GTK_TEXT_WINDOW_WIDGET</c> — coordinates relative to the whole text-view widget.</summary>
    internal const int GTK_TEXT_WINDOW_WIDGET = 1;

    /// <summary>Converts widget-window coordinates into buffer coordinates (which scrolling offsets).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_view_window_to_buffer_coords(nint textView, int windowType, int windowX, int windowY, out int bufferX, out int bufferY);

    /// <summary>Writes an iterator at the character closest to the buffer coordinates.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_text_view_get_iter_at_location(nint textView, out GtkTextIter iter, int x, int y);
}
