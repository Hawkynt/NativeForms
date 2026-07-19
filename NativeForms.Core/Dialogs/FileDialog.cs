using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// Shared options of the native open/save dialogs: the file name, the type filter in WinForms
/// syntax, the start directory and the caption.
/// </summary>
public abstract class FileDialog : CommonDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    private protected FileDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    private protected FileDialog(IPlatformBackend backend) : base(backend) { }

    /// <summary>The selected file's absolute path after OK; pre-fills the dialog's name box before.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The type filter in WinForms syntax — display texts and glob patterns alternating around
    /// <c>'|'</c>, for example <c>"Text files|*.txt|All files|*.*"</c>. Empty shows every file.
    /// </summary>
    /// <exception cref="ArgumentException">The value has an odd number of <c>'|'</c>-separated segments.</exception>
    public string Filter
    {
        get => field;
        set
        {
            value ??= string.Empty;
            ParseFilter(value);
            field = value;
        }
    } = string.Empty;

    /// <summary>The 1-based index of the initially selected <see cref="Filter"/> entry, as in WinForms.</summary>
    public int FilterIndex { get; set; } = 1;

    /// <summary>The directory the dialog starts in; empty picks the platform default.</summary>
    public string InitialDirectory { get; set; } = string.Empty;

    /// <summary>The title-bar caption; empty picks the platform default.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The dialog kind the backend should present.</summary>
    private protected abstract FileDialogKind Kind { get; }

    /// <summary>Whether several files may be picked at once (open dialogs only).</summary>
    private protected virtual bool SelectsMultiple => false;

    /// <summary>Stores the user's choice once the backend reports success.</summary>
    private protected virtual void Accept(string[] paths) => this.FileName = paths[0];

    /// <inheritdoc/>
    private protected sealed override DialogResult RunDialog(IPlatformBackend backend)
    {
        var options = new FileDialogOptions
        {
            Kind = this.Kind,
            Title = this.Title,
            FileName = this.FileName,
            InitialDirectory = this.InitialDirectory,
            Filters = ParseFilter(this.Filter),
            FilterIndex = this.FilterIndex,
            Multiselect = this.SelectsMultiple,
        };

        var paths = backend.ShowFileDialog(in options);
        if (paths is not { Length: > 0 })
            return DialogResult.Cancel;

        this.Accept(paths);
        return DialogResult.OK;
    }

    /// <summary>
    /// Splits a WinForms filter string into name/pattern pairs, validating that the segments pair up.
    /// </summary>
    internal static FileDialogFilter[] ParseFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return [];

        var segments = filter.Split('|');
        if ((segments.Length & 1) != 0)
            throw new ArgumentException(
                $"Invalid filter \"{filter}\": expected pairs of display text and patterns separated by '|', for example \"Text files|*.txt|All files|*.*\".",
                nameof(filter));

        var result = new FileDialogFilter[segments.Length / 2];
        for (var i = 0; i < result.Length; ++i)
            result[i] = new(segments[i * 2], segments[i * 2 + 1]);

        return result;
    }
}

/// <summary>
/// The platform's native "Open file" dialog. Set the options, call
/// <see cref="CommonDialog.ShowDialog"/>, and read <see cref="FileDialog.FileName"/> (or
/// <see cref="FileNames"/> with <see cref="Multiselect"/>) after <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class OpenFileDialog : FileDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    public OpenFileDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    internal OpenFileDialog(IPlatformBackend backend) : base(backend) { }

    /// <summary>Whether the user may pick several files at once; they arrive in <see cref="FileNames"/>.</summary>
    public bool Multiselect { get; set; }

    /// <summary>All selected absolute paths after OK — one element unless <see cref="Multiselect"/>.</summary>
    public string[] FileNames { get; private set; } = [];

    /// <inheritdoc/>
    private protected override FileDialogKind Kind => FileDialogKind.Open;

    /// <inheritdoc/>
    private protected override bool SelectsMultiple => this.Multiselect;

    /// <inheritdoc/>
    private protected override void Accept(string[] paths)
    {
        base.Accept(paths);
        this.FileNames = paths;
    }
}

/// <summary>
/// The platform's native "Save file" dialog (prompting before overwriting an existing file). Set the
/// options, call <see cref="CommonDialog.ShowDialog"/>, and read <see cref="FileDialog.FileName"/>
/// after <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class SaveFileDialog : FileDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    public SaveFileDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    internal SaveFileDialog(IPlatformBackend backend) : base(backend) { }

    /// <inheritdoc/>
    private protected override FileDialogKind Kind => FileDialogKind.Save;
}
