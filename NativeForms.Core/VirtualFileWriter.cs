namespace Hawkynt.NativeForms;

/// <summary>
/// Writes <see cref="VirtualFile"/> entries onto disk (PRD §8): into a destination a drop target
/// named (the GTK direct-save and macOS file-promise drags), or into a private temporary folder when
/// a backend can only drag existing paths.
/// </summary>
/// <remarks>
/// <para>
/// Into a destination, every top-level entry is first written under a hidden temporary name in the
/// destination folder itself — <c>.&lt;name&gt;.&lt;random&gt;.partial</c>, a folder together with
/// everything below it — and renamed to its real name only once it is complete. An interrupted drop
/// therefore never leaves a truncated file under the real name, and nothing is staged on another
/// volume. The rename never replaces anything: an existing name gets the next free
/// <c>name (2).ext</c>, <c>name (3).ext</c>, … instead.
/// </para>
/// <para>
/// Content is read on the calling thread, which the backends keep on the UI thread, where the
/// application's streams expect to be read.
/// </para>
/// </remarks>
internal static class VirtualFileWriter {
  /// <summary>The highest <c>name (n)</c> tried before giving up.</summary>
  private const int _MaxNumber = 10000;

  /// <summary>Temporary folders <see cref="Materialize"/> created; removed when the process exits.</summary>
  private static readonly List<string> _materialized = [];

  /// <summary>The first segment of every entry, in the order they first appear, each once.</summary>
  internal static List<string> TopLevelNames(IReadOnlyList<VirtualFile> files) {
    var names = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in files) {
      var path = file.RelativePath;
      var slash = path.IndexOf('/');
      var top = slash < 0 ? path : path[..slash];
      if (seen.Add(top))
        names.Add(top);
    }

