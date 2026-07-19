using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>The Win32 clipboard surface: the classic OpenClipboard/SetClipboardData handshake plus
/// the movable global memory it requires.</summary>
internal static partial class NativeMethods
{
    /// <summary>Clipboard format for zero-terminated UTF-16 text (<c>CF_UNICODETEXT</c>).</summary>
    internal const uint CF_UNICODETEXT = 13;

    /// <summary>Allocates movable global memory — the only kind the clipboard accepts.</summary>
    internal const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>Opens the clipboard for this thread; 0 owner associates it with the current task.</summary>
    [LibraryImport("user32.dll")]
    internal static partial int OpenClipboard(nint hWndNewOwner);

    /// <summary>Empties the open clipboard and claims ownership.</summary>
    [LibraryImport("user32.dll")]
    internal static partial int EmptyClipboard();

    /// <summary>Places data on the open clipboard; on success the system owns <paramref name="hMem"/>.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SetClipboardData(uint uFormat, nint hMem);

    /// <summary>Closes the clipboard, making the placed data visible to other applications.</summary>
    [LibraryImport("user32.dll")]
    internal static partial int CloseClipboard();

    /// <summary>Allocates a global memory block of the given byte size.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    /// <summary>Pins a movable global block and returns its address.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    /// <summary>Releases a <see cref="GlobalLock"/> pin.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial int GlobalUnlock(nint hMem);

    /// <summary>Frees a global block the clipboard did not take ownership of.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);
}
