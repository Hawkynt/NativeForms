namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// OLE on the UI thread (PRD §8), shared by the drag sources and the drop target, and the clipboard
/// formats of virtual files. OLE drag and drop needs a single-threaded apartment; the runtime makes a
/// <c>Main</c> without <c>[STAThread]</c> a multithreaded one before any code runs, and that cannot be
/// undone, so <see cref="NativeMethods.OleInitialize"/> fails there with <c>RPC_E_CHANGED_MODE</c> and
/// every OLE feature declines. Windows Forms makes the same demand.
/// </summary>
internal static class Win32Ole {
  /// <summary>Whether OLE is usable on this thread: 0 not yet asked, 1 yes, -1 no.</summary>
  [ThreadStatic]
  private static int _state;

  /// <summary>The registered <c>CFSTR_FILEDESCRIPTORW</c> clipboard format.</summary>
  internal static ushort DescriptorFormat => field != 0 ? field : field = (ushort)NativeMethods.RegisterClipboardFormatW(NativeMethods.CFSTR_FILEDESCRIPTORW);

  /// <summary>The registered <c>CFSTR_FILECONTENTS</c> clipboard format.</summary>
  internal static ushort ContentsFormat => field != 0 ? field : field = (ushort)NativeMethods.RegisterClipboardFormatW(NativeMethods.CFSTR_FILECONTENTS);

  /// <summary>Initializes OLE on this thread once; answers whether it is usable.</summary>
  /// <remarks>The initialization is kept for the thread's lifetime rather than balanced per use, so
  /// nothing ever tears down an apartment something else on the thread may since rely on.</remarks>
  internal static bool EnsureInitialized() {
    if (_state == 0)
      _state = NativeMethods.OleInitialize(0) >= 0 ? 1 : -1;

    return _state > 0;
  }
}
