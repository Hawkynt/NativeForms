using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Real OS-level input on Win32: the pointer is moved and the buttons and keys are pressed through
/// <c>SendInput</c>, so the messages arrive from the input queue exactly as a person's would.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the autopilot's <c>gdk_test_simulate_*</c> calls, and the reason it exists: that
/// injection is GTK-only, so every check built on it stops at the Linux border. Nothing posts messages
/// directly here — a posted <c>WM_LBUTTONDOWN</c> proves a window procedure works and says nothing
/// about hit-testing, capture, focus or z-order, which is most of what goes wrong.
/// </para>
/// <para>
/// A session without an interactive desktop cannot deliver injected input at all, so every entry point
/// reports whether it landed rather than assuming it did, and the caller treats "could not inject" as
/// a skip rather than a failure. Claiming a pass from input that never arrived would be worse than not
/// testing it.
/// </para>
/// </remarks>
internal static unsafe partial class ShootInput
{
    private const uint _InputMouse = 0;
    private const uint _InputKeyboard = 1;
    private const uint _MouseEventAbsolute = 0x8000;
    private const uint _MouseEventMove = 0x0001;
    private const uint _MouseEventLeftDown = 0x0002;
    private const uint _MouseEventLeftUp = 0x0004;
    private const uint _KeyEventKeyUp = 0x0002;
    private const uint _KeyEventUnicode = 0x0004;
    private const int _SmCxScreen = 0;
    private const int _SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG msg, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string? windowName);

    /// <summary>Whether injected input can be delivered here at all.</summary>
    public static bool Available { get; private set; } = true;

    /// <summary>Brings the gallery to the foreground, so injected input has somewhere to land.</summary>
    public static void Activate(string windowTitle)
    {
        var hwnd = FindWindowW(null, windowTitle);
        if (hwnd != 0)
            SetForegroundWindow(hwnd);
    }

    /// <summary>Clicks at a screen point, reporting whether the input was accepted by the queue.</summary>
    public static bool Click(Point screen)
    {
        // SendInput takes absolute coordinates normalized over the virtual screen.
        var width = Math.Max(1, GetSystemMetrics(_SmCxScreen));
        var height = Math.Max(1, GetSystemMetrics(_SmCyScreen));
        var x = screen.X * 65535 / width;
        var y = screen.Y * 65535 / height;

        var inputs = stackalloc INPUT[3];
        inputs[0] = Mouse(x, y, _MouseEventMove | _MouseEventAbsolute);
        inputs[1] = Mouse(x, y, _MouseEventLeftDown | _MouseEventAbsolute);
        inputs[2] = Mouse(x, y, _MouseEventLeftUp | _MouseEventAbsolute);
        return Send(inputs, 3);
    }

    /// <summary>Types one character as a Unicode key press, reporting whether it was accepted.</summary>
    public static bool Type(char character)
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = Key(character, _KeyEventUnicode);
        inputs[1] = Key(character, _KeyEventUnicode | _KeyEventKeyUp);
        return Send(inputs, 2);
    }

    /// <summary>
    /// Runs the pending messages, so an injected event has been delivered and handled before anything
    /// asks whether it arrived. Injected input joins the queue; it is not a function call, and checking
    /// for its effect without draining the queue first would test the timing rather than the toolkit.
    /// </summary>
    public static void Drain()
    {
        const uint remove = 0x0001;
        for (var guard = 0; guard < 512 && PeekMessageW(out var message, 0, 0, 0, remove); ++guard)
        {
            TranslateMessage(in message);
            DispatchMessageW(in message);
        }
    }

    private static INPUT Mouse(int x, int y, uint flags)
        => new() { type = _InputMouse, union = new() { mouse = new() { dx = x, dy = y, dwFlags = flags } } };

    private static INPUT Key(char character, uint flags)
        => new() { type = _InputKeyboard, union = new() { keyboard = new() { wScan = character, dwFlags = flags } } };

    private static bool Send(INPUT* inputs, uint count)
    {
        var sent = SendInput(count, inputs, sizeof(INPUT));
        if (sent == count)
            return true;

        // A session with no interactive desktop refuses every event; say so once and stop pretending.
        Available = false;
        return false;
    }
}