    return names;
  }

  /// <summary>
  /// Whether the top-level entry <paramref name="topName"/> is a folder: listed as one, or implied by
  /// an entry below it.
  /// </summary>
  internal static bool IsFolder(IReadOnlyList<VirtualFile> files, string topName) {
    foreach (var file in files)
      if (file.RelativePath == topName ? file.IsDirectory : IsBelow(file.RelativePath, topName))
        return true;

    return false;
  }

  /// <summary>
  /// Writes every entry below <paramref name="directory"/>, one top-level entry at a time through
  /// <see cref="WriteEntry"/>, and returns the paths the top-level entries got.
  /// </summary>
  internal static string[] WriteTree(string directory, IReadOnlyList<VirtualFile> files) {
    var names = TopLevelNames(files);
    var written = new string[names.Count];
    for (var i = 0; i < names.Count; ++i)
      written[i] = WriteEntry(Path.Combine(directory, names[i]), files, names[i]);

    return written;
  }

  /// <summary>
  /// Writes the top-level entry <paramref name="topName"/> of <paramref name="files"/> — one file, or
  /// a folder with every entry below it — to <paramref name="finalPath"/>, staged under a temporary
  /// name beside it and renamed once complete.
  /// </summary>
  /// <returns>The path the entry got: <paramref name="finalPath"/>, or a numbered variant when that was taken.</returns>
  /// <exception cref="InvalidOperationException"><paramref name="topName"/> is both a file and a folder in <paramref name="files"/>.</exception>
  internal static string WriteEntry(string finalPath, IReadOnlyList<VirtualFile> files, string topName) {
    var directory = Path.GetDirectoryName(finalPath) ?? throw new ArgumentException($"'{finalPath}' has no parent folder.", nameof(finalPath));
    var staging = Path.Combine(directory, TemporaryName(Path.GetFileName(finalPath)));
    var folder = IsFolder(files, topName);
    try {
      if (folder) {
        System.IO.Directory.CreateDirectory(staging);
        WriteBelow(staging, files, topName);
      } else {
        foreach (var file in files)
          if (file.RelativePath == topName) {
            WriteContent(file, staging);
            break;
          }
      }

      return MoveToFreeName(staging, finalPath, folder);
    } catch {
      TryDelete(staging);
      throw;
    }
  }

  /// <summary>
  /// Writes <paramref name="files"/> into a new private temporary folder, for a backend that can only
  /// drag existing paths, and returns the paths of the top-level entries. The folder stays until the
  /// process exits, because the drop target reads the files after the drag has ended.
  /// </summary>
  internal static string[] Materialize(IReadOnlyList<VirtualFile> files) {
    var folder = Path.Combine(Path.GetTempPath(), "NativeForms-drag-" + Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(folder);
    try {
      var names = TopLevelNames(files);
      var paths = new string[names.Count];
      WriteBelow(folder, files, null);
      for (var i = 0; i < names.Count; ++i)
        paths[i] = Path.Combine(folder, names[i]);

      lock (_materialized) {
        if (_materialized.Count == 0)
          AppDomain.CurrentDomain.ProcessExit += static (_, _) => DeleteMaterialized();

        _materialized.Add(folder);
      }

      return paths;
    } catch {
      TryDelete(folder);
      throw;
    }
  }

  /// <summary>
  /// Writes every entry below <paramref name="topName"/> (every entry when it is null) into
  /// <paramref name="root"/>, a folder of ours that nothing else sees yet, so entries are written in
  /// place; a path listed twice gets a numbered name rather than replacing the first.
  /// </summary>
  private static void WriteBelow(string root, IReadOnlyList<VirtualFile> files, string? topName) {
    foreach (var file in files) {
      string relative;
      if (topName is null)
        relative = file.RelativePath;
      else if (IsBelow(file.RelativePath, topName))
        relative = file.RelativePath[(topName.Length + 1)..];
      else if (file.RelativePath == topName && file.IsDirectory)
        continue;
      else if (file.RelativePath == topName)
        throw new InvalidOperationException($"'{topName}' is both a file and a folder.");
      else
        continue;

      var target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
      if (file.IsDirectory) {
        System.IO.Directory.CreateDirectory(target);
        continue;
      }

      System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      for (var number = 2; File.Exists(target) || System.IO.Directory.Exists(target); ++number) {
        if (number > _MaxNumber)
          throw new IOException($"No free name for '{relative}'.");

        target = Numbered(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), number, folder: false);
      }

      WriteContent(file, target);
    }
  }

  /// <summary>Copies <paramref name="file"/>'s content into a new file at <paramref name="path"/>.</summary>
  private static void WriteContent(VirtualFile file, string path) {
    using (var source = file.OpenRead())
    using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
      source.CopyTo(target);

    if (file.LastWriteTimeUtc is { } time)
      File.SetLastWriteTimeUtc(path, time);
  }

  /// <summary>
  /// Renames the staged <paramref name="staging"/> to <paramref name="finalPath"/>, or to the first
  /// free numbered variant of it; never replaces an existing file or folder.
  /// </summary>
  private static string MoveToFreeName(string staging, string finalPath, bool folder) {
    for (var number = 1; number <= _MaxNumber; ++number) {
      var candidate = number == 1 ? finalPath : Numbered(finalPath, number, folder);
      if (File.Exists(candidate) || System.IO.Directory.Exists(candidate))
        continue;

      try {
        if (folder)
          System.IO.Directory.Move(staging, candidate);
        else
          File.Move(staging, candidate, overwrite: false); // fails rather than replaces: a race loses cleanly

        return candidate;
      } catch (IOException) when (File.Exists(candidate) || System.IO.Directory.Exists(candidate)) {
        // Taken between the check and the rename; try the next number.
      }
    }

    throw new IOException($"No free name for '{finalPath}'.");
  }

  /// <summary><c>name (n).ext</c> for a file, <c>name (n)</c> for a folder or a name that is all extension.</summary>
  internal static string Numbered(string path, int number, bool folder) {
    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var name = Path.GetFileName(path);
    var extension = folder ? string.Empty : Path.GetExtension(name);
    var stem = name[..^extension.Length];
    if (stem.Length == 0) {
      stem = name;
      extension = string.Empty;
    }

    return Path.Combine(directory, $"{stem} ({number}){extension}");
  }

  /// <summary><c>.&lt;name&gt;.&lt;random&gt;.partial</c>: hidden on Unix, recognizable anywhere, unique.</summary>
  internal static string TemporaryName(string name) {
    const int keep = 200; // leaves room for the decoration within a 255-character name
    if (name.Length > keep)
      name = name[..(char.IsHighSurrogate(name[keep - 1]) ? keep - 1 : keep)];

    return $".{name}.{Guid.NewGuid().ToString("N")[..8]}.partial";
  }

  private static bool IsBelow(string path, string topName)
      => path.Length > topName.Length + 1 && path[topName.Length] == '/' && path.StartsWith(topName, StringComparison.Ordinal);

  private static void TryDelete(string path) {
    try {
      if (System.IO.Directory.Exists(path))
        System.IO.Directory.Delete(path, recursive: true);
      else if (File.Exists(path))
        File.Delete(path);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  private static void DeleteMaterialized() {
    lock (_materialized)
      foreach (var folder in _materialized)
        TryDelete(folder);
  }
}
