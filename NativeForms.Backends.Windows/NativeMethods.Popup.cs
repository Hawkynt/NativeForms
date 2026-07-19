using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The popup-surface slice of the backend: the window styles, messages and mouse-capture entry points
/// the light-dismiss popup peer needs on top of the canvas machinery. Kept in a separate partial so
/// the windowing (<c>NativeMethods.cs</c>) and drawing (<c>NativeMethods.Gdi.cs</c>) surfaces stay
/// focused. Everything is source-generated P/Invoke over blittable handles so the layer remains
/// trim- and AOT-safe.
/// </summary>
internal static partial class NativeMethods
{
    // --- Additional window styles ---

    /// <summary>A borderless top-level pop-up window.</summary>
    internal const uint WS_POPUP = 0x80000000;

    // --- Extended window styles (dwExStyle) ---

    /// <summary>A tool window: never shown in the taskbar or the Alt+Tab list.</summary>
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>The window does not become the foreground window when the user clicks it.</summary>
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    // --- SetWindowPos insert-after handles and flags ---

    /// <summary>Places the window above all non-topmost windows and keeps it there.</summary>
    internal const nint HWND_TOPMOST = -1;

    /// <summary>Does not activate the window while repositioning it.</summary>
    internal const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Shows the window as part of the repositioning.</summary>
    internal const uint SWP_SHOWWINDOW = 0x0040;

    // --- Additional window messages ---

    /// <summary>Sent to the window losing mouse capture (a light-dismiss trigger for the popup).</summary>
    internal const uint WM_CAPTURECHANGED = 0x0215;

    // --- Additional virtual key codes ---

    /// <summary>The Escape key.</summary>
    internal const int VK_ESCAPE = 0x1B;

    /// <summary>Changes a window's size, position, show state and Z order in a single call.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>Routes all subsequent mouse input to the window until capture is released.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SetCapture(nint hWnd);

    /// <summary>Releases mouse capture from the current thread's capture window.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    /// <summary>Returns the current thread's capture window, or 0 when no window holds the mouse.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint GetCapture();

    /// <summary>Retrieves a window's client rectangle (its top-left is always 0,0).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hWnd, out RECT lpRect);
}
