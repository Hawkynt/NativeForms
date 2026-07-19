namespace Hawkynt.NativeForms.Backends;

/// <summary>Which native file dialog <see cref="IPlatformBackend.ShowFileDialog"/> presents.</summary>
public enum FileDialogKind
{
    /// <summary>Pick one or more existing files to open.</summary>
    Open,

    /// <summary>Pick a target path to save to (prompting before overwriting).</summary>
    Save,

    /// <summary>Pick a directory.</summary>
    SelectFolder,
}

/// <summary>
/// One entry of a file dialog's type drop-down: a display name plus its glob patterns —
/// the parsed form of one <c>"Text files|*.txt"</c> pair from the WinForms filter syntax.
/// </summary>
public readonly struct FileDialogFilter(string name, string patterns)
{
    /// <summary>The human-readable entry name (for example <c>"Text files"</c>).</summary>
    public string Name { get; } = name;

    /// <summary>The glob patterns, <c>';'</c>-separated (for example <c>"*.txt;*.log"</c>).</summary>
    public string Patterns { get; } = patterns;
}

/// <summary>
/// Everything a backend needs to show a native file, save or folder dialog — the marshalled form of
/// <see cref="OpenFileDialog"/>, <see cref="SaveFileDialog"/> and <see cref="FolderBrowserDialog"/>.
/// Passed by <c>in</c> reference through <see cref="IPlatformBackend.ShowFileDialog"/>.
/// </summary>
public readonly struct FileDialogOptions
{
    /// <summary>Which dialog to present.</summary>
    public FileDialogKind Kind { get; init; }

    /// <summary>The title-bar caption; empty picks the platform default.</summary>
    public string Title { get; init; }

    /// <summary>The pre-filled file name (save) or initial selection (open); may be empty.</summary>
    public string FileName { get; init; }

    /// <summary>The directory the dialog starts in; empty picks the platform default.</summary>
    public string InitialDirectory { get; init; }

    /// <summary>The parsed filter entries; empty shows every file.</summary>
    public FileDialogFilter[] Filters { get; init; }

    /// <summary>The 1-based index of the initially selected filter entry, as in WinForms.</summary>
    public int FilterIndex { get; init; }

    /// <summary>Whether an open dialog lets the user pick several files at once.</summary>
    public bool Multiselect { get; init; }
}
