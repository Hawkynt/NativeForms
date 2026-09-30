namespace Hawkynt.NativeForms;

/// <summary>
/// A file whose content is produced on demand rather than read from an existing path — an archive
/// entry extracted as it is read, a mail attachment, a download. Dragged out through
/// <see cref="Control.DoDragDrop(object, DragDropEffects, Action{DragDropEffects})"/> as a
/// <c>VirtualFile[]</c>, and delivered as one in <see cref="DragEventArgs.Data"/> when another
/// application drops files that have no path on disk (PRD §8).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RelativePath"/> is where the entry lands below the drop's destination folder, so it is
/// validated when the entry is created: it uses <c>/</c> between folders (a <c>\</c> is taken as the
/// same separator), and it is not rooted, has no drive, and has no empty, <c>.</c> or <c>..</c>
/// segment — nothing can escape the destination. A tree is carried by listing its folders with
/// <see cref="Directory(string)"/>; folders that are only implied by a file's path are created too.
/// </para>
/// <para>
/// A dropped virtual file pulls its content from the application it came from, and that application
/// only answers while the drop is being delivered: read it inside the <see cref="Control.DragDrop"/>
/// handler, on the UI thread. After the handler has returned <see cref="OpenRead"/> throws
/// <see cref="InvalidOperationException"/>, so copy anything needed later (to disk, or to memory)
/// while the handler runs.
/// </para>
/// </remarks>
public sealed class VirtualFile {
  private readonly Func<Stream>? _openRead;

  /// <summary>Creates a file entry whose content <paramref name="openRead"/> produces.</summary>
  /// <param name="relativePath">
  /// Where the file lands below the destination, <c>/</c>-separated (<c>docs/readme.txt</c>).
  /// </param>
  /// <param name="openRead">
  /// Returns a new stream positioned at the start of the content each time it is called; the caller
  /// disposes it. Called only when the content is actually needed, on the UI thread.
  /// </param>
  /// <param name="length">The content's length in bytes when known; shown as progress by the target.</param>
  /// <param name="lastWriteTimeUtc">The modification time the written file gets, when known.</param>
  /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> or <paramref name="openRead"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="relativePath"/> is empty, rooted, or has an empty, <c>.</c> or <c>..</c> segment.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
  public VirtualFile(string relativePath, Func<Stream> openRead, long? length = null, DateTime? lastWriteTimeUtc = null) {
    ArgumentNullException.ThrowIfNull(openRead);
    if (length is < 0)
      throw new ArgumentOutOfRangeException(nameof(length), length, "A length cannot be negative.");

    this.RelativePath = Normalize(relativePath, allowTrailingSeparator: false);
    _openRead = openRead;
    this.Length = length;
    this.LastWriteTimeUtc = lastWriteTimeUtc is { } time ? ToUtc(time) : null;
  }

  private VirtualFile(string relativePath) {
    this.RelativePath = relativePath;
    this.IsDirectory = true;
  }

  /// <summary>Creates an (empty) folder entry, so a drag can carry a tree.</summary>
  /// <param name="relativePath">The folder below the destination; one trailing separator is allowed.</param>
  /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="relativePath"/> is empty, rooted, or has an empty, <c>.</c> or <c>..</c> segment.</exception>
  public static VirtualFile Directory(string relativePath) => new(Normalize(relativePath, allowTrailingSeparator: true));

  /// <summary>Where the entry lands below the destination: <c>/</c>-separated, never rooted, never escaping it.</summary>
  public string RelativePath { get; }

  /// <summary>Whether this entry is a folder, created by <see cref="Directory(string)"/>.</summary>
  public bool IsDirectory { get; }

  /// <summary>The content's length in bytes, or <see langword="null"/> when unknown (and for folders).</summary>
  public long? Length { get; }

  /// <summary>The modification time in UTC, or <see langword="null"/> when unknown.</summary>
  public DateTime? LastWriteTimeUtc { get; }

  /// <summary>The last segment of <see cref="RelativePath"/>.</summary>
  internal string Name => this.RelativePath[(this.RelativePath.LastIndexOf('/') + 1)..];

  /// <summary>Opens a new stream over the content, positioned at its start; the caller disposes it.</summary>
  /// <exception cref="InvalidOperationException">
  /// The entry is a folder, the content factory returned <see langword="null"/>, or — for a dropped
  /// file — the drop it came with has already been delivered.
  /// </exception>
  public Stream OpenRead() {
    if (_openRead is null)
      throw new InvalidOperationException($"'{this.RelativePath}' is a folder and has no content.");

    return _openRead() ?? throw new InvalidOperationException($"The content factory of '{this.RelativePath}' returned no stream.");
  }

  /// <inheritdoc/>
  public override string ToString() => this.IsDirectory ? this.RelativePath + "/" : this.RelativePath;

  private static DateTime ToUtc(DateTime time) => time.Kind switch {
    DateTimeKind.Local => time.ToUniversalTime(),
    DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    _ => time,
  };

  /// <summary>Validates <paramref name="path"/> and returns it with <c>/</c> separators.</summary>
  private static string Normalize(string path, bool allowTrailingSeparator) {
    ArgumentNullException.ThrowIfNull(path);

    var normalized = path.Replace('\\', '/');
    if (allowTrailingSeparator && normalized.Length > 1 && normalized[^1] == '/')
      normalized = normalized[..^1];

    if (normalized.Length == 0)
      throw new ArgumentException("A relative path cannot be empty.", nameof(path));
    if (normalized[0] == '/')
      throw new ArgumentException($"'{path}' is rooted; a virtual file's path is relative to the destination.", nameof(path));
    if (normalized.Length >= 2 && normalized[1] == ':' && char.IsAsciiLetter(normalized[0]))
      throw new ArgumentException($"'{path}' names a drive; a virtual file's path is relative to the destination.", nameof(path));
    if (normalized.Contains('\0'))
      throw new ArgumentException("A relative path cannot contain a NUL character.", nameof(path));

    foreach (var segment in normalized.Split('/'))
      if (segment is "" or "." or "..")
        throw new ArgumentException($"'{path}' has an empty, '.' or '..' segment.", nameof(path));

    return normalized;
  }
}
