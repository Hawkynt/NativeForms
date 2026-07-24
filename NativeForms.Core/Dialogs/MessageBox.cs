using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The button set a message box offers. The numeric values match <c>System.Windows.Forms.MessageBoxButtons</c> and the Win32 <c>MB_*</c> flags.</summary>
public enum MessageBoxButtons
{
    /// <summary>An OK button only.</summary>
    OK = 0,

    /// <summary>OK and Cancel.</summary>
    OKCancel = 1,

    /// <summary>Abort, Retry and Ignore.</summary>
    AbortRetryIgnore = 2,

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
        => Show(RequireBackend(), null, text, caption, buttons, icon);

    /// <summary>Shows <paramref name="text"/> owned by <paramref name="owner"/> with an OK button and no caption or icon.</summary>
    public static DialogResult Show(Form owner, string text)
        => Show(owner, text, string.Empty);

    /// <summary>Shows <paramref name="text"/> owned by <paramref name="owner"/> under <paramref name="caption"/> with an OK button.</summary>
    public static DialogResult Show(Form owner, string text, string caption)
        => Show(owner, text, caption, MessageBoxButtons.OK);

    /// <summary>Shows <paramref name="text"/> owned by <paramref name="owner"/> under <paramref name="caption"/> with the given button set.</summary>
    public static DialogResult Show(Form owner, string text, string caption, MessageBoxButtons buttons)
        => Show(owner, text, caption, buttons, MessageBoxIcon.None);

