using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// AppKit's external file-drop destination for a top-level window. The existing flipped content view
/// registers only <c>NSPasteboardTypeFileURL</c>; successful drops are translated to the same managed
/// file-list payload and <see cref="ExternalDropBridge"/> path the Win32 and GTK backends use.
/// </summary>
internal static unsafe class CocoaExternalDrop {
  private const nuint _Copy = 1; // NSDragOperationCopy

  private static readonly ConcurrentDictionary<nint, CocoaWindowPeer> _windows = new();
  private static nint _installedClass;

  /// <summary>Adds the two destination callbacks to the backend's flipped view class and registers one view.</summary>
  internal static void Attach(nint view, CocoaWindowPeer owner) {
    if (view == 0)
      return;

    var cls = CocoaRuntime.object_getClass(view);
    if (cls == 0)
      return;

    if (_installedClass != cls) {
      CocoaRuntime.class_addMethod(
          cls,
          CocoaRuntime.sel_registerName("draggingEntered:"),
          (nint)(delegate* unmanaged<nint, nint, nint, nuint>)&DraggingEntered,
          "Q@:@");
      CocoaRuntime.class_addMethod(
          cls,
          CocoaRuntime.sel_registerName("performDragOperation:"),
          (nint)(delegate* unmanaged<nint, nint, nint, byte>)&PerformDragOperation,
          "c@:@");
      _installedClass = cls;
    }

    var fileType = CocoaRuntime.Constant("NSPasteboardTypeFileURL");
    var arrays = CocoaRuntime.objc_getClass("NSArray");
    var types = fileType == 0 || arrays == 0
        ? 0
        : CocoaRuntime.SendPointer(arrays, CocoaRuntime.sel_registerName("arrayWithObject:"), fileType);
    if (types == 0)
      return;

    _windows[view] = owner;
    CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("registerForDraggedTypes:"), types);
  }

  /// <summary>Stops retaining a window peer after its native window is closed.</summary>
  internal static void Forget(nint view) {
    if (view != 0)
      _windows.TryRemove(view, out _);
  }

  [UnmanagedCallersOnly]
  private static nuint DraggingEntered(nint self, nint selector, nint draggingInfo) {
    try {
      if (!_windows.ContainsKey(self) || draggingInfo == 0)
        return 0;

      var sourceMask = unchecked((nuint)CocoaRuntime.SendInteger(
          draggingInfo,
          CocoaRuntime.sel_registerName("draggingSourceOperationMask")));
      return (sourceMask & _Copy) != 0 ? _Copy : 0;
    } catch {
      return 0;
    }
  }

  [UnmanagedCallersOnly]
  private static byte PerformDragOperation(nint self, nint selector, nint draggingInfo) {
    try {
      if (!_windows.TryGetValue(self, out var owner) || draggingInfo == 0)
        return 0;

      var files = ReadFiles(draggingInfo);
      if (files.Length == 0)
        return 0;

      var location = CocoaRuntime.SendPoint(draggingInfo, CocoaRuntime.sel_registerName("draggingLocation"));
      var local = CocoaRuntime.SendConvert(
          self,
          CocoaRuntime.sel_registerName("convertPoint:fromView:"),
          location,
          0);
      var screen = owner.PointToScreen(new Point((int)Math.Round(local.X), (int)Math.Round(local.Y)));
      return ExternalDropBridge.Route(owner, files, DragDropEffects.Copy, screen) == DragDropEffects.Copy
          ? (byte)1
          : (byte)0;
    } catch {
      return 0;
    }
  }

  private static string[] ReadFiles(nint draggingInfo) {
    var pasteboard = CocoaRuntime.SendPointer(draggingInfo, CocoaRuntime.sel_registerName("draggingPasteboard"));
    var urlClass = CocoaRuntime.objc_getClass("NSURL");
    var arrayClass = CocoaRuntime.objc_getClass("NSArray");
    if (pasteboard == 0 || urlClass == 0 || arrayClass == 0)
      return [];

    var classes = CocoaRuntime.SendPointer(arrayClass, CocoaRuntime.sel_registerName("arrayWithObject:"), urlClass);
    var urls = classes == 0
        ? 0
        : CocoaRuntime.SendPointer(
            pasteboard,
            CocoaRuntime.sel_registerName("readObjectsForClasses:options:"),
            classes,
            0);
    if (urls == 0)
      return [];

    var count = CocoaRuntime.SendInteger(urls, CocoaRuntime.sel_registerName("count"));
    if (count <= 0 || count > int.MaxValue)
      return [];

    var result = new List<string>((int)count);
    for (nint i = 0; i < count; ++i) {
      var url = CocoaRuntime.SendIndex(urls, CocoaRuntime.sel_registerName("objectAtIndex:"), i);
      if (url == 0 || !CocoaRuntime.SendBool(url, CocoaRuntime.sel_registerName("isFileURL")))
        continue;

      var path = CocoaRuntime.SendPointer(url, CocoaRuntime.sel_registerName("path"));
      var utf8 = path == 0 ? 0 : CocoaRuntime.SendPointer(path, CocoaRuntime.sel_registerName("UTF8String"));
      if (utf8 != 0 && Marshal.PtrToStringUTF8(utf8) is { Length: > 0 } text)
        result.Add(text);
    }

    return result.ToArray();
  }
}
