using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>The button set a message box offers. The numeric values match <c>System.Windows.Forms.MessageBoxButtons</c> and the Win32 <c>MB_*</c> flags.</summary>
public enum MessageBoxButtons
{
    /// <summary>An OK button only.</summary>
    OK = 0,

    /// <summary>OK and Cancel.</summary>
    OKCancel = 1,

    /// <summary>Yes, No and Cancel.</summary>
    YesNoCancel = 3,

    /// <summary>Yes and No.</summary>
    YesNo = 4,

    /// <summary>Retry and Cancel.</summary>
    RetryCancel = 5,
}

/// <summary>The severity glyph a message box shows. The numeric values match <c>System.Windows.Forms.MessageBoxIcon</c> and the Win32 <c>MB_ICON*</c> flags.</summary>
public enum MessageBoxIcon
{
    /// <summary>No icon.</summary>
    None = 0,

    /// <summary>A red error symbol.</summary>
    Error = 0x10,

    /// <summary>A question mark.</summary>
    Question = 0x20,

    /// <summary>A yellow warning symbol.</summary>
    Warning = 0x30,

    /// <summary>A blue information symbol.</summary>
    Information = 0x40,
}

/// <summary>
/// Shows the platform's native message box — <c>MessageBoxW</c> on Windows, a <c>GtkMessageDialog</c>
/// on Linux — and reports which button the user pressed. Runs application-modal on the calling
/// (UI) thread, so it must be invoked from inside <see cref="Application.Run(Form)"/>.
/// </summary>
public static class MessageBox
{
    /// <summary>Shows <paramref name="text"/> with an OK button and no caption or icon.</summary>
    public static DialogResult Show(string text)
        => Show(text, string.Empty);

    /// <summary>Shows <paramref name="text"/> under <paramref name="caption"/> with an OK button.</summary>
    public static DialogResult Show(string text, string caption)
        => Show(text, caption, MessageBoxButtons.OK);

    /// <summary>Shows <paramref name="text"/> under <paramref name="caption"/> with the given button set.</summary>
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        => Show(text, caption, buttons, MessageBoxIcon.None);

    /// <summary>Shows <paramref name="text"/> under <paramref name="caption"/> with the given buttons and icon.</summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(
            Application.Current ?? throw new InvalidOperationException(
                "MessageBox.Show needs a running backend — call it from inside Application.Run."),
            text, caption, buttons, icon);

    /// <summary>Shows the message box on an explicit backend. Intended for tests.</summary>
    internal static DialogResult Show(IPlatformBackend backend, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => backend.ShowMessageBox(text ?? string.Empty, caption ?? string.Empty, buttons, icon);
}
