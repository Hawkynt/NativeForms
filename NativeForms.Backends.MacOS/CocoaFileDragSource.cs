using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// Drags files out of a window with AppKit's dragging session (PRD §8): one <c>NSDraggingItem</c> per
/// path, each wrapping an <c>NSURL fileURLWithPath:</c> — a URL is its own pasteboard writer — and
/// showing the file's Finder icon, started with <c>beginDraggingSessionWithItems:event:source:</c>.
/// </summary>
/// <remarks>
/// <para>
/// The session needs the mouse event that is moving the pointer; the core asks from within the
/// canvas's <c>mouseDragged:</c>, so <c>[NSApp currentEvent]</c> is that event, and anything else
/// declines. The session is asynchronous: it begins once the current event has been handled and
/// reports its end to the source's <c>draggingSession:endedAtPoint:operation:</c>.
/// </para>
/// <para>
/// The source is an instance of a class built at run time conforming to <c>NSDraggingSource</c>, the
/// same shape as <see cref="CocoaAction"/>: its two methods are
/// <see cref="UnmanagedCallersOnlyAttribute"/> statics, and each source's allowed operations and
/// completion come back out of a static map keyed by the instance rather than out of a closure.
/// </para>
/// </remarks>
internal static unsafe class CocoaFileDragSource {
  /// <summary><c>NSDragOperationCopy</c>.</summary>
  internal const nuint NSDragOperationCopy = 1;

  /// <summary><c>NSDragOperationLink</c>.</summary>
  internal const nuint NSDragOperationLink = 2;

  /// <summary><c>NSDragOperationMove</c>.</summary>
  internal const nuint NSDragOperationMove = 16;

  /// <summary>The runtime source class, built on first use.</summary>
  private static nint _class;

  /// <summary>The operations and the completion of every session in flight, by source instance.</summary>
  private static readonly ConcurrentDictionary<nint, (nuint Mask, DragDropEffects Allowed, Action<DragDropEffects> Completed)> _sessions = new();

  /// <summary>Starts the dragging session of <paramref name="paths"/> from <paramref name="view"/>.</summary>
  /// <returns><see langword="false"/> when no session could be started, so the drag stays in process.</returns>
  internal static bool TryDrag(nint view, string[] paths, DragDropEffects allowedEffects, Action<DragDropEffects> completed) {
    if (view == 0 || paths.Length == 0 || !CocoaRuntime.Available)
      return false;

    EnsureClass();
    var application = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
    var trigger = application == 0 ? 0 : CocoaRuntime.SendPointer(application, CocoaRuntime.sel_registerName("currentEvent"));
    if (_class == 0 || trigger == 0 || !IsDragEvent(CocoaRuntime.SendInteger(trigger, CocoaRuntime.sel_registerName("type"))))
      return false;

    var items = CreateItems(view, trigger, paths);
    if (items == 0)
      return false;

    var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
    var source = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
    if (source == 0)
      return false;

    _sessions[source] = (ToOperations(allowedEffects), allowedEffects, completed);
    var session = CocoaRuntime.SendPointer(
        view,
        CocoaRuntime.sel_registerName("beginDraggingSessionWithItems:event:source:"),
        items,
        trigger,
        source);
    if (session != 0)
      return true;

    _sessions.TryRemove(source, out _);
    CocoaRuntime.SendVoid(source, CocoaRuntime.sel_registerName("release"));
    return false;
  }

  /// <summary>The <c>NSDragOperation</c> mask for toolkit effects.</summary>
  internal static nuint ToOperations(DragDropEffects effects) {
    nuint operations = 0;
    if ((effects & DragDropEffects.Copy) != 0)
      operations |= NSDragOperationCopy;
    if ((effects & DragDropEffects.Link) != 0)
      operations |= NSDragOperationLink;
    if ((effects & DragDropEffects.Move) != 0)
      operations |= NSDragOperationMove;
    return operations;
  }

  /// <summary>The toolkit effects in an <c>NSDragOperation</c>; generic, private and delete carry none.</summary>
  internal static DragDropEffects ToEffects(nuint operations) {
    var effects = DragDropEffects.None;
    if ((operations & NSDragOperationCopy) != 0)
      effects |= DragDropEffects.Copy;
    if ((operations & NSDragOperationLink) != 0)
      effects |= DragDropEffects.Link;
    if ((operations & NSDragOperationMove) != 0)
      effects |= DragDropEffects.Move;
    return effects;
  }

