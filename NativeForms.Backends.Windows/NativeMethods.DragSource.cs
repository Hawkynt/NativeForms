using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The shell drag source behind dragging files out of a window (PRD §8): absolute PIDLs from
/// <c>SHParseDisplayName</c>, a shell item array over them, the array's own <c>IDataObject</c>, and
/// <c>SHDoDragDrop</c> with the shell's default drop source.
/// </summary>
/// <remarks>
/// COM without COM interop, the same way the Direct2D surface does it (see
/// <c>NativeMethods.DirectWrite.cs</c>): the one interface method needed is called through the
/// object's vtable, and the data object is only ever passed on as a pointer. Nothing here implements
/// a COM interface — the shell supplies both the data object and the drop source — so no managed
/// object is ever handed to native code.
/// </remarks>
internal static unsafe partial class NativeMethods {
  /// <summary><c>DRAGDROP_S_DROP</c>: the drag ended in a drop.</summary>
  internal const int DRAGDROP_S_DROP = 0x00040100;

  /// <summary><c>RPC_E_CHANGED_MODE</c>: the thread is already a multithreaded apartment.</summary>
  internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

  /// <summary>The primary (left, unless swapped) mouse button's virtual key.</summary>
  internal const int VK_LBUTTON = 0x01;

  /// <summary>The secondary mouse button's virtual key.</summary>
  internal const int VK_RBUTTON = 0x02;

  /// <summary>The middle mouse button's virtual key.</summary>
  internal const int VK_MBUTTON = 0x04;

  /// <summary><c>BHID_DataObject</c>: binds a shell item array to an <c>IDataObject</c> over its items.</summary>
  internal static readonly Guid BHID_DataObject = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");

  /// <summary><c>IID_IDataObject</c>.</summary>
  internal static readonly Guid IID_IDataObject = new("0000010e-0000-0000-c000-000000000046");

  /// <summary>Makes the calling thread an OLE single-threaded apartment; <c>S_FALSE</c> when it already was.</summary>
  [LibraryImport("ole32.dll")]
  internal static partial int OleInitialize(nint reserved);

  /// <summary>Parses a file-system path into an absolute PIDL, to free with <see cref="ILFree"/>.</summary>
  [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
  internal static partial int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);

  /// <summary>Builds an <c>IShellItemArray</c> over absolute PIDLs; the array copies them.</summary>
  [LibraryImport("shell32.dll")]
  internal static partial int SHCreateShellItemArrayFromIDLists(uint count, nint* pidls, out nint itemArray);

  /// <summary>Runs a modal OLE drag of a data object, with the shell's default drop source when <paramref name="dropSource"/> is zero.</summary>
  [LibraryImport("shell32.dll")]
  internal static partial int SHDoDragDrop(nint hwnd, nint dataObject, nint dropSource, uint allowedEffects, out uint effect);

  /// <summary>Frees a PIDL the shell allocated.</summary>
  [LibraryImport("shell32.dll")]
  internal static partial void ILFree(nint pidl);

  /// <summary><c>IShellItemArray::BindToHandler</c> — slot 3 (3 IUnknown + the first of its own).</summary>
  internal static int BindToHandler(nint itemArray, in Guid handler, in Guid iid, out nint result) {
    fixed (Guid* h = &handler)
    fixed (Guid* i = &iid)
    fixed (nint* r = &result)
      return ((delegate* unmanaged<nint, nint, Guid*, Guid*, nint*, int>)Slot(itemArray, 3))(itemArray, 0, h, i, r);
  }
}
