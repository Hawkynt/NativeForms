using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK 3 drag-source surface behind dragging files out of a window (PRD §8): a target list of
/// <c>text/uri-list</c> (and <c>XdndDirectSave0</c> for a virtual file), <c>gtk_drag_begin_with_coordinates</c>
/// with the event being dispatched, the selection-data calls that answer the drop target, and the
/// window property the direct-save protocol exchanges the destination through.
/// </summary>
internal static unsafe partial class NativeMethods {
  /// <summary><c>GDK_PROP_MODE_REPLACE</c>.</summary>
  internal const int GDK_PROP_MODE_REPLACE = 0;

  /// <summary><c>GDK_ACTION_MOVE</c>.</summary>
  internal const int GDK_ACTION_MOVE = 4;

  /// <summary><c>GDK_ACTION_LINK</c>.</summary>
  internal const int GDK_ACTION_LINK = 8;

  /// <summary><c>GDK_BUTTON1_MASK</c>: the primary button is held.</summary>
  internal const uint GDK_BUTTON1_MASK = 1 << 8;

  /// <summary><c>GDK_BUTTON2_MASK</c>: the middle button is held.</summary>
  internal const uint GDK_BUTTON2_MASK = 1 << 9;

  /// <summary><c>GDK_BUTTON3_MASK</c>: the secondary button is held.</summary>
  internal const uint GDK_BUTTON3_MASK = 1 << 10;

  /// <summary>Creates an empty target list; release it with <see cref="gtk_target_list_unref"/>.</summary>
  [LibraryImport(Gtk)]
  internal static partial nint gtk_target_list_new(nint targets, uint count);

  /// <summary>Adds the URI-list targets (<c>text/uri-list</c>) to a target list.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_target_list_add_uri_targets(nint list, uint info);

  /// <summary>Drops a reference to a target list.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_target_list_unref(nint list);

  /// <summary>
  /// Starts a drag from <paramref name="widget"/>; answers the <c>GdkDragContext</c>, or zero when the
  /// drag could not start. Coordinates of -1 take the position from the event.
  /// </summary>
  [LibraryImport(Gtk)]
  internal static partial nint gtk_drag_begin_with_coordinates(nint widget, nint targets, int actions, int button, nint triggeringEvent, int x, int y);

  /// <summary>A copy of the event being dispatched, or zero; free it with <see cref="gdk_event_free"/>.</summary>
  [LibraryImport(Gtk)]
  internal static partial nint gtk_get_current_event();

  /// <summary>Frees an event copy.</summary>
  [LibraryImport(Gdk)]
  internal static partial void gdk_event_free(nint gdkEvent);

  /// <summary>Reads an event's modifier and button state; answers whether the event has one.</summary>
  [LibraryImport(Gdk)]
  internal static partial int gdk_event_get_state(nint gdkEvent, out uint state);

  /// <summary>The action the drop target chose for a drag.</summary>
  [LibraryImport(Gdk)]
  internal static partial int gdk_drag_context_get_selected_action(nint context);

  /// <summary>Answers a drop target's request with a NULL-terminated array of URIs.</summary>
  [LibraryImport(Gtk)]
  internal static partial int gtk_selection_data_set_uris(nint selectionData, nint uris);

  /// <summary>
  /// Converts an absolute file name into a <c>file://</c> URI with the escaping RFC 8089 requires;
  /// the result is freed with <see cref="g_free"/>, zero on failure (the error is not asked for).
  /// </summary>
  [LibraryImport(GLib, StringMarshalling = StringMarshalling.Utf8)]
  internal static partial nint g_filename_to_uri(string filename, nint hostname, nint error);

  /// <summary>
  /// Converts a <c>file://</c> URI into a local file name, freed with <see cref="g_free"/>, and hands
  /// out its host part (freed the same way, zero when there is none); zero when it is not a file URI.
  /// </summary>
  [LibraryImport(GLib)]
  internal static partial nint g_filename_from_uri(byte* uri, out nint hostname, nint error);

  /// <summary>The machine's host name, owned by GLib.</summary>
  [LibraryImport(GLib)]
  internal static partial nint g_get_host_name();

  /// <summary>The name of a GObject type.</summary>
  [LibraryImport(GObject)]
  internal static partial nint g_type_name(nuint type);

  /// <summary>Adds one target to a target list.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_target_list_add(nint list, nint target, uint flags, uint info);

  /// <summary>The window a drag was started from — the one the direct-save property lives on.</summary>
  [LibraryImport(Gdk)]
  internal static partial nint gdk_drag_context_get_source_window(nint context);

  /// <summary>The display a window belongs to.</summary>
  [LibraryImport(Gdk)]
  internal static partial nint gdk_window_get_display(nint window);

  /// <summary>Sets a window property (X11 only).</summary>
  [LibraryImport(Gdk)]
  internal static partial void gdk_property_change(nint window, nint property, nint type, int format, int mode, byte* data, int elements);

  /// <summary>
  /// Reads a window property; <paramref name="type"/> zero accepts any type. The data is freed with
  /// <see cref="g_free"/>.
  /// </summary>
  [LibraryImport(Gdk)]
  internal static partial int gdk_property_get(
      nint window, nint property, nint type, nuint offset, nuint length, int delete,
      out nint actualType, out int actualFormat, out int actualLength, out nint data);

  /// <summary>Deletes a window property.</summary>
  [LibraryImport(Gdk)]
  internal static partial void gdk_property_delete(nint window, nint property);

  /// <summary>The target a drop target asked a selection for.</summary>
  [LibraryImport(Gtk)]
  internal static partial nint gtk_selection_data_get_target(nint selectionData);

  /// <summary>Answers a drop target's request with raw data of <paramref name="type"/>.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_selection_data_set(nint selectionData, nint type, int format, byte* data, int length);
}