  /// <summary>Whether an <c>NSEventType</c> is a press or drag of any mouse button.</summary>
  private static bool IsDragEvent(nint type)
      => type is 1 or 3 or 6 or 7 or 25 or 27; // Left/RightMouseDown, Left/RightMouseDragged, OtherMouseDown/Dragged

  /// <summary>
  /// An autoreleased array of dragging items for <paramref name="paths"/>, each a 32-point icon
  /// fanned out from the pointer, or zero.
  /// </summary>
  private static nint CreateItems(nint view, nint trigger, string[] paths) {
    var arrays = CocoaRuntime.objc_getClass("NSMutableArray");
    var urls = CocoaRuntime.objc_getClass("NSURL");
    var workspace = CocoaRuntime.SendToClass("NSWorkspace", "sharedWorkspace");
    if (arrays == 0 || urls == 0)
      return 0;

    var items = CocoaRuntime.SendIndex(arrays, CocoaRuntime.sel_registerName("arrayWithCapacity:"), paths.Length);
    if (items == 0)
      return 0;

    var inWindow = CocoaRuntime.SendPoint(trigger, CocoaRuntime.sel_registerName("locationInWindow"));
    var at = CocoaRuntime.SendConvert(view, CocoaRuntime.sel_registerName("convertPoint:fromView:"), inWindow, 0);
    for (var i = 0; i < paths.Length; ++i) {
      var path = CocoaRuntime.NSString(paths[i]);
      if (path == 0)
        return 0;

      try {
        var url = CocoaRuntime.SendPointer(urls, CocoaRuntime.sel_registerName("fileURLWithPath:"), path);
        var allocated = url == 0 ? 0 : CocoaRuntime.Allocate("NSDraggingItem");
        var item = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithPasteboardWriter:"), url);
        if (item == 0)
          return 0;

        var icon = workspace == 0 ? 0 : CocoaRuntime.SendPointer(workspace, CocoaRuntime.sel_registerName("iconForFile:"), path);
        CocoaRuntime.SendRectObject(
            item,
            CocoaRuntime.sel_registerName("setDraggingFrame:contents:"),
            new(at.X - 16 + i * 4, at.Y - 16 + i * 4, 32, 32),
            icon);
        CocoaRuntime.SendVoid(items, CocoaRuntime.sel_registerName("addObject:"), item);
        CocoaRuntime.SendVoid(item, CocoaRuntime.sel_registerName("release")); // the array owns it now
      } finally {
        CocoaRuntime.SendVoid(path, CocoaRuntime.sel_registerName("release"));
      }
    }

    return items;
  }

  private static void EnsureClass() {
    if (_class != 0)
      return;

    var superclass = CocoaRuntime.objc_getClass("NSObject");
    var created = superclass == 0 ? 0 : CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsDraggingSource", 0);
    if (created == 0)
      return;

    if (CocoaRuntime.objc_getProtocol("NSDraggingSource") is var protocol and not 0)
      CocoaRuntime.class_addProtocol(created, protocol);

    // "Q@:@q": answers an NSDragOperation for self, _cmd, the session and an NSDraggingContext.
    CocoaRuntime.class_addMethod(
        created,
        CocoaRuntime.sel_registerName("draggingSession:sourceOperationMaskForDraggingContext:"),
        (nint)(delegate* unmanaged<nint, nint, nint, nint, nuint>)&SourceOperationMask,
        "Q@:@q");

    // "v@:@{CGPoint=dd}Q": the session, where it ended on screen, and the operation performed.
    CocoaRuntime.class_addMethod(
        created,
        CocoaRuntime.sel_registerName("draggingSession:endedAtPoint:operation:"),
        (nint)(delegate* unmanaged<nint, nint, nint, CocoaRuntime.CGPoint, nuint, void>)&Ended,
        "v@:@{CGPoint=dd}Q");
    CocoaRuntime.objc_registerClassPair(created);
    _class = created;
  }

  [UnmanagedCallersOnly]
  private static nuint SourceOperationMask(nint self, nint selector, nint session, nint context)
      => _sessions.TryGetValue(self, out var state) ? state.Mask : 0;

  [UnmanagedCallersOnly]
  private static void Ended(nint self, nint selector, nint session, CocoaRuntime.CGPoint screenPoint, nuint operation) {
    if (!_sessions.TryRemove(self, out var state))
      return;

    // Released once AppKit is done with this callback, not from inside it.
    CocoaRuntime.SendPointer(self, CocoaRuntime.sel_registerName("autorelease"));
    try {
      state.Completed(ToEffects(operation) & state.Allowed);
    } catch {
      // An exception must not unwind into AppKit's frames.
    }
  }
}
