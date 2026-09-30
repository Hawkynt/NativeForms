using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// A read-only <c>IStream</c> over a managed <see cref="Stream"/> (PRD §8): the <c>CFSTR_FILECONTENTS</c>
/// medium of a virtual file dragged out, which Explorer reads straight into the file it creates at the
/// destination.
/// </summary>
/// <remarks>
/// <c>Read</c>, <c>Seek</c> (any position on a seekable stream; on a forward-only one only asking where
/// it is) and <c>Stat</c> with the size are implemented; <c>Write</c> answers
/// <c>STG_E_ACCESSDENIED</c> and the rest <c>E_NOTIMPL</c>, which every consumer falls back from. The
/// managed stream is disposed with the last reference. A failure while producing the content is
/// answered with <c>STG_E_READFAULT</c>, which Explorer reports as a failed copy of that file.
/// </remarks>
internal sealed unsafe class Win32ComStream : IWin32ComCallable, IDisposable {
  private static readonly void** _vtable = CreateVtable();

  private readonly Stream _stream;
  private readonly string _name;
  private readonly long? _length;
  private readonly DateTime? _lastWriteTimeUtc;

  /// <summary>How far a forward-only stream has been read, so it can answer where it is.</summary>
  private long _position;

  private Win32ComStream(Stream stream, string name, long? length, DateTime? lastWriteTimeUtc) {
    _stream = stream;
    _name = name;
    _length = length;
    _lastWriteTimeUtc = lastWriteTimeUtc;
  }

  /// <summary>A new <c>IStream</c> over <paramref name="stream"/> with one reference, owned by the caller; it owns the stream.</summary>
  internal static nint Create(Stream stream, string name, long? length = null, DateTime? lastWriteTimeUtc = null)
      => Win32ComObject.Create(_vtable, new Win32ComStream(stream, name, length, lastWriteTimeUtc));

  /// <inheritdoc/>
  public bool Supports(in Guid iid) => iid == NativeMethods.IID_IStream || iid == NativeMethods.IID_ISequentialStream;

  /// <inheritdoc/>
  public void Dispose() => _stream.Dispose();

  private static void** CreateVtable() {
    var vtable = Win32ComObject.CreateVtable(14);
    vtable[3] = (delegate* unmanaged<nint, byte*, uint, uint*, int>)&Read;
    vtable[4] = (delegate* unmanaged<nint, byte*, uint, uint*, int>)&Write;
    vtable[5] = (delegate* unmanaged<nint, long, uint, ulong*, int>)&Seek;
    vtable[6] = (delegate* unmanaged<nint, ulong, int>)&SetSize;
    vtable[7] = (delegate* unmanaged<nint, nint, ulong, ulong*, ulong*, int>)&CopyTo;
    vtable[8] = (delegate* unmanaged<nint, uint, int>)&Commit;
    vtable[9] = (delegate* unmanaged<nint, int>)&Revert;
    vtable[10] = (delegate* unmanaged<nint, ulong, ulong, uint, int>)&LockRegion;
    vtable[11] = (delegate* unmanaged<nint, ulong, ulong, uint, int>)&LockRegion; // UnlockRegion: same shape, same answer
    vtable[12] = (delegate* unmanaged<nint, NativeMethods.STATSTG*, uint, int>)&Stat;
    vtable[13] = (delegate* unmanaged<nint, nint*, int>)&Clone;
    return vtable;
  }

  /// <summary><c>ISequentialStream::Read</c>: fills the buffer as far as the content goes; fewer bytes means the end.</summary>
  [UnmanagedCallersOnly]
  private static int Read(nint self, byte* buffer, uint count, uint* read) {
    if (read != null)
      *read = 0;

    if (buffer == null && count != 0)
      return NativeMethods.STG_E_INVALIDPOINTER;

    if (Win32ComObject.Target<Win32ComStream>(self) is not { } stream)
      return NativeMethods.E_UNEXPECTED;

    try {
      var span = new Span<byte>(buffer, (int)Math.Min(count, int.MaxValue));
      var total = 0;
      while (total < span.Length) {
        var got = stream._stream.Read(span[total..]);
        if (got == 0)
          break;

        total += got;
      }

      stream._position += total;
      if (read != null)
        *read = (uint)total;

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.STG_E_READFAULT;
    }
  }

