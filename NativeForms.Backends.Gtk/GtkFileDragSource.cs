using System.Runtime.InteropServices;
using System.Text;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// Drags files out of a window with GTK's own drag-and-drop (PRD §8): a <c>text/uri-list</c> drag
/// started from the canvas widget, answering the drop target's <c>drag-data-get</c> with
/// <c>file://</c> URIs, which is what Nautilus, the desktop and every other GTK or Qt file target read.
/// Virtual files are offered through the X direct-save protocol as well, so a file manager names the
/// destination and the content is written straight into it.
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
/// <para>
/// <b>Virtual files.</b> A drag of one virtual file (not a folder) on X11 also offers
/// <c>XdndDirectSave0</c> (the XDS protocol, freedesktop.org): the proposed name goes into the
/// <c>XdndDirectSave0</c> property of the drag's source window; a target that supports it — Nautilus,
/// Thunar, Caja, Nemo — puts the destination's <c>file://</c> URI there and asks for the
/// <c>XdndDirectSave0</c> target, and the file is then written into that folder under a hidden
/// temporary name and renamed to its final name only once complete (<see cref="VirtualFileWriter"/>),
/// answering <c>S</c>; a destination on another host is answered <c>F</c>, a failed write <c>E</c>. An
/// existing file of that name is never replaced: the file gets the next free <c>name (2)</c> instead.
/// XDS carries a single file, so several files, a folder, and any target without XDS get
/// <c>text/uri-list</c>: the files are written into a private temporary folder the first time a target
/// actually asks for the URIs, and those paths are handed over.
/// </para>
/// <para>
/// The content is written while GTK waits for the answer to the target's request, on the UI thread;
/// a target gives up waiting after its selection timeout (GTK's is 30 seconds without progress), so a
/// very slow stream still completes on disk but the target may report the save as failed.
/// </para>
/// </remarks>
internal static unsafe class GtkFileDragSource {
  /// <summary>The target-list info of <c>text/uri-list</c>.</summary>
  private const uint _UriListInfo = 0;

  /// <summary>The target-list info of <c>XdndDirectSave0</c>.</summary>
  private const uint _DirectSaveInfo = 1;

  /// <summary>The XDS atom name: both the window property and the target.</summary>
  internal const string DirectSave = "XdndDirectSave0";

  /// <summary>Whether a drag is in flight.</summary>
  private static bool _active;

  /// <summary>The NULL-terminated URI array of the drag in flight, or null until a target needs it.</summary>
  private static nint* _uris;

  /// <summary>The virtual files of the drag in flight, or null for a drag of existing paths.</summary>
  private static VirtualFile[]? _files;

  /// <summary>The window holding the XDS property of the drag in flight, or zero.</summary>
  private static nint _directSaveWindow;

  /// <summary>Whether GTK reported the drag in flight as failed.</summary>
  private static bool _failed;

  /// <summary>Whether the last direct save of the drag in flight was answered with anything but <c>S</c>.</summary>
  private static bool _directSaveFailed;

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
    if (paths.Length == 0)
      return false;

    var uris = CreateUris(paths);
    if (uris == null)
      return false;

    if (Begin(widget, uris, null, allowedEffects, completed))
      return true;

