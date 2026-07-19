namespace Hawkynt.NativeForms;

/// <summary>
/// The verdict a dialog returns from <see cref="Form.ShowDialog"/> or <see cref="MessageBox.Show(string)"/>.
/// The numeric values match both <c>System.Windows.Forms.DialogResult</c> and the Win32
/// <c>MessageBox</c> return ids (<c>IDOK</c> … <c>IDNO</c>), so the Win32 backend maps by cast.
/// </summary>
public enum DialogResult
{
    /// <summary>No result yet — the dialog is still open or was never shown.</summary>
    None = 0,

    /// <summary>The OK button.</summary>
    OK = 1,

    /// <summary>The Cancel button, Escape, or the window's close box.</summary>
    Cancel = 2,

    /// <summary>The Abort button.</summary>
    Abort = 3,

    /// <summary>The Retry button.</summary>
    Retry = 4,

    /// <summary>The Ignore button.</summary>
    Ignore = 5,

    /// <summary>The Yes button.</summary>
    Yes = 6,

    /// <summary>The No button.</summary>
    No = 7,
}
