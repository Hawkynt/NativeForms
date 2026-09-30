using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The <c>IDataObject</c> of virtual files dragged out (PRD §8): <c>CFSTR_FILEDESCRIPTORW</c> describes
/// every entry, and <c>CFSTR_FILECONTENTS</c> at an entry's index hands its content over as an
/// <c>IStream</c> (or, to a target that only takes it, an <c>HGLOBAL</c>). The drop target pulls each
/// file itself, so Explorer writes it straight to the destination; nothing is staged on disk here.
/// </summary>
/// <remarks>
/// <para>
/// Folders are carried as descriptors with <c>FILE_ATTRIBUTE_DIRECTORY</c>, a parent always before its
/// children; folders that the entries only imply are added. Each descriptor asks for Explorer's
/// progress UI and carries the size and the modification time when the entry knows them; a size
/// beyond 4 GB is split across <c>nFileSizeHigh</c>/<c>nFileSizeLow</c>.
/// </para>
/// <para>
/// <c>SetData</c> keeps whatever <c>HGLOBAL</c> formats the shell stores on a drag's data object — the
/// drag image, <c>Preferred DropEffect</c>, <c>Performed DropEffect</c>, … — and hands copies back
/// through <c>GetData</c>, which is what the shell's drag image and move handshake need. Advise
/// connections are not supported, and <c>GetDataHere</c> is not implemented; no drop target needs
/// either for files.
/// </para>
/// <para>
/// What happens to a half-copied file is the drop target's business, not this object's: Explorer
/// reads the stream into the destination file directly, and a copy that fails or is cancelled is
/// cleaned up (or not) by Explorer.
/// </para>
/// </remarks>
internal sealed unsafe class Win32VirtualFileDataObject : IWin32ComCallable {
  private static readonly void** _vtable = CreateVtable();

  private static readonly Guid IID_IDataObject = new("0000010e-0000-0000-c000-000000000046");

  /// <summary>Every entry in descriptor order: the given ones plus the folders they imply, parents first.</summary>
  private readonly VirtualFile[] _entries;

  /// <summary>Formats the shell stored through <c>SetData</c>, by clipboard format.</summary>
  private readonly Dictionary<ushort, byte[]> _stored = [];

  private Win32VirtualFileDataObject(VirtualFile[] entries) => _entries = entries;

  /// <summary>
  /// A new data object over <paramref name="files"/> with one reference, owned by the caller.
  /// </summary>
  /// <exception cref="ArgumentException">An entry's path does not fit a descriptor's <c>MAX_PATH</c> name.</exception>
  internal static nint Create(VirtualFile[] files) => Win32ComObject.Create(_vtable, new Win32VirtualFileDataObject(Order(files)));

  /// <summary>The entries of a descriptor, for tests: the given ones plus implied folders, parents first.</summary>
  internal static VirtualFile[] Order(VirtualFile[] files) {
    var entries = new List<VirtualFile>(files.Length);
    var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files) {
      if (file.RelativePath.Length >= NativeMethods.MAX_PATH)
        throw new ArgumentException($"'{file.RelativePath}' is longer than a file descriptor can carry.", nameof(files));

      // Every parent folder first, once.
      for (var slash = file.RelativePath.IndexOf('/'); slash > 0; slash = file.RelativePath.IndexOf('/', slash + 1)) {
        var parent = file.RelativePath[..slash];
        if (folders.Add(parent))
          entries.Add(VirtualFile.Directory(parent));
      }

      if (!file.IsDirectory || folders.Add(file.RelativePath))
        entries.Add(file);
    }