    /// <summary>
    /// Shows <paramref name="text"/> owned by (transient to) <paramref name="owner"/> under
    /// <paramref name="caption"/> with the given buttons and icon. The owner must be realized; an
    /// unrealized owner falls back to the running application backend without ownership.
    /// </summary>
    /// <exception cref="InvalidOperationException">The owner is unrealized and no application message loop is running.</exception>
    public static DialogResult Show(Form owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(owner?.Backend ?? RequireBackend(), owner, text, caption, buttons, icon);

    /// <summary>Shows the message box on an explicit backend. Intended for tests.</summary>
    internal static DialogResult Show(IPlatformBackend backend, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(backend, null, text, caption, buttons, icon);

    /// <summary>The single funnel every native overload ends in: resolve the owner's window peer and forward.</summary>
    internal static DialogResult Show(IPlatformBackend backend, Form? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => backend.ShowMessageBox(text ?? string.Empty, caption ?? string.Empty, buttons, icon, owner?.WindowPeer);

    // --- Custom icon and custom buttons (owner-drawn) ---------------------------------------------
    // The native dialog cannot show an arbitrary image (let alone an animated one) or arbitrary button
    // labels, so these overloads render an owner-drawn dialog instead. A custom icon may be any
    // IImage, including an AnimatedImage, which animates in the box.

    /// <summary>Shows a message box with a custom <paramref name="icon"/> image (still or an
    /// <see cref="AnimatedImage"/>) and the given standard button set.</summary>
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, IImage icon)
        => ShowOwnerDrawn(RequireBackend(), null, text, caption, buttons, MessageBoxIcon.None, icon);

    /// <summary>Shows a message box owned by <paramref name="owner"/> with a custom <paramref name="icon"/>
    /// image and the given standard button set.</summary>
    public static DialogResult Show(Form owner, string text, string caption, MessageBoxButtons buttons, IImage icon)
        => ShowOwnerDrawn(owner?.Backend ?? RequireBackend(), owner, text, caption, buttons, MessageBoxIcon.None, icon);

    /// <summary>
    /// Shows a message box with arbitrary <paramref name="buttonLabels"/> (left to right) and an
    /// optional custom <paramref name="icon"/> or standard <paramref name="standardIcon"/> glyph, and
    /// returns the zero-based index of the button pressed, or -1 if the dialog was closed another way.
    /// </summary>
    public static int Show(string text, string caption, IReadOnlyList<string> buttonLabels, IImage? icon = null, MessageBoxIcon standardIcon = MessageBoxIcon.None)
        => ShowCustomButtons(RequireBackend(), null, text, caption, buttonLabels, icon, standardIcon);

    /// <summary>The owner-taking counterpart of <see cref="Show(string, string, IReadOnlyList{string}, IImage, MessageBoxIcon)"/>.</summary>
    public static int Show(Form owner, string text, string caption, IReadOnlyList<string> buttonLabels, IImage? icon = null, MessageBoxIcon standardIcon = MessageBoxIcon.None)
        => ShowCustomButtons(owner?.Backend ?? RequireBackend(), owner, text, caption, buttonLabels, icon, standardIcon);

    /// <summary>Custom-button overload on an explicit backend. Intended for tests.</summary>
    internal static int Show(IPlatformBackend backend, string text, string caption, IReadOnlyList<string> buttonLabels, IImage? icon, MessageBoxIcon standardIcon)
        => ShowCustomButtons(backend, null, text, caption, buttonLabels, icon, standardIcon);

    /// <summary>Standard-buttons + custom-icon overload on an explicit backend. Intended for tests.</summary>
    internal static DialogResult Show(IPlatformBackend backend, string text, string caption, MessageBoxButtons buttons, IImage icon)
        => ShowOwnerDrawn(backend, null, text, caption, buttons, MessageBoxIcon.None, icon);

    /// <summary>Runs the owner-drawn dialog for a standard button set with a custom icon and maps the
    /// clicked index back to a <see cref="DialogResult"/>.</summary>
    private static DialogResult ShowOwnerDrawn(IPlatformBackend backend, Form? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon standardIcon, IImage? icon)
    {
        var set = StandardButtons(buttons);
        var form = new MessageBoxForm(backend, text ?? string.Empty, caption ?? string.Empty, icon, standardIcon, set.Labels, set.Default, set.Cancel);
        ConfiguredForTest?.Invoke(form);
        form.ShowDialog(owner, backend);
        return form.ClickedIndex >= 0 ? set.Results[form.ClickedIndex] : DialogResult.Cancel;
    }

    /// <summary>Runs the owner-drawn dialog for arbitrary button labels and returns the clicked index.</summary>
    private static int ShowCustomButtons(IPlatformBackend backend, Form? owner, string text, string caption, IReadOnlyList<string> labels, IImage? icon, MessageBoxIcon standardIcon)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
            throw new ArgumentException("At least one button label is required.", nameof(labels));

        var form = new MessageBoxForm(backend, text ?? string.Empty, caption ?? string.Empty, icon, standardIcon, labels, 0, -1);
        ConfiguredForTest?.Invoke(form);
        form.ShowDialog(owner, backend);
        return form.ClickedIndex;
    }

    /// <summary>The labels, results and default/cancel indices for a standard button set.</summary>
    private static (string[] Labels, DialogResult[] Results, int Default, int Cancel) StandardButtons(MessageBoxButtons buttons)
        => buttons switch
        {
            MessageBoxButtons.OKCancel => (["OK", "Cancel"], [DialogResult.OK, DialogResult.Cancel], 0, 1),
            MessageBoxButtons.AbortRetryIgnore => (["Abort", "Retry", "Ignore"], [DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore], 1, -1),
            MessageBoxButtons.YesNoCancel => (["Yes", "No", "Cancel"], [DialogResult.Yes, DialogResult.No, DialogResult.Cancel], 0, 2),
            MessageBoxButtons.YesNo => (["Yes", "No"], [DialogResult.Yes, DialogResult.No], 0, -1),
            MessageBoxButtons.RetryCancel => (["Retry", "Cancel"], [DialogResult.Retry, DialogResult.Cancel], 0, 1),
            _ => (["OK"], [DialogResult.OK], 0, 0),
        };

    /// <summary>Test seam: configure the freshly-built owner-drawn dialog (for example to script a
    /// button click through the backend's modal action) before it runs modally.</summary>
    internal static Action<MessageBoxForm>? ConfiguredForTest;

    /// <summary>The running application backend, or the classic "no message loop" complaint.</summary>
    private static IPlatformBackend RequireBackend()
        => Application.Current ?? throw new InvalidOperationException(
            "MessageBox.Show needs a running backend — call it from inside Application.Run.");
}
