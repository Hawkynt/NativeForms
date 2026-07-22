using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A folder field: a hosted native <see cref="TextBox"/> holding the directory plus a browse button
/// that opens the platform's own <see cref="FolderBrowserDialog"/> and writes the choice back into
/// <see cref="PathPickerBase.SelectedPath"/>.
/// </summary>
/// <remarks>
/// The committed path seeds the dialog's start location, so browsing twice resumes where the user
/// left off. <see cref="PathPickerBase.PathExists"/> asks about a directory here, not a file, and —
/// the mirror of <see cref="FilePicker"/> — the committed <see cref="PathPickerBase.SelectedPath"/>
/// is a <em>directory</em>: a folder is exactly what this picker stands behind, so it is accepted,
/// never refused. Use <see cref="FilePicker"/> when a file is the wanted value.
/// </remarks>
public class FolderPicker : PathPickerBase
{
    /// <summary>The browse dialog's title-bar caption; empty picks the platform default.</summary>
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc/>
    private protected override string? Browse(IPlatformBackend backend)
    {
        var dialog = new FolderBrowserDialog(backend)
        {
            SelectedPath = this.SelectedPath,
            Title = this.Title,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <inheritdoc/>
    private protected override bool Exists(string path) => Directory.Exists(path);
}