    FreeUris(uris);
    return false;
  }

  /// <summary>Starts the drag of <paramref name="files"/> from <paramref name="widget"/>, whose signals
  /// <see cref="Connect"/> has wired; nothing is written until a target asks.</summary>
  /// <returns><see langword="false"/> when no drag could be started, so the drag stays in process.</returns>
  internal static bool TryDragVirtual(nint widget, VirtualFile[] files, DragDropEffects allowedEffects, Action<DragDropEffects> completed)
      => files.Length > 0 && Begin(widget, null, files, allowedEffects, completed);

  /// <summary>
  /// The name to propose through XDS: the single top-level file of <paramref name="files"/>, or
  /// <see langword="null"/> when they are several entries or a folder, which XDS cannot carry.
  /// </summary>
  internal static string? DirectSaveName(VirtualFile[] files) {
    var names = VirtualFileWriter.TopLevelNames(files);
    return names.Count == 1 && !VirtualFileWriter.IsFolder(files, names[0]) ? names[0] : null;
  }

  private static bool Begin(nint widget, nint* uris, VirtualFile[]? files, DragDropEffects allowedEffects, Action<DragDropEffects> completed) {
    if (widget == 0 || _active)
      return false;

    var current = NativeMethods.gtk_get_current_event();
    if (current == 0)
      return false;

    nint targets = 0;
    try {
      var button = HeldButton(current);
      if (button == 0)
        return false;

      var directSave = files is null ? null : DirectSaveName(files);
      targets = NativeMethods.gtk_target_list_new(0, 0);
      if (directSave is not null)
        NativeMethods.gtk_target_list_add(targets, NativeMethods.gdk_atom_intern(DirectSave, 0), 0, _DirectSaveInfo);

      NativeMethods.gtk_target_list_add_uri_targets(targets, _UriListInfo);

      _active = true;
      _uris = uris;
      _files = files;
      _failed = false;
      _directSaveFailed = false;
      _completed = completed;
      _allowed = allowedEffects;
      var context = NativeMethods.gtk_drag_begin_with_coordinates(
          widget, targets, ToActions(allowedEffects), button, current, -1, -1);
      if (context == 0) {
        _uris = null; // the caller still owns them
        Clear();
        return false;
      }

      if (directSave is not null)
        OfferDirectSave(context, directSave);

      return true;
    } finally {
      if (targets != 0)
        NativeMethods.gtk_target_list_unref(targets);

      NativeMethods.gdk_event_free(current);
    }
  }

  /// <summary>
  /// XDS step 0: the proposed file name goes into the source window's <c>XdndDirectSave0</c> property,
  /// typed <c>text/plain</c>. Only on X11, the one GDK backend that has window properties.
  /// </summary>
  private static void OfferDirectSave(nint context, string name) {
    var window = NativeMethods.gdk_drag_context_get_source_window(context);
    if (window == 0 || !IsX11(window))
      return;

    var bytes = Encoding.UTF8.GetBytes(name);
    fixed (byte* data = bytes)
      NativeMethods.gdk_property_change(
          window,
          NativeMethods.gdk_atom_intern(DirectSave, 0),
          NativeMethods.gdk_atom_intern("text/plain", 0),
          8,
          NativeMethods.GDK_PROP_MODE_REPLACE,
          data,
          bytes.Length);
    _directSaveWindow = window;
  }

  /// <summary>Whether <paramref name="window"/> lives on an X11 display (its display's type is <c>GdkX11Display</c>).</summary>
  private static bool IsX11(nint window) {
    var display = NativeMethods.gdk_window_get_display(window);
    if (display == 0)
      return false;

    // G_OBJECT_TYPE_NAME: the instance's class pointer comes first, and the class starts with its GType.
    var type = *(nuint*)*(nint*)display;
    return Marshal.PtrToStringUTF8(NativeMethods.g_type_name(type)) == "GdkX11Display";
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

  /// <summary>Returns to idle, freeing the URIs and the XDS property; answers who was to hear the outcome.</summary>
  private static Action<DragDropEffects>? Clear() {
    if (_uris != null)
      FreeUris(_uris);

    // XDS step 4: the source deletes the property whatever happened.
    if (_directSaveWindow != 0)
      NativeMethods.gdk_property_delete(_directSaveWindow, NativeMethods.gdk_atom_intern(DirectSave, 0));

    _active = false;
    _uris = null;
    _files = null;
    _directSaveWindow = 0;
    var completed = _completed;
    _completed = null;
    return completed;
  }

  /// <summary>Native "drag-data-get": <c>void (GtkWidget*, GdkDragContext*, GtkSelectionData*, guint info, guint time, gpointer)</c>.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static void OnDragDataGet(nint widget, nint context, nint selectionData, uint info, uint time, nint userData) {
    if (!_active)
      return;

    try {
      if (info == _DirectSaveInfo) {
        AnswerDirectSave(context, selectionData);
        return;
      }

      // Virtual files become paths the first time a target actually wants them.
      if (_uris == null && _files is { } files)
        _uris = CreateUris(VirtualFileWriter.Materialize(files));

      if (_uris != null && NativeMethods.gtk_selection_data_set_uris(selectionData, (nint)_uris) != 0)
        _directSaveFailed = false; // a target that fell back from direct save got the files after all
    } catch {
      // An exception must not unwind into GTK's C frames; the target receives no data.
    }
  }

  /// <summary>
  /// XDS step 2: reads the destination the target put into the property, writes the file there, and
  /// answers <c>S</c> (saved), <c>F</c> (the destination is on another host) or <c>E</c> (the save failed).
  /// </summary>
  private static void AnswerDirectSave(nint context, nint selectionData) {
    var answer = (byte)'E';
    try {
      if (_files is { } files && DirectSaveName(files) is { } top && ReadDirectSaveUri(context) is { } uri) {
        var (path, local) = ToLocalPath(uri);
        if (path is null)
          answer = (byte)'E';
        else if (!local)
          answer = (byte)'F';
        else {
          VirtualFileWriter.WriteEntry(path, files, top);
          answer = (byte)'S';
        }
      }
    } catch {
      answer = (byte)'E';
    }

    _directSaveFailed = answer != (byte)'S';
    NativeMethods.gtk_selection_data_set(selectionData, NativeMethods.gdk_atom_intern("STRING", 0), 8, &answer, 1);
  }

  /// <summary>The URI the target stored in the source window's XDS property, or null.</summary>
  private static byte[]? ReadDirectSaveUri(nint context) {
    var window = _directSaveWindow != 0 ? _directSaveWindow : NativeMethods.gdk_drag_context_get_source_window(context);
    if (window == 0
        || NativeMethods.gdk_property_get(window, NativeMethods.gdk_atom_intern(DirectSave, 0), 0, 0, 65536, 0,
            out _, out var format, out var length, out var data) == 0
        || data == 0)
      return null;

    try {
      return format == 8 && length > 0 ? new ReadOnlySpan<byte>((void*)data, length).ToArray() : null;
    } finally {
      NativeMethods.g_free(data);
    }
  }

  /// <summary>
  /// The local path of a <c>file://</c> URI and whether its host is this machine (no host,
  /// <c>localhost</c>, or the host name); a null path when it is not a file URI.
  /// </summary>
  private static (string? Path, bool Local) ToLocalPath(byte[] uri) {
    var terminated = new byte[uri.Length + 1];
    uri.CopyTo(terminated, 0);
    nint name;
    nint host;
    fixed (byte* text = terminated)
      name = NativeMethods.g_filename_from_uri(text, out host, 0);

    try {
      if (name == 0)
        return (null, false);

      var hostName = host == 0 ? null : Marshal.PtrToStringUTF8(host);
      var local = string.IsNullOrEmpty(hostName)
          || hostName == "localhost"
          || hostName == Marshal.PtrToStringUTF8(NativeMethods.g_get_host_name());
      return (Marshal.PtrToStringUTF8(name), local);
    } finally {
      NativeMethods.g_free(name);
      NativeMethods.g_free(host);
    }
  }

  /// <summary>Native "drag-failed": <c>gboolean (GtkWidget*, GdkDragContext*, GtkDragResult, gpointer)</c>.
  /// Answers FALSE so GTK still plays its snap-back animation.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static int OnDragFailed(nint widget, nint context, int result, nint userData) {
    _failed = true;
    return 0;
  }

  /// <summary>Native "drag-end": <c>void (GtkWidget*, GdkDragContext*, gpointer)</c>. Reports the
  /// outcome — none after a failure, or after a direct save that did not save — and returns to idle.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
  private static void OnDragEnd(nint widget, nint context, nint userData) {
    if (!_active)
      return;

    var effect = _failed || _directSaveFailed
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
