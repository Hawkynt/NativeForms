namespace Hawkynt.NativeForms;

/// <summary>Why a <see cref="Form"/> is closing, reported through <see cref="FormClosingEventArgs"/>.</summary>
public enum CloseReason
{
    /// <summary>No close is in progress.</summary>
    None,

    /// <summary>The user is closing the window — the native close button, Alt+F4, ⌘W.</summary>
    UserClosing,

    /// <summary>Code is closing the window — <see cref="Form.Close"/>, including the modal teardown
    /// a <see cref="Form.DialogResult"/> verdict triggers.</summary>
    ProgrammaticClosing,
}