    return [.. entries];
  }

  /// <inheritdoc/>
  public bool Supports(in Guid iid) => iid == IID_IDataObject;

  private static void** CreateVtable() {
    var vtable = Win32ComObject.CreateVtable(12);
    vtable[3] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.STGMEDIUM*, int>)&GetData;
    vtable[4] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.STGMEDIUM*, int>)&GetDataHere;
    vtable[5] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, int>)&QueryGetData;
    vtable[6] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.FORMATETC*, int>)&GetCanonicalFormatEtc;
    vtable[7] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.STGMEDIUM*, int, int>)&SetData;
    vtable[8] = (delegate* unmanaged<nint, uint, nint*, int>)&EnumFormatEtc;
    vtable[9] = (delegate* unmanaged<nint, NativeMethods.FORMATETC*, uint, nint, uint*, int>)&DAdvise;
    vtable[10] = (delegate* unmanaged<nint, uint, int>)&DUnadvise;
    vtable[11] = (delegate* unmanaged<nint, nint*, int>)&EnumDAdvise;
    return vtable;
  }

  /// <summary>Whether <paramref name="format"/> can be served, as an HRESULT.</summary>
  private int Check(NativeMethods.FORMATETC* format) {
    if (format == null)
      return NativeMethods.E_INVALIDARG;

    if (format->dwAspect != NativeMethods.DVASPECT_CONTENT)
      return NativeMethods.DV_E_DVASPECT;

    if (format->cfFormat == Win32Ole.DescriptorFormat)
      return (format->tymed & NativeMethods.TYMED_HGLOBAL) != 0 ? NativeMethods.S_OK : NativeMethods.DV_E_TYMED;

    if (format->cfFormat == Win32Ole.ContentsFormat) {
      if ((format->tymed & (NativeMethods.TYMED_ISTREAM | NativeMethods.TYMED_HGLOBAL)) == 0)
        return NativeMethods.DV_E_TYMED;

      // lindex -1 asks whether contents exist at all; any other index must name a file.
      return format->lindex == -1 || (format->lindex >= 0 && format->lindex < _entries.Length && !_entries[format->lindex].IsDirectory)
          ? NativeMethods.S_OK
          : NativeMethods.DV_E_LINDEX;
    }

    if (_stored.ContainsKey(format->cfFormat))
      return (format->tymed & NativeMethods.TYMED_HGLOBAL) != 0 ? NativeMethods.S_OK : NativeMethods.DV_E_TYMED;

    return NativeMethods.DV_E_FORMATETC;
  }

  /// <summary><c>IDataObject::GetData</c>: the descriptor, an entry's contents, or a stored format.</summary>
  [UnmanagedCallersOnly]
  private static int GetData(nint self, NativeMethods.FORMATETC* format, NativeMethods.STGMEDIUM* medium) {
    if (medium == null)
      return NativeMethods.E_INVALIDARG;

    *medium = default;
    if (Win32ComObject.Target<Win32VirtualFileDataObject>(self) is not { } data)
      return NativeMethods.E_UNEXPECTED;

    try {
      var check = data.Check(format);
      if (check != NativeMethods.S_OK)
        return check;

      if (format->cfFormat == Win32Ole.DescriptorFormat)
        return Global(BuildDescriptor(data._entries), medium);

      if (format->cfFormat == Win32Ole.ContentsFormat)
        return format->lindex < 0 ? NativeMethods.DV_E_LINDEX : data.GetContents(data._entries[format->lindex], format->tymed, medium);

      return Global(data._stored[format->cfFormat], medium);
    } catch {
      return NativeMethods.E_FAIL;
    }
  }

  /// <summary>An entry's content: a fresh stream over it, or its bytes in global memory for a target that only takes that.</summary>
  private int GetContents(VirtualFile entry, uint tymed, NativeMethods.STGMEDIUM* medium) {
    Stream content;
    try {
      content = entry.OpenRead();
    } catch {
      return NativeMethods.STG_E_READFAULT;
    }

    if ((tymed & NativeMethods.TYMED_ISTREAM) != 0) {
      medium->tymed = NativeMethods.TYMED_ISTREAM;
      medium->handle = Win32ComStream.Create(content, entry.Name, entry.Length, entry.LastWriteTimeUtc);
      return NativeMethods.S_OK;
    }

    using (content) {
      using var buffer = new MemoryStream();
      try {
        content.CopyTo(buffer);
      } catch {
        return NativeMethods.STG_E_READFAULT;
      }

      return Global(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), medium);
    }
  }

  /// <summary>
  /// <c>FILEGROUPDESCRIPTORW</c>: the entry count, then one <c>FILEDESCRIPTORW</c> of
  /// <see cref="NativeMethods.FileDescriptorSize"/> bytes per entry, in the order given.
  /// </summary>
  internal static byte[] BuildDescriptor(VirtualFile[] entries) {
    var bytes = new byte[4 + entries.Length * NativeMethods.FileDescriptorSize];
    var span = bytes.AsSpan();
    Write(span, 0, (uint)entries.Length);
    for (var i = 0; i < entries.Length; ++i) {
      var entry = entries[i];
      var descriptor = span.Slice(4 + i * NativeMethods.FileDescriptorSize, NativeMethods.FileDescriptorSize);
      var flags = NativeMethods.FD_UNICODE | NativeMethods.FD_PROGRESSUI | NativeMethods.FD_ATTRIBUTES;
      Write(descriptor, 36, entry.IsDirectory ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL);
      if (entry.LastWriteTimeUtc is { } time) {
        var fileTime = Win32ComStream.ToFileTime(time);
        flags |= NativeMethods.FD_WRITESTIME;
        Write(descriptor, 56, fileTime.dwLowDateTime);
        Write(descriptor, 60, fileTime.dwHighDateTime);
      }

      if (!entry.IsDirectory && entry.Length is { } length) {
        flags |= NativeMethods.FD_FILESIZE;
        Write(descriptor, 64, (uint)((ulong)length >> 32));
        Write(descriptor, 68, (uint)length);
      }

      Write(descriptor, 0, flags);
      var name = MemoryMarshal.Cast<byte, char>(descriptor.Slice(72, NativeMethods.MAX_PATH * sizeof(char)));
      entry.RelativePath.Replace('/', '\\').AsSpan().CopyTo(name);
    }

    return bytes;

    static void Write(Span<byte> target, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(target[offset..], value);
  }

  /// <summary>Copies <paramref name="bytes"/> into a new <c>HGLOBAL</c> medium the receiver frees.</summary>
  private static int Global(ReadOnlySpan<byte> bytes, NativeMethods.STGMEDIUM* medium) {
    var memory = NativeMethods.GlobalAlloc(NativeMethods.GHND, (nuint)Math.Max(bytes.Length, 1));
    if (memory == 0)
      return NativeMethods.E_OUTOFMEMORY;

    var target = NativeMethods.GlobalLock(memory);
    bytes.CopyTo(new Span<byte>((void*)target, bytes.Length));
    NativeMethods.GlobalUnlock(memory);
    medium->tymed = NativeMethods.TYMED_HGLOBAL;
    medium->handle = memory;
    medium->pUnkForRelease = 0;
    return NativeMethods.S_OK;
  }

  [UnmanagedCallersOnly]
  private static int GetDataHere(nint self, NativeMethods.FORMATETC* format, NativeMethods.STGMEDIUM* medium) => NativeMethods.E_NOTIMPL;

  [UnmanagedCallersOnly]
  private static int QueryGetData(nint self, NativeMethods.FORMATETC* format) {
    try {
      return Win32ComObject.Target<Win32VirtualFileDataObject>(self)?.Check(format) ?? NativeMethods.E_UNEXPECTED;
    } catch {
      return NativeMethods.E_FAIL;
    }
  }

  [UnmanagedCallersOnly]
  private static int GetCanonicalFormatEtc(nint self, NativeMethods.FORMATETC* format, NativeMethods.FORMATETC* canonical) {
    if (canonical != null)
      canonical->ptd = 0;

    return NativeMethods.E_NOTIMPL;
  }

  /// <summary><c>IDataObject::SetData</c>: keeps a copy of an <c>HGLOBAL</c> format the shell stores.</summary>
  [UnmanagedCallersOnly]
  private static int SetData(nint self, NativeMethods.FORMATETC* format, NativeMethods.STGMEDIUM* medium, int release) {
    if (format == null || medium == null)
      return NativeMethods.E_INVALIDARG;

    if (Win32ComObject.Target<Win32VirtualFileDataObject>(self) is not { } data)
      return NativeMethods.E_UNEXPECTED;

    if (medium->tymed != NativeMethods.TYMED_HGLOBAL || format->cfFormat == Win32Ole.DescriptorFormat || format->cfFormat == Win32Ole.ContentsFormat)
      return NativeMethods.E_NOTIMPL;

    try {
      var size = (int)Math.Min(NativeMethods.GlobalSize(medium->handle), int.MaxValue);
      var source = NativeMethods.GlobalLock(medium->handle);
      if (source == 0)
        return NativeMethods.E_INVALIDARG;

      try {
        data._stored[format->cfFormat] = new ReadOnlySpan<byte>((void*)source, size).ToArray();
      } finally {
        NativeMethods.GlobalUnlock(medium->handle);
      }

      if (release != 0)
        NativeMethods.ReleaseStgMedium(medium);

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.E_FAIL;
    }
  }

  /// <summary><c>IDataObject::EnumFormatEtc</c>: the standard enumerator over what <c>GetData</c> serves.</summary>
  [UnmanagedCallersOnly]
  private static int EnumFormatEtc(nint self, uint direction, nint* enumerator) {
    if (enumerator == null)
      return NativeMethods.E_INVALIDARG;

    *enumerator = 0;
    if (direction != NativeMethods.DATADIR_GET)
      return NativeMethods.E_NOTIMPL;

    if (Win32ComObject.Target<Win32VirtualFileDataObject>(self) is not { } data)
      return NativeMethods.E_UNEXPECTED;

    try {
      var formats = new NativeMethods.FORMATETC[2 + data._stored.Count];
      formats[0] = new() { cfFormat = Win32Ole.DescriptorFormat, dwAspect = NativeMethods.DVASPECT_CONTENT, lindex = -1, tymed = NativeMethods.TYMED_HGLOBAL };
      formats[1] = new() { cfFormat = Win32Ole.ContentsFormat, dwAspect = NativeMethods.DVASPECT_CONTENT, lindex = -1, tymed = NativeMethods.TYMED_ISTREAM | NativeMethods.TYMED_HGLOBAL };
      var i = 2;
      foreach (var stored in data._stored.Keys)
        formats[i++] = new() { cfFormat = stored, dwAspect = NativeMethods.DVASPECT_CONTENT, lindex = -1, tymed = NativeMethods.TYMED_HGLOBAL };

      fixed (NativeMethods.FORMATETC* list = formats)
        return NativeMethods.SHCreateStdEnumFmtEtc((uint)formats.Length, list, out *enumerator);
    } catch {
      return NativeMethods.E_FAIL;
    }
  }

  [UnmanagedCallersOnly]
  private static int DAdvise(nint self, NativeMethods.FORMATETC* format, uint flags, nint sink, uint* connection) {
    if (connection != null)
      *connection = 0;

    return NativeMethods.OLE_E_ADVISENOTSUPPORTED;
  }

  [UnmanagedCallersOnly]
  private static int DUnadvise(nint self, uint connection) => NativeMethods.OLE_E_ADVISENOTSUPPORTED;

  [UnmanagedCallersOnly]
  private static int EnumDAdvise(nint self, nint* enumerator) {
    if (enumerator != null)
      *enumerator = 0;

    return NativeMethods.OLE_E_ADVISENOTSUPPORTED;
  }
}
