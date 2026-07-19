namespace Hawkynt.NativeForms;

/// <summary>
/// The frame a <see cref="Form"/>'s native window wears. Win32 maps each value to window-style bits
/// (<c>WS_THICKFRAME</c>, <c>WS_CAPTION</c>, <c>WS_EX_TOOLWINDOW</c> …) toggled live on the HWND;
/// GTK maps to <c>gtk_window_set_resizable</c>/<c>set_decorated</c>/<c>set_type_hint</c>, where the
/// window manager has the final say over the exact frame.
/// </summary>
public enum FormBorderStyle
{
    /// <summary>No frame at all — no caption, no border, not resizable.</summary>
    None,

    /// <summary>A caption with a thin single-line border; not resizable by the user.</summary>
    FixedSingle,

    /// <summary>A dialog frame; not resizable by the user.</summary>
    FixedDialog,

    /// <summary>A tool-window caption (small, no taskbar entry on Windows); not resizable.</summary>
    FixedToolWindow,

    /// <summary>The standard resizable window frame. The default.</summary>
    Sizable,
}
