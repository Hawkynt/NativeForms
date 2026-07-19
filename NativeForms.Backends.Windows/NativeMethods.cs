using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The complete Win32 surface this backend touches: USER32/KERNEL32 entry points, the handful of
/// structs the message loop needs, and the window-style/message constants. Everything here is
/// source-generated P/Invoke (<see cref="LibraryImportAttribute"/>) over blittable, pointer-sized
/// handles (<see cref="nint"/>/<see cref="nuint"/>) so the whole layer stays trim- and AOT-safe.
/// </summary>
internal static partial class NativeMethods
{
    // --- Window class styles (WNDCLASSEXW.style) ---

    /// <summary>Redraws the entire window on a horizontal size change.</summary>
    internal const uint CS_HREDRAW = 0x0002;

    /// <summary>Redraws the entire window on a vertical size change.</summary>
    internal const uint CS_VREDRAW = 0x0001;

    // --- Window styles (dwStyle) ---

    /// <summary>The standard overlapped top-level window (caption, border, sysmenu, min/max).</summary>
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

    /// <summary>A child window; requires a parent HWND.</summary>
    internal const uint WS_CHILD = 0x40000000;

    /// <summary>The window is initially visible.</summary>
    internal const uint WS_VISIBLE = 0x10000000;

    /// <summary>The control can receive keyboard focus via Tab.</summary>
    internal const uint WS_TABSTOP = 0x00010000;

    /// <summary>A thin single-line border around the window.</summary>
    internal const uint WS_BORDER = 0x00800000;

    // --- Button styles (BUTTON class) ---

    /// <summary>A standard push button that posts <c>WM_COMMAND</c> when clicked.</summary>
    internal const uint BS_PUSHBUTTON = 0x00000000;

    // --- Static (label) styles (STATIC class) ---

    /// <summary>Left-aligned static text.</summary>
    internal const uint SS_LEFT = 0x00000000;

    /// <summary>Horizontally centered static text.</summary>
    internal const uint SS_CENTER = 0x00000001;

    /// <summary>Right-aligned static text.</summary>
    internal const uint SS_RIGHT = 0x00000002;

    /// <summary>Do not interpret <c>&amp;</c> as a mnemonic prefix.</summary>
    internal const uint SS_NOPREFIX = 0x00000080;

    /// <summary>Vertically center the content (single-line text only).</summary>
    internal const uint SS_CENTERIMAGE = 0x00000200;

    // --- ShowWindow commands ---

    /// <summary>Hides the window.</summary>
    internal const int SW_HIDE = 0;

    /// <summary>Activates and shows the window at its current size and position.</summary>
    internal const int SW_SHOW = 5;

    // --- Window messages ---

    /// <summary>Sent when a window is being destroyed (after it is removed from the screen).</summary>
    internal const uint WM_DESTROY = 0x0002;

    /// <summary>Sent as a signal that a window should be closed (native close button, Alt+F4).</summary>
    internal const uint WM_CLOSE = 0x0010;

    /// <summary>Sent to a parent when a child control (e.g. a button) generates a notification.</summary>
    internal const uint WM_COMMAND = 0x0111;

    // --- Notification codes (WM_COMMAND high word) ---

    /// <summary>The button was clicked.</summary>
    internal const int BN_CLICKED = 0;

    // --- Miscellaneous ---

    /// <summary>Lets Windows pick a default position/size for the window.</summary>
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

    /// <summary>The standard arrow cursor resource id (used with <see cref="LoadCursorW"/>).</summary>
    internal const nint IDC_ARROW = 32512;

    /// <summary>The window background system color (used as <c>hbrBackground = COLOR_WINDOW + 1</c>).</summary>
    internal const int COLOR_WINDOW = 5;

    /// <summary>
    /// The extended window-class registration structure. Kept fully blittable — the string and
    /// procedure fields are raw pointers we fill in ourselves — so it can be passed straight through
    /// source-generated marshalling.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEXW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint cbSize;

        /// <summary>Class style flags (<c>CS_*</c>).</summary>
        public uint style;

        /// <summary>Pointer to the window procedure (a <c>delegate* unmanaged</c> function pointer).</summary>
        public nint lpfnWndProc;

