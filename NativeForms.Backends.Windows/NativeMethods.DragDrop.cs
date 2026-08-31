using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

internal static unsafe partial class NativeMethods
{
    internal const uint WM_DROPFILES = 0x0233;

    [LibraryImport("shell32.dll")]
    internal static partial void DragAcceptFiles(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    internal static partial uint DragQueryFileW(nint hDrop, uint fileIndex, char* fileName, uint fileNameLength);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DragQueryPoint(nint hDrop, out POINT point);

    [LibraryImport("shell32.dll")]
    internal static partial void DragFinish(nint hDrop);
}
