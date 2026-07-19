using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The shell surface of the backend: the <c>Shell_NotifyIconW</c> entry point, its data structure and
/// the icon-creation helpers the tray peer needs. Kept in a separate partial like the other native
/// surfaces; everything is source-generated P/Invoke over blittable data so the layer stays trim- and
/// AOT-safe.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // --- Shell_NotifyIconW messages (dwMessage) ---

    /// <summary>Adds an icon to the status area.</summary>
    internal const uint NIM_ADD = 0x00000000;

    /// <summary>Modifies an icon already in the status area.</summary>
    internal const uint NIM_MODIFY = 0x00000001;

    /// <summary>Removes an icon from the status area.</summary>
    internal const uint NIM_DELETE = 0x00000002;

    // --- NOTIFYICONDATAW.uFlags ---

    /// <summary>The <c>uCallbackMessage</c> member is valid.</summary>
    internal const uint NIF_MESSAGE = 0x00000001;

    /// <summary>The <c>hIcon</c> member is valid.</summary>
    internal const uint NIF_ICON = 0x00000002;

    /// <summary>The <c>szTip</c> member is valid.</summary>
    internal const uint NIF_TIP = 0x00000004;

    // --- Miscellaneous ---

    /// <summary>The first message number private to an application; the tray callback uses it.</summary>
    internal const uint WM_APP = 0x8000;

    /// <summary>Left mouse button double-clicked (relayed through the tray callback).</summary>
    internal const uint WM_LBUTTONDBLCLK = 0x0203;

    /// <summary>Passed as the parent HWND to create a message-only window (no display, just a queue).</summary>
    internal const nint HWND_MESSAGE = -3;

    /// <summary>
    /// The tray-icon registration passed to <see cref="Shell_NotifyIconW"/>. Fully blittable — the
    /// hover text lives in the inline <c>szTip</c> buffer — so it crosses the boundary without any
    /// marshalling. Declared with the classic 128-character tip (the V2 size, supported everywhere).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NOTIFYICONDATAW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint cbSize;

        /// <summary>The message window that receives the callback messages.</summary>
        public nint hWnd;

        /// <summary>The application-defined identifier of the icon within the window.</summary>
        public uint uID;

        /// <summary>Which members are valid (<c>NIF_*</c>).</summary>
        public uint uFlags;

        /// <summary>The application-private message the shell sends for icon interactions.</summary>
        public uint uCallbackMessage;

        /// <summary>The icon handle to show.</summary>
        public nint hIcon;

        /// <summary>The null-terminated hover text.</summary>
        public fixed char szTip[128];
    }

    /// <summary>Describes the bitmaps an icon is built from for <see cref="CreateIconIndirect"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        /// <summary>Non-zero for an icon, zero for a cursor.</summary>
        public int fIcon;

        /// <summary>The cursor hotspot x-coordinate (ignored for icons).</summary>
        public uint xHotspot;

        /// <summary>The cursor hotspot y-coordinate (ignored for icons).</summary>
        public uint yHotspot;

        /// <summary>The AND mask bitmap; for 32bpp color bitmaps an empty monochrome mask suffices.</summary>
        public nint hbmMask;

        /// <summary>The color bitmap carrying the (alpha-capable) pixels.</summary>
        public nint hbmColor;
    }

    /// <summary>Adds, modifies or removes a status-area icon.</summary>
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIconW(uint dwMessage, in NOTIFYICONDATAW lpData);

    /// <summary>Creates an icon (or cursor) from the bitmaps described by <paramref name="piconinfo"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreateIconIndirect(in ICONINFO piconinfo);

    /// <summary>Destroys an icon created with <see cref="CreateIconIndirect"/>.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    /// <summary>Creates a device-dependent bitmap; used for the monochrome icon mask.</summary>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, nint lpBits);
}
