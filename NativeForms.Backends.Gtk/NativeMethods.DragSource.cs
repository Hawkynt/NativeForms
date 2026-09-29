using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK 3 drag-source surface behind dragging files out of a window (PRD §8): a target list of
/// <c>text/uri-list</c>, <c>gtk_drag_begin_with_coordinates</c> with the event being dispatched, and the
/// selection-data call that answers the drop target's request for the URIs.
/// </summary>
internal static partial class NativeMethods {
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
}
