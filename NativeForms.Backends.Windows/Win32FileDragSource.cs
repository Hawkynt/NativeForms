namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Drags files out of a window through the shell (PRD §8): the shell builds the data object for the
/// paths — <c>CF_HDROP</c>, shell ID lists and the drag image Explorer draws — and supplies the default
/// drop source, so the drop lands in Explorer, on the desktop or in any OLE drop target exactly as a
/// drag out of Explorer would.
/// </summary>
/// <remarks>
/// <para>
/// <c>SHDoDragDrop</c> is modal: it runs OLE's own message loop until the button is released and
/// returns the effect the target performed, which is reported before this returns. The core only
/// hands a drag over once the pointer has left the application's window. Should the pointer come
/// back, the window's own OLE drop target (<see cref="Win32DropTarget"/>) receives the drag like any
/// other.
/// </para>
/// <para>
/// OLE drag and drop needs the UI thread to be a single-threaded apartment. The runtime makes a
/// <c>Main</c> without <c>[STAThread]</c> a multithreaded one before any code runs, and that cannot
/// be undone, so OLE cannot be initialized there (<see cref="Win32Ole"/>): the drag then declines and
/// stays in process. Windows Forms makes the same demand.
/// </para>
/// </remarks>
internal static unsafe class Win32FileDragSource {
  /// <summary>Runs the shell drag of <paramref name="paths"/> from <paramref name="hwnd"/>.</summary>
  /// <returns><see langword="false"/> when no drag could be started, so the drag stays in process;
  /// otherwise the drag has already ended and <paramref name="completed"/> has been told its effect.</returns>
  internal static bool TryDrag(nint hwnd, string[] paths, DragDropEffects allowedEffects, Action<DragDropEffects> completed) {
    DragDropEffects effect;

    // SHDoDragDrop with no button held drops at once, wherever the pointer happens to be.
    if (hwnd == 0 || paths.Length == 0 || !IsMouseButtonHeld() || !Win32Ole.EnsureInitialized())
      return false;

    var pidls = new nint[paths.Length];
    nint itemArray = 0;
    nint dataObject = 0;
    try {
      for (var i = 0; i < paths.Length; ++i)
        if (NativeMethods.SHParseDisplayName(paths[i], 0, out pidls[i], 0, out _) < 0 || pidls[i] == 0)
          return false;

      int result;
      fixed (nint* list = pidls)
        result = NativeMethods.SHCreateShellItemArrayFromIDLists((uint)pidls.Length, list, out itemArray);

      if (result < 0 || itemArray == 0)
        return false;

      if (NativeMethods.BindToHandler(itemArray, NativeMethods.BHID_DataObject, NativeMethods.IID_IDataObject, out dataObject) < 0
          || dataObject == 0)
        return false;

      result = NativeMethods.SHDoDragDrop(hwnd, dataObject, 0, ToDropEffect(allowedEffects), out var performed);
      if (result < 0)
        return false;

      effect = result == NativeMethods.DRAGDROP_S_DROP ? ToEffects(performed) & allowedEffects : DragDropEffects.None;
    } finally {
      NativeMethods.Release(dataObject);
      NativeMethods.Release(itemArray);
      foreach (var pidl in pidls)
        if (pidl != 0)
          NativeMethods.ILFree(pidl);
    }

    completed(effect);
    return true;
  }

  /// <summary>The <c>DROPEFFECT</c> mask for toolkit effects; Copy, Move and Link share their values.</summary>
  internal static uint ToDropEffect(DragDropEffects effects) => (uint)(effects & DragDropEffects.All);

  /// <summary>The toolkit effects in a <c>DROPEFFECT</c>; <c>DROPEFFECT_SCROLL</c> and unknown bits are dropped.</summary>
  internal static DragDropEffects ToEffects(uint dropEffect) => (DragDropEffects)dropEffect & DragDropEffects.All;

  /// <summary>Whether a mouse button is down according to the message being processed.</summary>
  private static bool IsMouseButtonHeld()
      => (NativeMethods.GetKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
      || (NativeMethods.GetKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0
      || (NativeMethods.GetKeyState(NativeMethods.VK_MBUTTON) & 0x8000) != 0;
}
