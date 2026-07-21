namespace Hawkynt.NativeForms;

/// <summary>Which native file dialog a <see cref="FilePicker"/> browses with.</summary>
public enum FilePickerMode
{
    /// <summary>Pick an existing file through <see cref="OpenFileDialog"/>. The default.</summary>
    Open,

    /// <summary>Name a file to write through <see cref="SaveFileDialog"/>.</summary>
    Save,
}
