using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// The platform's native "Select folder" dialog. <see cref="SelectedPath"/> seeds the initial
/// location and carries the chosen directory after <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class FolderBrowserDialog : CommonDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    public FolderBrowserDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    internal FolderBrowserDialog(IPlatformBackend backend) : base(backend) { }

    /// <summary>The chosen directory after OK; pre-selects the dialog's start location before.</summary>
    public string SelectedPath { get; set; } = string.Empty;

    /// <summary>The title-bar caption; empty picks the platform default.</summary>
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc/>
    private protected override DialogResult RunDialog(IPlatformBackend backend)
    {
        var options = new FileDialogOptions
        {
            Kind = FileDialogKind.SelectFolder,
            Title = this.Title,
            FileName = string.Empty,
            InitialDirectory = this.SelectedPath,
            Filters = [],
            FilterIndex = 1,
        };

        var paths = backend.ShowFileDialog(in options);
        if (paths is not { Length: > 0 })
            return DialogResult.Cancel;

        this.SelectedPath = paths[0];
        return DialogResult.OK;
    }
}