  [UnmanagedCallersOnly]
  private static int Write(nint self, byte* buffer, uint count, uint* written) {
    if (written != null)
      *written = 0;

    return NativeMethods.STG_E_ACCESSDENIED;
  }

  /// <summary><c>IStream::Seek</c>: anywhere on a seekable stream; a forward-only one can only say where it is.</summary>
  [UnmanagedCallersOnly]
  private static int Seek(nint self, long move, uint origin, ulong* position) {
    if (Win32ComObject.Target<Win32ComStream>(self) is not { } stream)
      return NativeMethods.E_UNEXPECTED;

    try {
      long at;
      if (stream._stream.CanSeek) {
        at = stream._stream.Seek(move, origin switch {
          NativeMethods.STREAM_SEEK_SET => SeekOrigin.Begin,
          NativeMethods.STREAM_SEEK_CUR => SeekOrigin.Current,
          NativeMethods.STREAM_SEEK_END => SeekOrigin.End,
          _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        });
        stream._position = at;
      } else if ((origin == NativeMethods.STREAM_SEEK_CUR && move == 0)
                 || (origin == NativeMethods.STREAM_SEEK_SET && move == stream._position)) {
        at = stream._position;
      } else {
        return NativeMethods.STG_E_INVALIDFUNCTION;
      }

      if (position != null)
        *position = (ulong)at;

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.STG_E_INVALIDFUNCTION;
    }
  }

  [UnmanagedCallersOnly]
  private static int SetSize(nint self, ulong size) => NativeMethods.E_NOTIMPL;

  [UnmanagedCallersOnly]
  private static int CopyTo(nint self, nint target, ulong count, ulong* read, ulong* written) => NativeMethods.E_NOTIMPL;

  [UnmanagedCallersOnly]
  private static int Commit(nint self, uint flags) => NativeMethods.E_NOTIMPL;

  [UnmanagedCallersOnly]
  private static int Revert(nint self) => NativeMethods.E_NOTIMPL;

  [UnmanagedCallersOnly]
  private static int LockRegion(nint self, ulong offset, ulong count, uint type) => NativeMethods.E_NOTIMPL;

  /// <summary><c>IStream::Stat</c>: a stream, its size when known, its time, and its name unless declined.</summary>
  [UnmanagedCallersOnly]
  private static int Stat(nint self, NativeMethods.STATSTG* stat, uint flags) {
    if (stat == null)
      return NativeMethods.STG_E_INVALIDPOINTER;

    *stat = default;
    if (Win32ComObject.Target<Win32ComStream>(self) is not { } stream)
      return NativeMethods.E_UNEXPECTED;

    try {
      stat->type = NativeMethods.STGTY_STREAM;
      stat->cbSize = (ulong)(stream._length ?? (stream._stream.CanSeek ? stream._stream.Length : 0));
      if (stream._lastWriteTimeUtc is { } time)
        stat->mtime = ToFileTime(time);

      if ((flags & NativeMethods.STATFLAG_NONAME) == 0)
        stat->pwcsName = AllocateName(stream._name);

      return NativeMethods.S_OK;
    } catch {
      return NativeMethods.E_FAIL;
    }
  }

  [UnmanagedCallersOnly]
  private static int Clone(nint self, nint* clone) {
    if (clone != null)
      *clone = 0;

    return NativeMethods.E_NOTIMPL;
  }

  /// <summary>A <c>FILETIME</c> for <paramref name="utc"/>; times before 1601 clamp to its start.</summary>
  internal static NativeMethods.FILETIME ToFileTime(DateTime utc) {
    var ticks = utc < DateTime.FromFileTimeUtc(0) ? 0 : utc.ToFileTimeUtc();
    return new() { dwLowDateTime = (uint)ticks, dwHighDateTime = (uint)(ticks >> 32) };
  }

  /// <summary>A zero-terminated UTF-16 copy of <paramref name="name"/> in task memory, which the caller frees.</summary>
  private static nint AllocateName(string name) {
    var bytes = (nuint)(name.Length + 1) * sizeof(char);
    var memory = NativeMethods.CoTaskMemAlloc(bytes);
    if (memory == 0)
      return 0;

    var target = new Span<char>((void*)memory, name.Length + 1);
    name.AsSpan().CopyTo(target);
    target[^1] = '\0';
    return memory;
  }
}
