using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The payload of an OLE data object dropped onto a window (PRD §8): the paths of <c>CF_HDROP</c> as a
/// <c>string[]</c> when the source offers them — Explorer offers descriptors too, and real paths win —
/// otherwise the entries of <c>CFSTR_FILEDESCRIPTORW</c> as a <c>VirtualFile[]</c> whose content is
/// pulled from <c>CFSTR_FILECONTENTS</c> at the entry's index — what Outlook, browsers and archive
/// managers drag.
/// </summary>
/// <remarks>
/// <para>
/// The data object stays referenced from the moment the drag enters until the drop has been delivered
/// (or the drag left), and the virtual files only read from it in that window: afterwards
/// <see cref="VirtualFile.OpenRead"/> throws, because the source is free to discard its data once the
/// drop returns. A stream opened while the drop is being delivered holds its own reference and stays
/// readable until disposed. Everything happens on the UI thread, where OLE calls the drop target;
/// a stream read from another thread throws rather than making a cross-apartment call.
/// </para>
/// <para>
/// Content arrives as <c>TYMED_ISTREAM</c> (read in place) or <c>TYMED_HGLOBAL</c> (copied out).
/// <c>TYMED_ISTORAGE</c> — how Outlook hands over some <c>.msg</c> mails — is refused with
/// <see cref="NotSupportedException"/>. Entries whose path would escape the drop (rooted, <c>..</c>)
/// are left out rather than delivered.
/// </para>
/// </remarks>
internal sealed unsafe class Win32DroppedData {
  private nint _dataObject;
  private readonly int _thread = Environment.CurrentManagedThreadId;

  private Win32DroppedData(object payload) => this.Payload = payload;

  /// <summary>The translated payload: a <c>string[]</c> or a <c>VirtualFile[]</c>.</summary>
  internal object Payload { get; private set; }

  /// <summary>
  /// Translates <paramref name="dataObject"/>, or answers <see langword="null"/> when it carries neither
  /// paths nor file descriptors.
  /// </summary>
  internal static Win32DroppedData? Read(nint dataObject) {
    if (dataObject == 0)
      return null;

    if (ReadPaths(dataObject) is { Length: > 0 } paths)
      return new Win32DroppedData(paths);

    var descriptors = ReadDescriptors(dataObject);
    if (descriptors is null)
      return null;

    var dropped = new Win32DroppedData(Array.Empty<VirtualFile>());
    var files = CreateFiles(descriptors, dropped.OpenContents);
    if (files.Length == 0)
      return null;

    NativeMethods.AddRef(dataObject);
    dropped._dataObject = dataObject;
    dropped.Payload = files;
    return dropped;
  }

  /// <summary>
  /// The virtual files for parsed descriptors, each reading through <paramref name="open"/> with its
  /// index; an entry whose path would escape the destination is left out.
  /// </summary>
  internal static VirtualFile[] CreateFiles(
      List<(string Path, bool IsDirectory, long? Length, DateTime? Time)> descriptors,
      Func<int, string, long?, Stream> open) {
    var files = new List<VirtualFile>(descriptors.Count);
    for (var i = 0; i < descriptors.Count; ++i) {
      var (path, isDirectory, length, time) = descriptors[i];
      var index = i;
      try {
        files.Add(isDirectory
            ? VirtualFile.Directory(path)
            : new VirtualFile(path, () => open(index, path, length), length, time));
      } catch (ArgumentException) {
        // A path that would escape the destination never reaches the application.
      }
    }

    return [.. files];
  }

  /// <summary>Lets go of the data object; the virtual files stop reading from it.</summary>
  internal void Release() {
    var dataObject = _dataObject;
    _dataObject = 0;
    NativeMethods.Release(dataObject);
  }

  /// <summary>The paths of <c>CF_HDROP</c>, or <see langword="null"/>.</summary>
  private static string[]? ReadPaths(nint dataObject) {
    var format = new NativeMethods.FORMATETC {
      cfFormat = NativeMethods.CF_HDROP,
      dwAspect = NativeMethods.DVASPECT_CONTENT,
      lindex = -1,
      tymed = NativeMethods.TYMED_HGLOBAL,
    };
    NativeMethods.STGMEDIUM medium;
    if (NativeMethods.GetData(dataObject, &format, &medium) < 0)
      return null;

    try {
      return medium.tymed == NativeMethods.TYMED_HGLOBAL ? ReadDropFiles(medium.handle) : null;
    } finally {
      NativeMethods.ReleaseStgMedium(&medium);
    }
  }

  /// <summary>The paths in a <c>DROPFILES</c> block (an <c>HDROP</c>), or <see langword="null"/> when it is malformed.</summary>
  internal static string[]? ReadDropFiles(nint hDrop) {
    var count = NativeMethods.DragQueryFileW(hDrop, uint.MaxValue, null, 0);
    if (count == 0 || count > int.MaxValue)
      return null;

    var files = new string[(int)count];
    for (uint i = 0; i < count; ++i) {
      var length = NativeMethods.DragQueryFileW(hDrop, i, null, 0);
      if (length >= int.MaxValue)
        return null;

      var capacity = checked((int)length + 1);
      var buffer = new char[capacity];
      fixed (char* destination = buffer) {
        var written = NativeMethods.DragQueryFileW(hDrop, i, destination, (uint)capacity);
        files[i] = new string(buffer.AsSpan(0, (int)written));
      }
    }

    return files;
  }

