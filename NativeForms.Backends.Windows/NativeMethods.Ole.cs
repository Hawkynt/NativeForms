using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The OLE data-transfer surface behind virtual files (PRD §8): the drop-target registration, the
/// <c>FORMATETC</c>/<c>STGMEDIUM</c> pair every <c>IDataObject</c> call exchanges, the shell's
/// <c>FILEGROUPDESCRIPTORW</c> layout, and vtable calls into data objects and streams other
/// processes hand us.
/// </summary>
/// <remarks>
/// COM without COM interop, as everywhere in this backend: foreign objects are called by indexing
/// their vtable (see <c>NativeMethods.DirectWrite.cs</c>), and the objects this backend implements
/// itself are built by <see cref="Win32ComObject"/>. All layouts are the Windows SDK's, and every
/// member falls on its natural alignment, so sequential layout matches both 32- and 64-bit.
/// </remarks>
internal static unsafe partial class NativeMethods {
  // --- HRESULTs ---

  internal const int S_OK = 0;
  internal const int S_FALSE = 1;
  internal const int E_NOTIMPL = unchecked((int)0x80004001);
  internal const int E_NOINTERFACE = unchecked((int)0x80004002);
  internal const int E_POINTER = unchecked((int)0x80004003);
  internal const int E_FAIL = unchecked((int)0x80004005);
  internal const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
  internal const int E_INVALIDARG = unchecked((int)0x80070057);
  internal const int E_OUTOFMEMORY = unchecked((int)0x8007000E);

