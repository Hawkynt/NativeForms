using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// Drags files out of a window with GTK's own drag-and-drop (PRD §8): a <c>text/uri-list</c> drag
/// started from the canvas widget, answering the drop target's <c>drag-data-get</c> with
/// <c>file://</c> URIs, which is what Nautilus, the desktop and every other GTK or Qt file target read.
/// </summary>
/// <remarks>
/// <para>
/// GTK wants the drag begun while a button is held, from the event that is moving the pointer: its
/// timestamp and device take the pointer grab (on Wayland the serial is mandatory). The core asks from
/// within the canvas's motion handler, so <c>gtk_get_current_event</c> is exactly that event, and the
/// button comes from its state.
/// </para>
/// <para>
/// The drag is asynchronous: <c>gtk_drag_begin_with_coordinates</c> returns at once, and
/// <c>drag-end</c> — which GTK emits for every drag it began, after <c>drag-failed</c> when it failed —
/// reports the target's chosen action. One drag runs at a time, so its state is static; the signal
/// handlers are <see cref="UnmanagedCallersOnlyAttribute"/> statics connected once per widget.
/// </para>
/// </remarks>
internal static unsafe class GtkFileDragSource {
  /// <summary>The NULL-terminated URI array of the drag in flight, or zero when idle.</summary>
  private static nint* _uris;

  /// <summary>Whether GTK reported the drag in flight as failed.</summary>
  private static bool _failed;

  /// <summary>Who hears how the drag in flight ended.</summary>
  private static Action<DragDropEffects>? _completed;

  /// <summary>The effects the drag in flight allows, for filtering the target's answer.</summary>
  private static DragDropEffects _allowed;

  /// <summary>Connects the drag-source signals to <paramref name="widget"/>; once per widget.</summary>
  internal static void Connect(nint widget) {
    NativeMethods.g_signal_connect_data(
        widget, "drag-data-get", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, uint, uint, nint, void>)&OnDragDataGet, 0, 0, 0);
    NativeMethods.g_signal_connect_data(
        widget, "drag-failed", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)&OnDragFailed, 0, 0, 0);
    NativeMethods.g_signal_connect_data(
        widget, "drag-end", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnDragEnd, 0, 0, 0);
  }

  /// <summary>Starts the drag of <paramref name="paths"/> from <paramref name="widget"/>, whose signals
  /// <see cref="Connect"/> has wired.</summary>
  /// <returns><see langword="false"/> when no drag could be started, so the drag stays in process.</returns>
  internal static bool TryDrag(nint widget, string[] paths, DragDropEffects allowedEffects, Action<DragDropEffects> completed) {
    if (widget == 0 || paths.Length == 0 || _uris != null)
      return false;

    var current = NativeMethods.gtk_get_current_event();
    if (current == 0)
      return false;

    nint targets = 0;
    try {
      var button = HeldButton(current);
      if (button == 0)
        return false;

      var uris = CreateUris(paths);
      if (uris == null)
        return false;

      _uris = uris;
      _failed = false;
      _completed = completed;
      _allowed = allowedEffects;

      targets = NativeMethods.gtk_target_list_new(0, 0);
      NativeMethods.gtk_target_list_add_uri_targets(targets, 0);
      var context = NativeMethods.gtk_drag_begin_with_coordinates(
          widget, targets, ToActions(allowedEffects), button, current, -1, -1);
      if (context != 0)
        return true;

      Clear();
      return false;
    } finally {
      if (targets != 0)
        NativeMethods.gtk_target_list_unref(targets);

      NativeMethods.gdk_event_free(current);
    }
  }

  /// <summary>The GDK action mask for toolkit effects.</summary>
  internal static int ToActions(DragDropEffects effects) {
    var actions = 0;
    if ((effects & DragDropEffects.Copy) != 0)
      actions |= NativeMethods.GDK_ACTION_COPY;
    if ((effects & DragDropEffects.Move) != 0)
      actions |= NativeMethods.GDK_ACTION_MOVE;
    if ((effects & DragDropEffects.Link) != 0)
      actions |= NativeMethods.GDK_ACTION_LINK;
    return actions;
  }

  /// <summary>The toolkit effects in a GDK action mask; <c>DEFAULT</c>, <c>PRIVATE</c> and <c>ASK</c> carry none.</summary>
  internal static DragDropEffects ToEffects(int actions) {
    var effects = DragDropEffects.None;
    if ((actions & NativeMethods.GDK_ACTION_COPY) != 0)
      effects |= DragDropEffects.Copy;
    if ((actions & NativeMethods.GDK_ACTION_MOVE) != 0)
      effects |= DragDropEffects.Move;
    if ((actions & NativeMethods.GDK_ACTION_LINK) != 0)
      effects |= DragDropEffects.Link;
    return effects;
  }

  /// <summary>The GDK number of a held button in <paramref name="gdkEvent"/>'s state, or 0.</summary>
  private static int HeldButton(nint gdkEvent) {
    if (NativeMethods.gdk_event_get_state(gdkEvent, out var state) == 0)
      return 0;

    return (state & NativeMethods.GDK_BUTTON1_MASK) != 0 ? 1
        : (state & NativeMethods.GDK_BUTTON2_MASK) != 0 ? 2
        : (state & NativeMethods.GDK_BUTTON3_MASK) != 0 ? 3
        : 0;
  }

  /// <summary>A NULL-terminated array of <c>file://</c> URIs for <paramref name="paths"/>, or null.</summary>
  private static nint* CreateUris(string[] paths) {
    var uris = (nint*)NativeMemory.AllocZeroed((nuint)(paths.Length + 1), (nuint)sizeof(nint));
    for (var i = 0; i < paths.Length; ++i) {
      uris[i] = NativeMethods.g_filename_to_uri(paths[i], 0, 0);
      if (uris[i] != 0)
        continue;

      FreeUris(uris);
      return null;
    }

    return uris;
  }

  private static void FreeUris(nint* uris) {
    for (var current = uris; *current != 0; ++current)
      NativeMethods.g_free(*current);

    NativeMemory.Free(uris);
  }

  /// <summary>Returns to idle, freeing the URIs; answers who was to hear the outcome.</summary>
  private static Action<DragDropEffects>? Clear() {
    if (_uris != null)
      FreeUris(_uris);

    _uris = null;
    var completed = _completed;
    _completed = null;
    return completed;
  }

  /// <summary>Native "drag-data-get": <c>void (GtkWidget*, GdkDragContext*, GtkSelectionData*, guint info, guint time, gpointer)</c>.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static void OnDragDataGet(nint widget, nint context, nint selectionData, uint info, uint time, nint userData) {
    if (_uris != null)
      NativeMethods.gtk_selection_data_set_uris(selectionData, (nint)_uris);
  }

  /// <summary>Native "drag-failed": <c>gboolean (GtkWidget*, GdkDragContext*, GtkDragResult, gpointer)</c>.
  /// Answers FALSE so GTK still plays its snap-back animation.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static int OnDragFailed(nint widget, nint context, int result, nint userData) {
    _failed = true;
    return 0;
  }

  /// <summary>Native "drag-end": <c>void (GtkWidget*, GdkDragContext*, gpointer)</c>. Reports the
  /// outcome — none after a failure — and returns to idle.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static void OnDragEnd(nint widget, nint context, nint userData) {
    if (_uris == null)
      return;

    var effect = _failed
        ? DragDropEffects.None
        : ToEffects(NativeMethods.gdk_drag_context_get_selected_action(context)) & _allowed;
    var completed = Clear();
    try {
      completed?.Invoke(effect);
    } catch {
      // An exception must not unwind into GTK's C frames.
    }
  }
}
