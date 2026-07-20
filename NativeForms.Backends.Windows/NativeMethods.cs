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

    /// <summary>A title bar (implies a border).</summary>
    internal const uint WS_CAPTION = 0x00C00000;

    /// <summary>A system menu in the title bar (the close button lives here).</summary>
    internal const uint WS_SYSMENU = 0x00080000;

    /// <summary>A sizing border the user can drag.</summary>
    internal const uint WS_THICKFRAME = 0x00040000;

    /// <summary>A minimize button in the title bar.</summary>
    internal const uint WS_MINIMIZEBOX = 0x00020000;

    /// <summary>A maximize button in the title bar.</summary>
    internal const uint WS_MAXIMIZEBOX = 0x00010000;

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

    /// <summary>The button displays the bitmap attached via <c>BM_SETIMAGE</c> instead of text.</summary>
    internal const uint BS_BITMAP = 0x00000080;

    /// <summary>Attaches an image to a button; wParam is the image type, lParam the handle.</summary>
    internal const uint BM_SETIMAGE = 0x00F7;

    // --- Static (label) styles (STATIC class) ---

    /// <summary>The static displays the bitmap attached via <c>STM_SETIMAGE</c> (a type value, not a flag).</summary>
    internal const uint SS_BITMAP = 0x0000000E;

    /// <summary>Attaches an image to a static control; wParam is the image type, lParam the handle.</summary>
    internal const uint STM_SETIMAGE = 0x0172;

    /// <summary>Image-type argument for <c>BM_SETIMAGE</c>/<c>STM_SETIMAGE</c>: an <c>HBITMAP</c>.</summary>
    internal const nint IMAGE_BITMAP = 0;

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

    // --- Edit styles (EDIT class) ---

    /// <summary>Scrolls text horizontally as the caret passes the right edge (single-line editing).</summary>
    internal const uint ES_AUTOHSCROLL = 0x0080;

    /// <summary>A multiline edit control.</summary>
    internal const uint ES_MULTILINE = 0x0004;

    /// <summary>Scrolls text vertically as the caret passes the bottom edge (multiline editing).</summary>
    internal const uint ES_AUTOVSCROLL = 0x0040;

    /// <summary>The window has a vertical scroll bar.</summary>
    internal const uint WS_VSCROLL = 0x00200000;

    // --- Edit messages ---

    /// <summary>Retrieves the selection as start/end character positions written through the pointers passed in wParam/lParam.</summary>
    internal const uint EM_GETSEL = 0x00B0;

    /// <summary>Selects the character range wParam (start) … lParam (end).</summary>
    internal const uint EM_SETSEL = 0x00B1;

    /// <summary>Caps the amount of text the user can type; wParam 0 raises the limit to the class maximum.</summary>
    internal const uint EM_SETLIMITTEXT = 0x00C5;

    /// <summary>Sets the character shown instead of typed text; wParam 0 turns masking off.</summary>
    internal const uint EM_SETPASSWORDCHAR = 0x00CC;

    /// <summary>Toggles the read-only state; wParam is a BOOL.</summary>
    internal const uint EM_SETREADONLY = 0x00CF;

    /// <summary>Sets the grey cue-banner hint (lParam LPCWSTR); single-line EDIT controls only.</summary>
    internal const uint EM_SETCUEBANNER = 0x1501;

    // --- Extended window styles (dwExStyle) ---

    /// <summary>A double border without a system-menu icon — the classic dialog frame.</summary>
    internal const uint WS_EX_DLGMODALFRAME = 0x00000001;

    /// <summary>The window sits above all non-topmost windows (managed via <c>SetWindowPos</c>).</summary>
    internal const uint WS_EX_TOPMOST = 0x00000008;

    /// <summary>A layered window whose opacity <see cref="SetLayeredWindowAttributes"/> controls.</summary>
    internal const uint WS_EX_LAYERED = 0x00080000;

    // --- ShowWindow commands ---

    /// <summary>Hides the window.</summary>
    internal const int SW_HIDE = 0;

    /// <summary>Activates and shows the window minimized.</summary>
    internal const int SW_SHOWMINIMIZED = 2;

    /// <summary>Activates and shows the window maximized.</summary>
    internal const int SW_SHOWMAXIMIZED = 3;

    /// <summary>Activates and shows the window at its current size and position.</summary>
    internal const int SW_SHOW = 5;

    /// <summary>Minimizes the window.</summary>
    internal const int SW_MINIMIZE = 6;

    /// <summary>Activates and restores the window from its minimized or maximized state.</summary>
    internal const int SW_RESTORE = 9;

    // --- Window messages ---

    /// <summary>Sent when a window is being destroyed (after it is removed from the screen).</summary>
    internal const uint WM_DESTROY = 0x0002;

    /// <summary>Sent after a window has been moved; the client origin is packed into lParam.</summary>
    internal const uint WM_MOVE = 0x0003;

    /// <summary>Sent after a window's size changed; wParam carries the <c>SIZE_*</c> state word.</summary>
    internal const uint WM_SIZE = 0x0005;

    /// <summary>Sent as a signal that a window should be closed (native close button, Alt+F4).</summary>
    internal const uint WM_CLOSE = 0x0010;

    /// <summary>Sent while a window is being sized/moved so it can constrain the tracking rectangle.</summary>
    internal const uint WM_GETMINMAXINFO = 0x0024;

    /// <summary>Associates an icon with the window; wParam is <see cref="ICON_SMALL"/>/<see cref="ICON_BIG"/>, lParam the HICON.</summary>
    internal const uint WM_SETICON = 0x0080;

    /// <summary>Sent to a parent when a child control (e.g. a button) generates a notification.</summary>
    internal const uint WM_COMMAND = 0x0111;

    // --- WM_SIZE state words (wParam) ---

    /// <summary>The window was resized without being minimized or maximized.</summary>
    internal const int SIZE_RESTORED = 0;

    /// <summary>The window was minimized.</summary>
    internal const int SIZE_MINIMIZED = 1;

    /// <summary>The window was maximized.</summary>
    internal const int SIZE_MAXIMIZED = 2;

    // --- WM_SETICON icon slots (wParam) ---

    /// <summary>The small icon (title bar, taskbar).</summary>
    internal const nint ICON_SMALL = 0;

    /// <summary>The big icon (Alt+Tab).</summary>
    internal const nint ICON_BIG = 1;

    /// <summary>The alpha argument of <see cref="SetLayeredWindowAttributes"/> is valid.</summary>
    internal const uint LWA_ALPHA = 0x00000002;

    // --- Notification codes (WM_COMMAND high word) ---

    /// <summary>The button was clicked.</summary>
    internal const int BN_CLICKED = 0;

    /// <summary>The button gained keyboard focus.</summary>
    internal const int BN_SETFOCUS = 6;

    /// <summary>The button lost keyboard focus.</summary>
    internal const int BN_KILLFOCUS = 7;

    /// <summary>The edit control gained keyboard focus.</summary>
    internal const int EN_SETFOCUS = 0x0100;

    /// <summary>The edit control lost keyboard focus.</summary>
    internal const int EN_KILLFOCUS = 0x0200;

    /// <summary>The edit control's text changed (sent after the screen was updated).</summary>
    internal const int EN_CHANGE = 0x0300;

    // --- Miscellaneous ---

    /// <summary>Lets Windows pick a default position/size for the window.</summary>
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

    /// <summary>The standard arrow cursor resource id (used with <see cref="LoadCursorW"/>).</summary>
    internal const nint IDC_ARROW = 32512;

    /// <summary>The text-insertion I-beam cursor resource id.</summary>
    internal const nint IDC_IBEAM = 32513;

    /// <summary>The busy/wait cursor resource id.</summary>
    internal const nint IDC_WAIT = 32514;

    /// <summary>The crosshair cursor resource id.</summary>
    internal const nint IDC_CROSS = 32515;

    /// <summary>The northwest-southeast resize cursor resource id.</summary>
    internal const nint IDC_SIZENWSE = 32642;

    /// <summary>The northeast-southwest resize cursor resource id.</summary>
    internal const nint IDC_SIZENESW = 32643;

    /// <summary>The west-east resize cursor resource id.</summary>
    internal const nint IDC_SIZEWE = 32644;

    /// <summary>The north-south resize cursor resource id.</summary>
    internal const nint IDC_SIZENS = 32645;

    /// <summary>The four-headed move cursor resource id.</summary>
    internal const nint IDC_SIZEALL = 32646;

    /// <summary>The "not allowed" cursor resource id.</summary>
    internal const nint IDC_NO = 32648;

    /// <summary>The pointing-hand cursor resource id.</summary>
    internal const nint IDC_HAND = 32649;

    /// <summary>The arrow-with-hourglass (working in background) cursor resource id.</summary>
    internal const nint IDC_APPSTARTING = 32650;

    /// <summary>The arrow-with-question-mark (help) cursor resource id.</summary>
    internal const nint IDC_HELP = 32651;

    /// <summary>Sent to decide the pointer shape; the low word of <c>lParam</c> is the hit-test code.</summary>
    internal const uint WM_SETCURSOR = 0x0020;

    /// <summary>Applies a font handle to a control; <c>lParam != 0</c> requests an immediate redraw.</summary>
    internal const uint WM_SETFONT = 0x0030;

    /// <summary>The <c>WM_SETCURSOR</c> hit-test code for the client area.</summary>
    internal const int HTCLIENT = 1;

    /// <summary>Sent to an editable edit control's parent to pick text/background colors.</summary>
    internal const uint WM_CTLCOLOREDIT = 0x0133;

    /// <summary>Sent to a list box's parent to pick text/background colors.</summary>
    internal const uint WM_CTLCOLORLISTBOX = 0x0134;

    /// <summary>Sent to a button's parent; classic push buttons only honor the background brush.</summary>
    internal const uint WM_CTLCOLORBTN = 0x0135;

    /// <summary>Sent to a static (or read-only/disabled edit) control's parent to pick colors.</summary>
    internal const uint WM_CTLCOLORSTATIC = 0x0138;

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

    /// <summary>
    /// The tracking limits a window fills in while handling <see cref="WM_GETMINMAXINFO"/>: the
    /// <c>ptMinTrackSize</c>/<c>ptMaxTrackSize</c> pair caps user resizing.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        /// <summary>Reserved.</summary>
        public POINT ptReserved;

        /// <summary>The maximized size of the window.</summary>
        public POINT ptMaxSize;

        /// <summary>The maximized position of the window.</summary>
        public POINT ptMaxPosition;

        /// <summary>The smallest size the user can drag the window to.</summary>
        public POINT ptMinTrackSize;

        /// <summary>The largest size the user can drag the window to.</summary>
        public POINT ptMaxTrackSize;
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

    /// <summary>Retrieves a window's bounding rectangle in screen coordinates.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    /// <summary>Sets the opacity (and optional color key) of a <see cref="WS_EX_LAYERED"/> window.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

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

    /// <summary>Sends a message to a window and waits for it to be processed.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Queues a message into a window's thread message queue without waiting.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Sends a message whose lParam is a string (for example <see cref="EM_SETCUEBANNER"/>).</summary>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SendMessageStringW(nint hWnd, uint msg, nint wParam, string lParam);

    /// <summary>Returns the length, in characters, of the window's text.</summary>
    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLengthW(nint hWnd);

    /// <summary>Copies the window's text into the caller-provided buffer; returns the copied length.</summary>
    [LibraryImport("user32.dll")]
    internal static unsafe partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

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
