using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A file field: a hosted native <see cref="TextBox"/> holding the path plus a browse button that
/// opens the platform's own <see cref="OpenFileDialog"/> or <see cref="SaveFileDialog"/> — chosen by
/// <see cref="Mode"/> — and writes the choice back into <see cref="PathPickerBase.SelectedPath"/>.
/// </summary>
/// <remarks>
/// <see cref="Filter"/>, <see cref="FilterIndex"/> and <see cref="Multiselect"/> are handed straight
/// to the dialog in WinForms syntax, so a filter that works with <see cref="OpenFileDialog"/> works
/// here unchanged. The dialog object is built per browse rather than held: a picker that is never
/// clicked costs nothing beyond its own fields (§4).
/// <para>
/// The committed <see cref="PathPickerBase.SelectedPath"/> is a <em>file</em>, never a directory. In
/// <see cref="FilePickerMode.Open"/> a typed or returned path that resolves to an existing directory
/// is refused — the earlier value stands and the editor snaps back to it — the way WinForms navigates
/// into a folder rather than returning it. Use <see cref="FolderPicker"/> when a directory is the
/// wanted value.
/// </para>
/// </remarks>
public class FilePicker : PathPickerBase
{
    private string[] _selectedPaths = [];

    /// <summary>The selection the dialog just produced, awaiting the commit that publishes it.</summary>
    private string[]? _pendingSelection;

    /// <summary>Which dialog the browse button opens. Defaults to <see cref="FilePickerMode.Open"/>.</summary>
    public FilePickerMode Mode { get; set; }

    /// <summary>
    /// The type filter in WinForms syntax — display texts and glob patterns alternating around
    /// <c>'|'</c>, for example <c>"Text files|*.txt|All files|*.*"</c>. Empty shows every file.
    /// Validated on assignment exactly as <see cref="FileDialog.Filter"/> validates it.
    /// </summary>
    /// <exception cref="ArgumentException">The value has an odd number of <c>'|'</c>-separated segments.</exception>
    public string Filter
    {
        get => field;
        set
        {
            value ??= string.Empty;
            FileDialog.ParseFilter(value);
            field = value;
        }
    } = string.Empty;

    /// <summary>The 1-based index of the initially selected <see cref="Filter"/> entry, as in WinForms.</summary>
    public int FilterIndex { get; set; } = 1;

    /// <summary>The directory the dialog starts in; empty starts at the committed path's own folder.</summary>
    public string InitialDirectory { get; set; } = string.Empty;

    /// <summary>The browse dialog's title-bar caption; empty picks the platform default.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the browse dialog lets the user pick several files at once
    /// (<see cref="FilePickerMode.Open"/> only). The full set lands in <see cref="SelectedPaths"/>
    /// while <see cref="PathPickerBase.SelectedPath"/> keeps the first.
    /// </summary>
    public bool Multiselect { get; set; }

    /// <summary>
    /// Every path the current selection covers: the whole set after a multi-pick, a single element
    /// after any other commit, and empty while the field is. Never <see langword="null"/>.
    /// </summary>
    public string[] SelectedPaths => _selectedPaths;

    /// <inheritdoc/>
    private protected override string? Browse(IPlatformBackend backend)
    {
        // A cancelled browse must not leave an earlier pick pending for the next commit to adopt.
        _pendingSelection = null;

        var start = this.InitialDirectory.Length > 0 ? this.InitialDirectory : DirectoryOf(this.SelectedPath);
        if (this.Mode == FilePickerMode.Save)
        {
            var save = new SaveFileDialog(backend)
            {
                FileName = this.SelectedPath,
                Filter = this.Filter,
                FilterIndex = this.FilterIndex,
                InitialDirectory = start,
                Title = this.Title,
            };

            if (save.ShowDialog() != DialogResult.OK)
                return null;

            _pendingSelection = [save.FileName];
            return save.FileName;
        }

        var open = new OpenFileDialog(backend)
        {
            FileName = this.SelectedPath,
            Filter = this.Filter,
            FilterIndex = this.FilterIndex,
            InitialDirectory = start,
            Title = this.Title,
            Multiselect = this.Multiselect,
        };

        if (open.ShowDialog() != DialogResult.OK)
            return null;

        _pendingSelection = open.FileNames;
        return open.FileName;
    }

    /// <summary>
    /// Publishes the dialog's full selection after the commit. Runs even when the pick equalled the
    /// committed path — that commit is a no-op and raises nothing, yet the set behind it may still
    /// have widened from one file to several.
    /// </summary>
    private protected override void AfterBrowse()
    {
        if (_pendingSelection is not { } selection)
            return;

        _selectedPaths = selection;
        _pendingSelection = null;
    }

    /// <summary>
    /// What "this path is real" means depends on the mode. Opening asks for a file that is there.
    /// Saving asks for a file that is <em>not</em> there yet — naming one that does not exist is the
    /// entire point — so the meaningful check is whether the folder it would be written into exists.
    /// A bare file name carries no folder and is resolved against the working directory, so it is
    /// accepted rather than flagged.
    /// </summary>
    private protected override bool Exists(string path)
    {
        if (this.Mode != FilePickerMode.Save)
            return File.Exists(path);

        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) || Directory.Exists(directory);
    }

    /// <summary>
    /// A directory is not a file selection. In <see cref="FilePickerMode.Open"/> a path that resolves
    /// to an existing directory is refused outright — it never becomes <see cref="PathPickerBase.SelectedPath"/>,
    /// the previously committed value stands, and the editor snaps back to it — exactly as WinForms'
    /// <see cref="OpenFileDialog"/> navigates into a typed folder instead of returning it. A missing
    /// file path is <em>not</em> vetoed: it commits and is flagged as broken through
    /// <see cref="PathPickerBase.PathExists"/>, because it may be a typo the user wants to see and fix,
    /// or a file about to appear. In <see cref="FilePickerMode.Save"/> nothing is vetoed here — naming
    /// a not-yet-written file, even one whose name happens to match no directory, is the whole point.
    /// </summary>
    private protected override bool CanCommit(string path)
        => this.Mode != FilePickerMode.Open || !Directory.Exists(path);

    /// <summary>
    /// Narrows the selection to the committed path. A path that arrived by typing is its own whole
    /// selection, so <see cref="SelectedPaths"/> never reports a stale multi-pick; a path that
    /// arrived from the dialog is widened again by <see cref="AfterBrowse"/> right after.
    /// </summary>
    protected override void OnPathChanged(EventArgs e)
    {
        var path = this.SelectedPath;
        _selectedPaths = path.Length == 0 ? [] : [path];
        base.OnPathChanged(e);
    }

    /// <summary>The folder part of a path, or empty when there is none to derive.</summary>
    private static string DirectoryOf(string path)
        => path.Length == 0 ? string.Empty : Path.GetDirectoryName(path) ?? string.Empty;
}
