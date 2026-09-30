namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// A forward-only, read-only <see cref="Stream"/> over the <c>IStream</c> medium a drop source handed
/// over for one dropped file (PRD §8). It owns the medium and releases it when disposed.
/// </summary>
/// <remarks>
/// The source's stream belongs to the UI thread's apartment — it is usually a proxy into another
/// process — so reading from any other thread throws <see cref="InvalidOperationException"/> instead
/// of making a call COM would reject anyway.
/// </remarks>
internal sealed unsafe class Win32ComStreamReader : Stream {
  private NativeMethods.STGMEDIUM _medium;
  private readonly int _thread;
  private long _position;

  /// <summary>Takes ownership of <paramref name="medium"/>, a <c>TYMED_ISTREAM</c> medium readable on <paramref name="thread"/>.</summary>
  internal Win32ComStreamReader(NativeMethods.STGMEDIUM medium, int thread) {
    _medium = medium;
    _thread = thread;
  }

  /// <inheritdoc/>
  public override bool CanRead => _medium.handle != 0;

  /// <inheritdoc/>
  public override bool CanSeek => false;

  /// <inheritdoc/>
  public override bool CanWrite => false;

  /// <inheritdoc/>
  public override long Length => throw new NotSupportedException();

  /// <inheritdoc/>
  public override long Position {
    get => _position;
    set => throw new NotSupportedException();
  }

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

  /// <inheritdoc/>
  public override int Read(Span<byte> buffer) {
    ObjectDisposedException.ThrowIf(_medium.handle == 0, this);
    if (Environment.CurrentManagedThreadId != _thread)
      throw new InvalidOperationException("A dropped file's content can only be read on the UI thread.");

    if (buffer.IsEmpty)
      return 0;

    uint read;
    int result;
    fixed (byte* target = buffer)
      result = NativeMethods.StreamRead(_medium.handle, target, (uint)buffer.Length, &read);

    if (result < 0)
      throw new IOException($"The drop source failed to deliver the content (0x{result:X8}).");

    _position += read;
    return (int)read;
  }

  /// <inheritdoc/>
  public override void Flush() { }

  /// <inheritdoc/>
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  /// <inheritdoc/>
  public override void SetLength(long value) => throw new NotSupportedException();

  /// <inheritdoc/>
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  /// <inheritdoc/>
  protected override void Dispose(bool disposing) {
    if (_medium.handle != 0) {
      var medium = _medium;
      _medium = default;
      NativeMethods.ReleaseStgMedium(&medium);
    }

    base.Dispose(disposing);
  }
}
