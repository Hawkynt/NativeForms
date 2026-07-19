using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK dialog surface: modal window plumbing, <c>GtkMessageDialog</c>, <c>GtkFileChooserDialog</c>
/// and the color/font choosers. The GTK constructors here are C-variadic; they are bound with fixed
/// signatures whose trailing argument is the explicit <c>NULL</c> terminator — the same technique
/// (and ABI reasoning) as the <c>g_object_get</c> binding in <c>NativeMethods.cs</c>.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Value of <c>GTK_DIALOG_MODAL</c>.</summary>
    internal const int GTK_DIALOG_MODAL = 1;

    // --- GtkMessageType ---

    /// <summary>Value of <c>GTK_MESSAGE_INFO</c>.</summary>
    internal const int GTK_MESSAGE_INFO = 0;

    /// <summary>Value of <c>GTK_MESSAGE_WARNING</c>.</summary>
    internal const int GTK_MESSAGE_WARNING = 1;

    /// <summary>Value of <c>GTK_MESSAGE_QUESTION</c>.</summary>
    internal const int GTK_MESSAGE_QUESTION = 2;

    /// <summary>Value of <c>GTK_MESSAGE_ERROR</c>.</summary>
    internal const int GTK_MESSAGE_ERROR = 3;

    /// <summary>Value of <c>GTK_MESSAGE_OTHER</c> — no icon.</summary>
    internal const int GTK_MESSAGE_OTHER = 4;

    /// <summary>Value of <c>GTK_BUTTONS_NONE</c> — buttons are added individually.</summary>
    internal const int GTK_BUTTONS_NONE = 0;

    // --- GtkResponseType ---

    /// <summary>Value of <c>GTK_RESPONSE_ACCEPT</c> — the affirmative file-chooser button.</summary>
    internal const int GTK_RESPONSE_ACCEPT = -3;

    /// <summary>Value of <c>GTK_RESPONSE_OK</c> — the affirmative color/font-chooser button.</summary>
    internal const int GTK_RESPONSE_OK = -5;

    /// <summary>Value of <c>GTK_RESPONSE_CANCEL</c>.</summary>
    internal const int GTK_RESPONSE_CANCEL = -6;

    // --- GtkFileChooserAction ---

    /// <summary>Value of <c>GTK_FILE_CHOOSER_ACTION_OPEN</c>.</summary>
    internal const int GTK_FILE_CHOOSER_ACTION_OPEN = 0;

    /// <summary>Value of <c>GTK_FILE_CHOOSER_ACTION_SAVE</c>.</summary>
    internal const int GTK_FILE_CHOOSER_ACTION_SAVE = 1;

    /// <summary>Value of <c>GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER</c>.</summary>
    internal const int GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER = 2;

    /// <summary>A singly-linked <c>GSList</c> cell, as returned by <see cref="gtk_file_chooser_get_filenames"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GSList
    {
        /// <summary>The payload pointer (here: a heap UTF-8 string to free with <see cref="g_free"/>).</summary>
        public nint Data;

        /// <summary>The next cell, or 0 at the end.</summary>
        public nint Next;
    }

    // --- Modal windows ---

    /// <summary>Marks a window as modal: while shown it blocks input to its transient parent (<c>gboolean</c>).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_modal(nint window, int modal);

    /// <summary>Makes <paramref name="window"/> a transient child of <paramref name="parent"/> (stacking, centering, modality scope).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_window_set_transient_for(nint window, nint parent);

    // --- Dialogs (shared) ---

    /// <summary>Runs a dialog's own modal loop until it emits a response; returns the response id.</summary>
    [LibraryImport(Gtk)]
    internal static partial int gtk_dialog_run(nint dialog);

    /// <summary>Adds a button (mnemonic text) that emits <paramref name="responseId"/>; returns the button widget.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_dialog_add_button(nint dialog, string buttonText, int responseId);

    /// <summary>Makes the button bound to <paramref name="responseId"/> the default (activated by Enter).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_dialog_set_default_response(nint dialog, int responseId);

    /// <summary>
    /// Creates a <c>GtkMessageDialog</c>. The final parameter is the (printf-style, variadic) message
    /// format — always passed as 0 here; the text is set afterwards through the <c>"text"</c>
    /// property, which avoids routing user text through a format string.
    /// </summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_message_dialog_new(nint parent, int flags, int type, int buttons, nint format);

    /// <summary>
    /// Sets a single string property (variadic <c>g_object_set</c> bound with a fixed signature; the
    /// trailing <c>NULL</c> terminator is passed explicitly — see <c>g_object_get</c>).
    /// </summary>
    [LibraryImport(GObject, EntryPoint = "g_object_set", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void g_object_set_string(nint @object, string firstPropertyName, string value, nint terminator);

    // --- File chooser ---

    /// <summary>
    /// Creates a <c>GtkFileChooserDialog</c>. The first-button parameter (variadic in C) is always
    /// passed as 0; buttons are added via <see cref="gtk_dialog_add_button"/>.
    /// </summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_file_chooser_dialog_new(string title, nint parent, int action, nint firstButtonText);

    /// <summary>Lets the user pick several files at once (<c>gboolean</c>).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_file_chooser_set_select_multiple(nint chooser, int selectMultiple);

    /// <summary>Sets the directory the chooser starts in.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_chooser_set_current_folder(nint chooser, string filename);

    /// <summary>Pre-fills the file-name entry (save dialogs; plain name, not a path).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_chooser_set_current_name(nint chooser, string name);

    /// <summary>Asks before overwriting an existing file (<c>gboolean</c>; save dialogs).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_file_chooser_set_do_overwrite_confirmation(nint chooser, int doOverwriteConfirmation);

    /// <summary>Returns the selected path as a heap UTF-8 string to free with <see cref="g_free"/>, or 0.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_file_chooser_get_filename(nint chooser);

    /// <summary>Returns every selected path as a <see cref="GSList"/> of heap UTF-8 strings — free each with <see cref="g_free"/> and the list with <see cref="g_slist_free"/>.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_file_chooser_get_filenames(nint chooser);

    /// <summary>Creates an empty file-type filter.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_file_filter_new();

    /// <summary>Sets the filter's display name.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_filter_set_name(nint filter, string name);

    /// <summary>Adds one glob pattern the filter accepts.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_filter_add_pattern(nint filter, string pattern);

    /// <summary>Adds a filter to the chooser's type drop-down (the chooser takes ownership).</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_file_chooser_add_filter(nint chooser, nint filter);

    /// <summary>Makes <paramref name="filter"/> the initially active drop-down entry.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_file_chooser_set_filter(nint chooser, nint filter);

    /// <summary>Frees a <c>GSList</c>'s cells (not the payloads).</summary>
    [LibraryImport(GLib)]
    internal static partial void g_slist_free(nint list);

    // --- Color chooser ---

    /// <summary>Creates a <c>GtkColorChooserDialog</c>; its Select button responds <see cref="GTK_RESPONSE_OK"/>.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_color_chooser_dialog_new(string title, nint parent);

    /// <summary>Preselects a color.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_color_chooser_set_rgba(nint chooser, in GdkRGBA color);

    /// <summary>Reads the currently selected color.</summary>
    [LibraryImport(Gtk)]
    internal static partial void gtk_color_chooser_get_rgba(nint chooser, out GdkRGBA color);

    // --- Font chooser ---

    /// <summary>Creates a <c>GtkFontChooserDialog</c>; its Select button responds <see cref="GTK_RESPONSE_OK"/>.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_font_chooser_dialog_new(string title, nint parent);

    /// <summary>Preselects the font described by a Pango description string (for example <c>"Sans Bold 12"</c>).</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_font_chooser_set_font(nint fontchooser, string fontname);

    /// <summary>Returns the selected font as a Pango description — a heap UTF-8 string to free with <see cref="g_free"/>, or 0.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_font_chooser_get_font(nint fontchooser);
}
