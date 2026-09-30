using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The OLE drop target of a top-level window (PRD §8): an <c>IDropTarget</c> registered with
/// <c>RegisterDragDrop</c>, which OLE calls for the whole hover of any drag over the window —
/// enter, every move, leave or drop — and which forwards each to
/// <see cref="ExternalDropBridge"/>, so managed targets get continuous <see cref="Control.DragOver"/>.
/// </summary>
/// <remarks>
/// <para>
/// The data object is translated once, when the drag enters (<see cref="Win32DroppedData"/>): real
/// paths as a <c>string[]</c>, otherwise virtual files as a <c>VirtualFile[]</c>. A data object with
/// neither is refused for the whole hover. The translation is let go of after the drop has been
/// delivered, or when the drag leaves.
/// </para>
/// <para>
/// Registration needs OLE on the UI thread, i.e. a single-threaded apartment (<c>[STAThread]</c>).
/// Where that is unavailable <see cref="Register"/> declines and the window keeps the shell's
/// <c>WM_DROPFILES</c> drop, which carries only real paths and only the final drop.
/// </para>
/// </remarks>
internal sealed unsafe class Win32DropTarget : IWin32ComCallable {
  private static readonly void** _vtable = CreateVtable();

  private readonly IWindowPeer _window;
  private Win32DroppedData? _dropped;

  private Win32DropTarget(IWindowPeer window) => _window = window;

  /// <summary>A new drop target for <paramref name="window"/> with one reference, owned by the caller.</summary>
  internal static nint Create(IWindowPeer window) => Win32ComObject.Create(_vtable, new Win32DropTarget(window));

  /// <summary>
  /// Registers a drop target for <paramref name="window"/> on <paramref name="hwnd"/>; answers whether
  /// OLE took it. OLE keeps the only reference until the window revokes it.
  /// </summary>
  internal static bool Register(nint hwnd, IWindowPeer window) {
    if (hwnd == 0 || !Win32Ole.EnsureInitialized())
      return false;

    var target = Create(window);
    try {
      return NativeMethods.RegisterDragDrop(hwnd, target) >= 0;
    } finally {
      NativeMethods.Release(target);
    }
  }

  /// <inheritdoc/>
  public bool Supports(in Guid iid) => iid == NativeMethods.IID_IDropTarget;

  private static void** CreateVtable() {
    var vtable = Win32ComObject.CreateVtable(7);
    vtable[3] = (delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)&DragEnter;
    vtable[4] = (delegate* unmanaged<nint, uint, NativeMethods.POINT, uint*, int>)&DragOver;
    vtable[5] = (delegate* unmanaged<nint, int>)&DragLeave;
    vtable[6] = (delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)&Drop;
    return vtable;
  }

  /// <summary><c>IDropTarget::DragEnter</c>: translates the data object and asks the target under the pointer.</summary>
  [UnmanagedCallersOnly]
  private static int DragEnter(nint self, nint dataObject, uint keyState, NativeMethods.POINT point, uint* effect) {
    if (effect == null)
      return NativeMethods.E_INVALIDARG;

    var allowed = Win32FileDragSource.ToEffects(*effect);
    *effect = 0;
    try {
      if (Win32ComObject.Target<Win32DropTarget>(self) is not { } target)
        return NativeMethods.E_UNEXPECTED;

      target.Forget();
      target._dropped = Win32DroppedData.Read(dataObject);
      if (target._dropped is { } dropped)
        *effect = Win32FileDragSource.ToDropEffect(
            ExternalDropBridge.DragEnter(target._window, dropped.Payload, allowed, new Point(point.x, point.y)));

      return NativeMethods.S_OK;
    } catch {
      // An exception must not unwind into OLE's frames.
      return NativeMethods.E_UNEXPECTED;
    }
  }

  /// <summary><c>IDropTarget::DragOver</c>: the enter/over/leave sequence for the target under the pointer.</summary>
  [UnmanagedCallersOnly]
  private static int DragOver(nint self, uint keyState, NativeMethods.POINT point, uint* effect) {
    if (effect == null)
      return NativeMethods.E_INVALIDARG;

    var allowed = Win32FileDragSource.ToEffects(*effect);
    *effect = 0;
    try {
      if (Win32ComObject.Target<Win32DropTarget>(self) is { _dropped: not null } target)
        *effect = Win32FileDragSource.ToDropEffect(ExternalDropBridge.DragOver(target._window, allowed, new Point(point.x, point.y)));

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.E_UNEXPECTED;
    }
  }

  /// <summary><c>IDropTarget::DragLeave</c>: the target is left and the translation let go of.</summary>
  [UnmanagedCallersOnly]
  private static int DragLeave(nint self) {
    try {
      if (Win32ComObject.Target<Win32DropTarget>(self) is { } target) {
        if (target._dropped is not null)
          ExternalDropBridge.DragLeave(target._window);

        target.Forget();
      }

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.E_UNEXPECTED;
    }
  }

  /// <summary><c>IDropTarget::Drop</c>: delivers the drop, then lets go of the translation.</summary>
  [UnmanagedCallersOnly]
  private static int Drop(nint self, nint dataObject, uint keyState, NativeMethods.POINT point, uint* effect) {
    if (effect == null)
      return NativeMethods.E_INVALIDARG;

    var allowed = Win32FileDragSource.ToEffects(*effect);
    *effect = 0;
    try {
      if (Win32ComObject.Target<Win32DropTarget>(self) is not { } target)
        return NativeMethods.E_UNEXPECTED;

      try {
        // OLE always enters before it drops; a drop out of nowhere is translated here.
        if (target._dropped is null && Win32DroppedData.Read(dataObject) is { } late) {
          target._dropped = late;
          ExternalDropBridge.DragEnter(target._window, late.Payload, allowed, new Point(point.x, point.y));
        }

        if (target._dropped is not null)
          *effect = Win32FileDragSource.ToDropEffect(ExternalDropBridge.Drop(target._window, allowed, new Point(point.x, point.y)));
      } finally {
        target.Forget();
      }

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.E_UNEXPECTED;
    }
  }

  /// <summary>Lets go of the translated data object, ending the virtual files' access to it.</summary>
  private void Forget() {
    var dropped = _dropped;
    _dropped = null;
    dropped?.Release();
  }
}
