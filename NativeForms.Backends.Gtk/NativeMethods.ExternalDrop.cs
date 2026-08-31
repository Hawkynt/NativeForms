using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

internal static partial class NativeMethods
{
    internal const int GTK_DEST_DEFAULT_MOTION = 1;
    internal const int GTK_DEST_DEFAULT_HIGHLIGHT = 2;
    internal const int GDK_ACTION_COPY = 2;

    [LibraryImport(Gtk)]
    internal static partial void gtk_drag_dest_set(nint widget, int flags, nint targets, int nTargets, int actions);

    [LibraryImport(Gtk)]
    internal static partial void gtk_drag_dest_add_uri_targets(nint widget);

    [LibraryImport(Gtk)]
    internal static partial nint gtk_drag_dest_find_target(nint widget, nint context, nint targetList);

    [LibraryImport(Gtk)]
    internal static partial void gtk_drag_get_data(nint widget, nint context, nint target, uint time);

    [LibraryImport(Gtk)]
    internal static partial nint gtk_selection_data_get_uris(nint selectionData);

    [LibraryImport(Gtk)]
    internal static partial void gtk_drag_finish(nint context, int success, int delete, uint time);

    [LibraryImport(GLib)]
    internal static partial void g_strfreev(nint stringArray);
}
