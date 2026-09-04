using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>GtkScale-specific entry points used by the promoted track bar peer.</summary>
internal static partial class NativeMethods {
  /// <summary>Removes every mark previously added to a <c>GtkScale</c>.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_scale_clear_marks(nint scale);

  /// <summary>Adds an unlabelled mark at the requested value and <c>GtkPositionType</c>.</summary>
  [LibraryImport(Gtk)]
  internal static partial void gtk_scale_add_mark(nint scale, double value, int position, nint markup);
}