        /// <summary>Extra bytes to allocate following the window-class structure.</summary>
        public int cbClsExtra;

        /// <summary>Extra bytes to allocate following each window instance.</summary>
        public int cbWndExtra;

        /// <summary>Handle to the instance that contains the window procedure.</summary>
        public nint hInstance;

        /// <summary>Handle to the class icon.</summary>
        public nint hIcon;

        /// <summary>Handle to the class cursor.</summary>
        public nint hCursor;

        /// <summary>Handle to the class background brush.</summary>
        public nint hbrBackground;

        /// <summary>Pointer to a null-terminated menu-resource name (LPCWSTR), or 0.</summary>
        public nint lpszMenuName;

        /// <summary>Pointer to a null-terminated class name (LPCWSTR).</summary>
        public nint lpszClassName;

        /// <summary>Handle to the small class icon.</summary>
        public nint hIconSm;
    }

    /// <summary>A point (x, y) in the message queue's <see cref="MSG.pt"/> field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        /// <summary>The x-coordinate.</summary>
        public int x;

        /// <summary>The y-coordinate.</summary>
        public int y;
    }

    /// <summary>A rectangle described by its edges (left, top, right, bottom).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        /// <summary>The x-coordinate of the left edge.</summary>
        public int left;

        /// <summary>The y-coordinate of the top edge.</summary>
        public int top;

        /// <summary>The x-coordinate of the right edge.</summary>
        public int right;

        /// <summary>The y-coordinate of the bottom edge.</summary>
        public int bottom;
    }

    /// <summary>A thread message as returned by <see cref="GetMessageW"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        /// <summary>Handle to the window whose procedure receives the message.</summary>
        public nint hwnd;

        /// <summary>The message identifier.</summary>
        public uint message;

        /// <summary>Additional message-specific information (WPARAM).</summary>
        public nuint wParam;

        /// <summary>Additional message-specific information (LPARAM).</summary>
        public nint lParam;

        /// <summary>The time the message was posted.</summary>
        public uint time;

        /// <summary>The cursor position, in screen coordinates, when the message was posted.</summary>
        public POINT pt;
    }

    /// <summary>Retrieves a module handle for the specified module, or the calling process when null.</summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint GetModuleHandleW(string? lpModuleName);

    /// <summary>Loads a cursor resource. Pass <see cref="IDC_ARROW"/> with a null instance handle.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    /// <summary>Registers a window class for subsequent use in calls to <see cref="CreateWindowExW"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    /// <summary>Creates an overlapped, pop-up, or child window.</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    /// <summary>Destroys the specified window and its child windows.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    /// <summary>Sets a window's caption/text.</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowTextW(nint hWnd, string lpString);

    /// <summary>Sets a window's show state (see <c>SW_*</c>).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>Enables or disables mouse and keyboard input to a window.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    /// <summary>Repositions and resizes a window.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveWindow(nint hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    /// <summary>Converts a point from a window's client space to screen coordinates, in place.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    /// <summary>Calls the default window procedure for messages this backend does not handle.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// Retrieves the next message from the calling thread's queue. Returns non-zero for a normal
    /// message, 0 on <c>WM_QUIT</c>, and -1 on error (hence the <see cref="int"/> return, not BOOL).
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /// <summary>Translates virtual-key messages into character messages.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in MSG lpMsg);

    /// <summary>Dispatches a message to a window procedure.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint DispatchMessageW(in MSG lpMsg);

    /// <summary>Posts a <c>WM_QUIT</c> message, asking the message loop to terminate.</summary>
    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int nExitCode);

    /// <summary>
    /// Creates an interval timer. Called with a null HWND and a zero event id, USER32 allocates a
    /// fresh timer and returns its id; <paramref name="lpTimerFunc"/> (a <c>TIMERPROC</c> function
    /// pointer) is then invoked by the message loop's <c>WM_TIMER</c> dispatch. Returns 0 on failure.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    /// <summary>Destroys a timer. For window-less timers, pass a null HWND and the id <see cref="SetTimer"/> returned.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint hWnd, nuint uIDEvent);
}
