using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>A managed object this backend exposes to OLE as a COM object built by <see cref="Win32ComObject"/>.</summary>
internal interface IWin32ComCallable {
  /// <summary>Whether <c>QueryInterface</c> answers <paramref name="iid"/> with this object (IUnknown is implied).</summary>
  bool Supports(in Guid iid);
}

/// <summary>
/// Builds COM objects without COM interop (PRD §8): a block of native memory whose first field is the
/// vtable pointer, followed by a <see cref="GCHandle"/> to the managed object and the reference count.
/// </summary>
/// <remarks>
/// <para>
/// Every vtable is a static block of native memory filled with <see cref="UnmanagedCallersOnlyAttribute"/>
/// function pointers; its first three slots are the shared <c>IUnknown</c> implementation here, and
/// each method recovers its managed object from the handle — no delegate is marshalled, nothing is
/// generated at run time, so it stays trim- and NativeAOT-safe.
/// </para>
/// <para>
/// The count starts at one, owned by whoever called <see cref="Create"/>. When the last reference is
/// released the handle and the block are freed, and a managed object that is <see cref="IDisposable"/>
/// is disposed. Every interface is served by the object's one vtable, so <c>QueryInterface</c> answers
/// with the same pointer; that only works for interfaces that extend each other (IStream extends
/// ISequentialStream), which is all this backend implements.
/// </para>
/// </remarks>
internal static unsafe class Win32ComObject {
  [StructLayout(LayoutKind.Sequential)]
  private struct Instance {
    public void** Vtable;
    public nint Handle;
    public int References;
  }

  /// <summary>A vtable of <paramref name="slots"/> entries with the three <c>IUnknown</c> slots filled in; lives forever.</summary>
  internal static void** CreateVtable(int slots) {
    var vtable = (void**)NativeMemory.AllocZeroed((nuint)slots, (nuint)sizeof(void*));
    vtable[0] = (delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterface;
    vtable[1] = (delegate* unmanaged<nint, uint>)&AddRef;
    vtable[2] = (delegate* unmanaged<nint, uint>)&Release;
    return vtable;
  }

  /// <summary>A new COM object over <paramref name="target"/> with one reference, owned by the caller.</summary>
  internal static nint Create(void** vtable, IWin32ComCallable target) {
    var instance = (Instance*)NativeMemory.AllocZeroed((nuint)sizeof(Instance));
    instance->Vtable = vtable;
    instance->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(target));
    instance->References = 1;
    return (nint)instance;
  }

  /// <summary>The managed object behind <paramref name="self"/>.</summary>
  internal static T? Target<T>(nint self) where T : class
      => self == 0 ? null : GCHandle.FromIntPtr(((Instance*)self)->Handle).Target as T;

  /// <summary>The current reference count, for tests.</summary>
  internal static int References(nint self) => ((Instance*)self)->References;

  [UnmanagedCallersOnly]
  private static int QueryInterface(nint self, Guid* iid, nint* result) {
    if (result == null)
      return NativeMethods.E_POINTER;

    *result = 0;
    if (iid == null)
      return NativeMethods.E_INVALIDARG;

    if (*iid != NativeMethods.IID_IUnknown && Target<IWin32ComCallable>(self)?.Supports(*iid) != true)
      return NativeMethods.E_NOINTERFACE;

    Interlocked.Increment(ref ((Instance*)self)->References);
    *result = self;
    return NativeMethods.S_OK;
  }

  [UnmanagedCallersOnly]
  private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref ((Instance*)self)->References);

  [UnmanagedCallersOnly]
  private static uint Release(nint self) {
    var left = Interlocked.Decrement(ref ((Instance*)self)->References);
    if (left != 0)
      return (uint)left;

    var handle = GCHandle.FromIntPtr(((Instance*)self)->Handle);
    var target = handle.Target;
    handle.Free();
    NativeMemory.Free((void*)self);
    try {
      (target as IDisposable)?.Dispose();
    } catch {
      // An exception must not unwind into the caller's native frames.
    }

    return 0;
  }
}
