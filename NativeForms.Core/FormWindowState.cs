namespace Hawkynt.NativeForms;

/// <summary>
/// Whether a <see cref="Form"/> is shown normally, minimized or maximized. Two-way: assigning
/// <see cref="Form.WindowState"/> drives the native window, and native state changes (the user
/// clicking the caption buttons) flow back into the property without echoing.
/// </summary>
public enum FormWindowState
{
    /// <summary>The window occupies its <see cref="Control.Bounds"/>.</summary>
    Normal,

    /// <summary>The window is minimized (iconified).</summary>
    Minimized,

    /// <summary>The window fills the screen's working area.</summary>
    Maximized,
}