  /// <summary><c>OLE_E_ADVISENOTSUPPORTED</c>: the data object keeps no advise connections.</summary>
  internal const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);

  /// <summary><c>DV_E_FORMATETC</c>: the clipboard format is not offered.</summary>
  internal const int DV_E_FORMATETC = unchecked((int)0x80040064);

  /// <summary><c>DV_E_LINDEX</c>: the item index is out of range, or names an item without content.</summary>
  internal const int DV_E_LINDEX = unchecked((int)0x80040068);

  /// <summary><c>DV_E_TYMED</c>: none of the requested storage media is offered.</summary>
  internal const int DV_E_TYMED = unchecked((int)0x80040069);

  /// <summary><c>DV_E_DVASPECT</c>: only <c>DVASPECT_CONTENT</c> is offered.</summary>
  internal const int DV_E_DVASPECT = unchecked((int)0x8004006B);

  /// <summary><c>STG_E_INVALIDFUNCTION</c>: the stream cannot do that (seek on a forward-only stream).</summary>
  internal const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);

  /// <summary><c>STG_E_ACCESSDENIED</c>: the stream is read-only.</summary>
  internal const int STG_E_ACCESSDENIED = unchecked((int)0x80030005);

  /// <summary><c>STG_E_INVALIDPOINTER</c>.</summary>
  internal const int STG_E_INVALIDPOINTER = unchecked((int)0x80030009);

  /// <summary><c>STG_E_READFAULT</c>: producing the content failed.</summary>
  internal const int STG_E_READFAULT = unchecked((int)0x8003001E);

  // --- Data transfer ---

  /// <summary><c>CF_HDROP</c>: a <c>DROPFILES</c> list of existing paths.</summary>
  internal const ushort CF_HDROP = 15;

  internal const uint TYMED_HGLOBAL = 1;
  internal const uint TYMED_ISTREAM = 4;
  internal const uint TYMED_ISTORAGE = 8;

  internal const uint DVASPECT_CONTENT = 1;

  /// <summary><c>DATADIR_GET</c>: enumerate the formats <c>GetData</c> offers.</summary>
  internal const uint DATADIR_GET = 1;

  /// <summary><c>GMEM_MOVEABLE | GMEM_ZEROINIT</c>, the allocation an <c>HGLOBAL</c> medium uses.</summary>
  internal const uint GHND = 0x0042;

  /// <summary>The name of <c>CFSTR_FILEDESCRIPTORW</c>.</summary>
  internal const string CFSTR_FILEDESCRIPTORW = "FileGroupDescriptorW";

  /// <summary>The name of <c>CFSTR_FILECONTENTS</c>.</summary>
  internal const string CFSTR_FILECONTENTS = "FileContents";

  // --- FILEDESCRIPTORW ---

  internal const uint FD_ATTRIBUTES = 0x00000004;
  internal const uint FD_WRITESTIME = 0x00000020;
  internal const uint FD_FILESIZE = 0x00000040;
  internal const uint FD_PROGRESSUI = 0x00004000;
  internal const uint FD_UNICODE = 0x80000000;

  internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
  internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;

  /// <summary><c>MAX_PATH</c>: the capacity of <c>FILEDESCRIPTORW.cFileName</c>, terminator included.</summary>
  internal const int MAX_PATH = 260;

  /// <summary><c>sizeof(FILEDESCRIPTORW)</c>.</summary>
  internal const int FileDescriptorSize = 592;

  // --- IStream ---

  internal const uint STREAM_SEEK_SET = 0;
  internal const uint STREAM_SEEK_CUR = 1;
  internal const uint STREAM_SEEK_END = 2;

  /// <summary><c>STGTY_STREAM</c>.</summary>
  internal const uint STGTY_STREAM = 2;

  /// <summary><c>STATFLAG_NONAME</c>: <c>Stat</c> must not allocate the name.</summary>
  internal const uint STATFLAG_NONAME = 1;

  // --- Interface identifiers ---

  internal static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
  internal static readonly Guid IID_IDropTarget = new("00000122-0000-0000-c000-000000000046");
  internal static readonly Guid IID_ISequentialStream = new("0c733a30-2a1c-11ce-ade5-00aa0044773d");
  internal static readonly Guid IID_IStream = new("0000000c-0000-0000-c000-000000000046");

  /// <summary><c>FORMATETC</c>: which clipboard format, item, aspect and storage media.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct FORMATETC {
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
  }

  /// <summary><c>STGMEDIUM</c>: the medium (one handle of the union) and who releases it.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct STGMEDIUM {
    public uint tymed;
    public nint handle;
    public nint pUnkForRelease;
  }

  /// <summary><c>FILETIME</c>: 100-nanosecond intervals since 1601, split in halves.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct FILETIME {
    public uint dwLowDateTime;
    public uint dwHighDateTime;
  }

  /// <summary><c>STATSTG</c>, as <c>IStream::Stat</c> fills it.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct STATSTG {
    public nint pwcsName;
    public uint type;
    public ulong cbSize;
    public FILETIME mtime;
    public FILETIME ctime;
    public FILETIME atime;
    public uint grfMode;
    public uint grfLocksSupported;
    public Guid clsid;
    public uint grfStateBits;
    public uint reserved;
  }

  /// <summary>Registers a window's OLE drop target; OLE keeps a reference until <see cref="RevokeDragDrop"/>.</summary>
  [LibraryImport("ole32.dll")]
  internal static partial int RegisterDragDrop(nint hwnd, nint dropTarget);

  /// <summary>Unregisters a window's OLE drop target and releases OLE's reference.</summary>
  [LibraryImport("ole32.dll")]
  internal static partial int RevokeDragDrop(nint hwnd);

  /// <summary>Frees a medium the way its <c>pUnkForRelease</c> says.</summary>
  [LibraryImport("ole32.dll")]
  internal static partial void ReleaseStgMedium(STGMEDIUM* medium);

  /// <summary>Returns the id of a registered clipboard format, registering it on first use.</summary>
  [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  internal static partial uint RegisterClipboardFormatW(string format);

  /// <summary>The standard <c>IEnumFORMATETC</c> over a copy of <paramref name="formats"/>.</summary>
  [LibraryImport("shell32.dll")]
  internal static partial int SHCreateStdEnumFmtEtc(uint count, FORMATETC* formats, out nint enumerator);

  /// <summary>The byte size of a global memory block (which may exceed what was asked for).</summary>
  [LibraryImport("kernel32.dll")]
  internal static partial nuint GlobalSize(nint hMem);

  /// <summary>Allocates task memory the caller of <c>IStream::Stat</c> frees.</summary>
  [LibraryImport("ole32.dll")]
  internal static partial nint CoTaskMemAlloc(nuint size);

  // --- Vtable calls into foreign objects ---

  /// <summary><c>IUnknown::QueryInterface</c> — slot 0.</summary>
  internal static int QueryInterface(nint instance, in Guid iid, out nint result) {
    fixed (Guid* i = &iid)
    fixed (nint* r = &result)
      return ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(instance, 0))(instance, i, r);
  }

  /// <summary><c>IUnknown::AddRef</c> — slot 1.</summary>
  internal static void AddRef(nint instance) {
    if (instance != 0)
      ((delegate* unmanaged<nint, uint>)Slot(instance, 1))(instance);
  }

  /// <summary><c>IDataObject::GetData</c> — slot 3.</summary>
  internal static int GetData(nint dataObject, FORMATETC* format, STGMEDIUM* medium)
      => ((delegate* unmanaged<nint, FORMATETC*, STGMEDIUM*, int>)Slot(dataObject, 3))(dataObject, format, medium);

  /// <summary><c>IDataObject::QueryGetData</c> — slot 5.</summary>
  internal static int QueryGetData(nint dataObject, FORMATETC* format)
      => ((delegate* unmanaged<nint, FORMATETC*, int>)Slot(dataObject, 5))(dataObject, format);

  /// <summary><c>ISequentialStream::Read</c> — slot 3.</summary>
  internal static int StreamRead(nint stream, void* buffer, uint count, uint* read)
      => ((delegate* unmanaged<nint, void*, uint, uint*, int>)Slot(stream, 3))(stream, buffer, count, read);
}
