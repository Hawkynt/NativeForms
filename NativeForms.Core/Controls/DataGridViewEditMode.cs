namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="DataGridView"/> cell enters edit mode — the Windows Forms modes the toolkit's
/// focus model can honor.
/// </summary>
public enum DataGridViewEditMode
{
    /// <summary>Typing a character, F2 on the current cell or a double-click begins the edit. The
    /// default.</summary>
    EditOnKeystrokeOrF2,

    /// <summary>A cell begins editing as soon as it becomes current — clicking it, or Tab/Enter
    /// moving there from a committed edit; the keystroke gestures keep working.</summary>
    EditOnEnter,

    /// <summary>Only an explicit <see cref="DataGridView.BeginEdit"/> call begins an edit; every
    /// user gesture is ignored.</summary>
    EditProgrammatically,
}
