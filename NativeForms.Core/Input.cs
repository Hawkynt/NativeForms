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
/// so backends can forward them directly; letters and digits use their ASCII code. Like its Windows
/// Forms namesake the enum doubles as key <em>data</em>: the <see cref="Shift"/>/<see cref="Control"/>/
/// <see cref="Alt"/> modifier bits combine with a key code to describe a chord such as
/// <c>Keys.Control | Keys.S</c> for menu shortcuts.
/// </summary>
[Flags]
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

    /// <summary>Enter/Return — the classic alias for <see cref="Enter"/>, kept for ported code.</summary>
    Return = Enter,

    /// <summary>The Shift key itself (either side), as opposed to the <see cref="Shift"/> modifier bit.</summary>
    ShiftKey = 0x10,

    /// <summary>The Ctrl key itself (either side), as opposed to the <see cref="Control"/> modifier bit.</summary>
    ControlKey = 0x11,

    /// <summary>The Alt key itself — <c>VK_MENU</c>, hence the classic name.</summary>
    Menu = 0x12,

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

    /// <summary>F4 (toggles combo drop-downs, like the classic toolkits).</summary>
    F4 = 0x73,

    /// <summary>Delete.</summary>
    Delete = 0x2E,

    /// <summary>The context-menu (application) key.</summary>
    Apps = 0x5D,

    /// <summary>Numeric-keypad 0.</summary>
    NumPad0 = 0x60,

    /// <summary>Numeric-keypad 1.</summary>
    NumPad1 = 0x61,

    /// <summary>Numeric-keypad 2.</summary>
    NumPad2 = 0x62,

    /// <summary>Numeric-keypad 3.</summary>
    NumPad3 = 0x63,

    /// <summary>Numeric-keypad 4.</summary>
    NumPad4 = 0x64,

    /// <summary>Numeric-keypad 5.</summary>
    NumPad5 = 0x65,

    /// <summary>Numeric-keypad 6.</summary>
    NumPad6 = 0x66,

    /// <summary>Numeric-keypad 7.</summary>
    NumPad7 = 0x67,

    /// <summary>Numeric-keypad 8.</summary>
    NumPad8 = 0x68,

    /// <summary>Numeric-keypad 9.</summary>
    NumPad9 = 0x69,

    /// <summary>Numeric-keypad multiply (*).</summary>
    Multiply = 0x6A,

    /// <summary>Numeric-keypad add (+).</summary>
    Add = 0x6B,

    /// <summary>Numeric-keypad subtract (−).</summary>
    Subtract = 0x6D,

    /// <summary>Numeric-keypad decimal separator (.).</summary>
    Decimal = 0x6E,

    /// <summary>Numeric-keypad divide (/).</summary>
    Divide = 0x6F,

    /// <summary>The =/+ key on the main keyboard row (<c>VK_OEM_PLUS</c>).</summary>
    Oemplus = 0xBB,

    /// <summary>The -/_ key on the main keyboard row (<c>VK_OEM_MINUS</c>).</summary>
    OemMinus = 0xBD,

    /// <summary>The comma key (<c>VK_OEM_COMMA</c>).</summary>
    Oemcomma = 0xBC,

    /// <summary>The period key (<c>VK_OEM_PERIOD</c>).</summary>
    OemPeriod = 0xBE,

    /// <summary>The digit 0.</summary>
    D0 = 0x30,

    /// <summary>The digit 1.</summary>
    D1 = 0x31,

    /// <summary>The digit 2.</summary>
    D2 = 0x32,

    /// <summary>The digit 3.</summary>
    D3 = 0x33,

    /// <summary>The digit 4.</summary>
    D4 = 0x34,

    /// <summary>The digit 5.</summary>
    D5 = 0x35,

    /// <summary>The digit 6.</summary>
    D6 = 0x36,

    /// <summary>The digit 7.</summary>
    D7 = 0x37,

    /// <summary>The digit 8.</summary>
    D8 = 0x38,

    /// <summary>The digit 9.</summary>
    D9 = 0x39,

    /// <summary>The letter A.</summary>
    A = 0x41,

    /// <summary>The letter B.</summary>
    B = 0x42,

    /// <summary>The letter C.</summary>
    C = 0x43,

    /// <summary>The letter D.</summary>
    D = 0x44,

    /// <summary>The letter E.</summary>
    E = 0x45,

    /// <summary>The letter F.</summary>
    F = 0x46,

    /// <summary>The letter G.</summary>
    G = 0x47,

    /// <summary>The letter H.</summary>
    H = 0x48,

    /// <summary>The letter I.</summary>
    I = 0x49,

    /// <summary>The letter J.</summary>
    J = 0x4A,

    /// <summary>The letter K.</summary>
    K = 0x4B,

    /// <summary>The letter L.</summary>
    L = 0x4C,

    /// <summary>The letter M.</summary>
    M = 0x4D,

    /// <summary>The letter N.</summary>
    N = 0x4E,

    /// <summary>The letter O.</summary>
    O = 0x4F,

    /// <summary>The letter P.</summary>
    P = 0x50,

    /// <summary>The letter Q.</summary>
    Q = 0x51,

    /// <summary>The letter R.</summary>
    R = 0x52,

    /// <summary>The letter S.</summary>
    S = 0x53,

    /// <summary>The letter T.</summary>
    T = 0x54,

    /// <summary>The letter U.</summary>
    U = 0x55,

    /// <summary>The letter V.</summary>
    V = 0x56,

    /// <summary>The letter W.</summary>
    W = 0x57,

    /// <summary>The letter X.</summary>
    X = 0x58,

    /// <summary>The letter Y.</summary>
    Y = 0x59,

    /// <summary>The letter Z.</summary>
    Z = 0x5A,

    /// <summary>F1.</summary>
    F1 = 0x70,

    /// <summary>F2.</summary>
    F2 = 0x71,

    /// <summary>F3.</summary>
    F3 = 0x72,

    /// <summary>F5.</summary>
    F5 = 0x74,

    /// <summary>F6.</summary>
    F6 = 0x75,

    /// <summary>F7.</summary>
    F7 = 0x76,

    /// <summary>F8.</summary>
    F8 = 0x77,

    /// <summary>F9.</summary>
    F9 = 0x78,

    /// <summary>F10.</summary>
    F10 = 0x79,

    /// <summary>F11.</summary>
    F11 = 0x7A,

    /// <summary>F12.</summary>
    F12 = 0x7B,

    /// <summary>The bit mask extracting the key code from key data.</summary>
    KeyCode = 0xFFFF,

    /// <summary>The bit mask extracting the modifier bits from key data.</summary>
    Modifiers = ~0xFFFF,

    /// <summary>The Shift modifier bit.</summary>
    Shift = 0x10000,

    /// <summary>The Control modifier bit.</summary>
    Control = 0x20000,

    /// <summary>The Alt modifier bit.</summary>
    Alt = 0x40000,
}
