namespace Hawkynt.NativeForms;

/// <summary>Mouse buttons, combinable, matching <c>System.Windows.Forms.MouseButtons</c>.</summary>
[Flags]
public enum MouseButtons
{
    /// <summary>No button.</summary>
    None = 0,

    /// <summary>The left button.</summary>
    Left = 1,

    /// <summary>The right button.</summary>
    Right = 2,

    /// <summary>The middle button.</summary>
    Middle = 4,
}

/// <summary>Keyboard modifier flags.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift held.</summary>
    Shift = 1,

    /// <summary>Control held.</summary>
    Control = 2,

    /// <summary>Alt held.</summary>
    Alt = 4,
}

/// <summary>
/// Virtual key codes for the keys the toolkit reacts to. Values follow the Win32 virtual-key numbers
/// so backends can forward them directly; letters and digits use their ASCII code.
/// </summary>
public enum Keys
{
    /// <summary>No key.</summary>
    None = 0,

    /// <summary>Backspace.</summary>
    Back = 0x08,

    /// <summary>Tab.</summary>
    Tab = 0x09,

    /// <summary>Enter/Return.</summary>
    Enter = 0x0D,

    /// <summary>Escape.</summary>
    Escape = 0x1B,

    /// <summary>Spacebar.</summary>
    Space = 0x20,

    /// <summary>Page Up.</summary>
    PageUp = 0x21,

    /// <summary>Page Down.</summary>
    PageDown = 0x22,

    /// <summary>End.</summary>
    End = 0x23,

    /// <summary>Home.</summary>
    Home = 0x24,

    /// <summary>Left arrow.</summary>
    Left = 0x25,

    /// <summary>Up arrow.</summary>
    Up = 0x26,

    /// <summary>Right arrow.</summary>
    Right = 0x27,

    /// <summary>Down arrow.</summary>
    Down = 0x28,

    /// <summary>Insert.</summary>
    Insert = 0x2D,

    /// <summary>Delete.</summary>
    Delete = 0x2E,
}