  /// <summary>The entries of <c>CFSTR_FILEDESCRIPTORW</c>, or <see langword="null"/> when it is not offered.</summary>
  private static List<(string Path, bool IsDirectory, long? Length, DateTime? Time)>? ReadDescriptors(nint dataObject) {
    var format = new NativeMethods.FORMATETC {
      cfFormat = Win32Ole.DescriptorFormat,
      dwAspect = NativeMethods.DVASPECT_CONTENT,
      lindex = -1,
      tymed = NativeMethods.TYMED_HGLOBAL,
    };
    NativeMethods.STGMEDIUM medium;
    if (NativeMethods.GetData(dataObject, &format, &medium) < 0)
      return null;

    try {
      if (medium.tymed != NativeMethods.TYMED_HGLOBAL)
        return null;

      var size = (int)Math.Min(NativeMethods.GlobalSize(medium.handle), int.MaxValue);
      var block = NativeMethods.GlobalLock(medium.handle);
      if (block == 0)
        return null;

      try {
        return ParseDescriptors(new ReadOnlySpan<byte>((void*)block, size));
      } finally {
        NativeMethods.GlobalUnlock(medium.handle);
      }
    } finally {
      NativeMethods.ReleaseStgMedium(&medium);
    }
  }

  /// <summary>
  /// Parses a <c>FILEGROUPDESCRIPTORW</c>; a count the block cannot hold is cut to what it holds. A name
  /// runs to its terminator, and <c>\</c> separators become <c>/</c>.
  /// </summary>
  internal static List<(string Path, bool IsDirectory, long? Length, DateTime? Time)> ParseDescriptors(ReadOnlySpan<byte> block) {
    var result = new List<(string, bool, long?, DateTime?)>();
    if (block.Length < 4)
      return result;

    var count = BinaryPrimitives.ReadUInt32LittleEndian(block);
    var available = (block.Length - 4) / NativeMethods.FileDescriptorSize;
    count = Math.Min(count, (uint)available);
    for (var i = 0; i < (int)count; ++i) {
      var descriptor = block.Slice(4 + i * NativeMethods.FileDescriptorSize, NativeMethods.FileDescriptorSize);
      var flags = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
      var name = MemoryMarshal.Cast<byte, char>(descriptor.Slice(72, NativeMethods.MAX_PATH * sizeof(char)));
      var end = name.IndexOf('\0');
      var path = new string(end < 0 ? name : name[..end]).Replace('\\', '/');

      var isDirectory = (flags & NativeMethods.FD_ATTRIBUTES) != 0
          && (BinaryPrimitives.ReadUInt32LittleEndian(descriptor[36..]) & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;

      long? length = null;
      if ((flags & NativeMethods.FD_FILESIZE) != 0) {
        var high = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(descriptor[64..]);
        var low = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(descriptor[68..]);
        var size = (high << 32) | low;
        if (size <= long.MaxValue)
          length = (long)size;
      }

      DateTime? time = null;
      if ((flags & NativeMethods.FD_WRITESTIME) != 0) {
        var ticks = (long)BinaryPrimitives.ReadUInt64LittleEndian(descriptor[56..]);
        if (ticks >= 0 && ticks <= DateTime.MaxValue.ToFileTimeUtc())
          time = DateTime.FromFileTimeUtc(ticks);
      }

      result.Add((path, isDirectory, length, time));
    }

    return result;
  }

  /// <summary>Opens the content of entry <paramref name="index"/> from the source.</summary>
  private Stream OpenContents(int index, string path, long? length) {
    if (_dataObject == 0)
      throw new InvalidOperationException(
          $"The drop that carried '{path}' has been delivered; read a dropped file's content inside the DragDrop handler.");

    if (Environment.CurrentManagedThreadId != _thread)
      throw new InvalidOperationException($"The content of the dropped '{path}' can only be read on the UI thread.");

    var format = new NativeMethods.FORMATETC {
      cfFormat = Win32Ole.ContentsFormat,
      dwAspect = NativeMethods.DVASPECT_CONTENT,
      lindex = index,
      tymed = NativeMethods.TYMED_ISTREAM | NativeMethods.TYMED_HGLOBAL | NativeMethods.TYMED_ISTORAGE,
    };
    NativeMethods.STGMEDIUM medium;
    var result = NativeMethods.GetData(_dataObject, &format, &medium);
    if (result < 0)
      throw new IOException($"The drop source did not hand over the content of '{path}' (0x{result:X8}).");

    switch (medium.tymed) {
      case NativeMethods.TYMED_ISTREAM when medium.handle != 0:
        return new Win32ComStreamReader(medium, _thread);

      case NativeMethods.TYMED_HGLOBAL when medium.handle != 0:
        try {
          var size = (long)NativeMethods.GlobalSize(medium.handle);
          if (length is { } known && known < size)
            size = known; // the block may be rounded up past the content

          var block = NativeMethods.GlobalLock(medium.handle);
          if (block == 0)
            throw new IOException($"The content of '{path}' could not be read.");

          try {
            return new MemoryStream(new ReadOnlySpan<byte>((void*)block, checked((int)size)).ToArray(), writable: false);
          } finally {
            NativeMethods.GlobalUnlock(medium.handle);
          }
        } finally {
          NativeMethods.ReleaseStgMedium(&medium);
        }

      case NativeMethods.TYMED_ISTORAGE:
        NativeMethods.ReleaseStgMedium(&medium);
        throw new NotSupportedException(
            $"The drop source hands over '{path}' as structured storage (as Outlook does for mails), which is not supported.");

      default:
        NativeMethods.ReleaseStgMedium(&medium);
        throw new IOException($"The drop source handed over the content of '{path}' in an unsupported medium ({medium.tymed}).");
    }
  }
}
