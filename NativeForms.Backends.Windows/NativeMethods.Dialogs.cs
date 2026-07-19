using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The common-dialog surface: <c>MessageBoxW</c> plus the classic COMDLG32/SHELL32 dialogs
/// (<c>GetOpenFileNameW</c> family, <c>SHBrowseForFolderW</c>, <c>ChooseColorW</c>,
/// <c>ChooseFontW</c>). The <c>GetOpenFileName</c> family is used deliberately instead of the COM
/// <c>IFileDialog</c>: it is a plain flat API — no vtables, no COM activation — which keeps the AOT
/// story trivial. Struct layouts use natural (64-bit) alignment; COMDLG32's 1-byte packing applies
/// to 32-bit builds only, which .NET no longer targets for NativeAOT.
/// </summary>
internal static partial class NativeMethods
{
    // --- Open/save dialog flags (OPENFILENAMEW.Flags) ---

    /// <summary>Asks before overwriting an existing file (save dialogs).</summary>
    internal const uint OFN_OVERWRITEPROMPT = 0x00000002;

    /// <summary>Hides the legacy read-only checkbox.</summary>
    internal const uint OFN_HIDEREADONLY = 0x00000004;

    /// <summary>Lets the user select more than one file (Explorer-style: dir + names, NUL-separated).</summary>
    internal const uint OFN_ALLOWMULTISELECT = 0x00000200;

    /// <summary>The typed path must exist.</summary>
    internal const uint OFN_PATHMUSTEXIST = 0x00000800;

    /// <summary>The typed file must exist (open dialogs).</summary>
    internal const uint OFN_FILEMUSTEXIST = 0x00001000;

    /// <summary>Requests the Explorer-style dialog (required for the multiselect buffer format).</summary>
    internal const uint OFN_EXPLORER = 0x00080000;

    // --- Folder browser flags (BROWSEINFOW.ulFlags) ---

    /// <summary>Only file-system directories may be picked.</summary>
    internal const uint BIF_RETURNONLYFSDIRS = 0x00000001;

    /// <summary>The resizable modern dialog with drag-drop and a New Folder button.</summary>
    internal const uint BIF_NEWDIALOGSTYLE = 0x00000040;

    // --- Color dialog flags (CHOOSECOLORW.Flags) ---

    /// <summary>Preselect <c>rgbResult</c>.</summary>
    internal const uint CC_RGBINIT = 0x00000001;

    /// <summary>Open with the custom-color pane already expanded.</summary>
    internal const uint CC_FULLOPEN = 0x00000002;

    // --- Font dialog flags (CHOOSEFONTW.Flags) ---

    /// <summary>List screen fonts.</summary>
    internal const uint CF_SCREENFONTS = 0x00000001;

    /// <summary>Preselect the font described by <c>lpLogFont</c>.</summary>
    internal const uint CF_INITTOLOGFONTSTRUCT = 0x00000040;

    /// <summary>Show the underline/strikeout/color effects controls.</summary>
    internal const uint CF_EFFECTS = 0x00000100;

    /// <summary>
    /// The <c>GetOpenFileNameW</c>/<c>GetSaveFileNameW</c> request. All string fields are raw
    /// pointers we pin ourselves — the filter carries embedded NULs, and the file buffer is in/out —
    /// so the struct stays fully blittable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct OPENFILENAMEW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint lStructSize;

        /// <summary>Owner window, or 0.</summary>
        public nint hwndOwner;

        /// <summary>Unused (template instance handle).</summary>
        public nint hInstance;

        /// <summary>Filter pairs as <c>name\0patterns\0…\0\0</c>, or 0 for no filter.</summary>
        public nint lpstrFilter;

        /// <summary>Unused (user-typed custom filter buffer).</summary>
        public nint lpstrCustomFilter;

        /// <summary>Unused (custom filter buffer length).</summary>
        public uint nMaxCustFilter;

        /// <summary>1-based index of the active filter; updated to the user's choice on return.</summary>
        public uint nFilterIndex;

        /// <summary>In: initial file name. Out: the selection (dir + NUL-separated names for multiselect).</summary>
        public nint lpstrFile;

        /// <summary>Capacity of <c>lpstrFile</c> in characters.</summary>
        public uint nMaxFile;

        /// <summary>Unused (file-name-without-path buffer).</summary>
        public nint lpstrFileTitle;

        /// <summary>Unused (its length).</summary>
        public uint nMaxFileTitle;

        /// <summary>Initial directory, or 0 for the system default.</summary>
        public nint lpstrInitialDir;

        /// <summary>Title-bar caption, or 0 for the default.</summary>
        public nint lpstrTitle;

        /// <summary>Behavior flags (<c>OFN_*</c>).</summary>
        public uint Flags;

        /// <summary>Offset of the file name within the returned path.</summary>
        public ushort nFileOffset;

        /// <summary>Offset of the extension within the returned path.</summary>
        public ushort nFileExtension;

        /// <summary>Default extension appended when the user types none, or 0.</summary>
        public nint lpstrDefExt;

        /// <summary>Unused (hook data).</summary>
        public nint lCustData;

        /// <summary>Unused (hook procedure).</summary>
        public nint lpfnHook;

        /// <summary>Unused (template name).</summary>
        public nint lpTemplateName;

        /// <summary>Reserved.</summary>
        public nint pvReserved;

        /// <summary>Reserved.</summary>
        public uint dwReserved;

        /// <summary>Extended flags (unused).</summary>
        public uint FlagsEx;
    }

    /// <summary>The <c>SHBrowseForFolderW</c> request. Callback-free — the initial location hook is not used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BROWSEINFOW
    {
        /// <summary>Owner window, or 0.</summary>
        public nint hwndOwner;

        /// <summary>Root item the browse starts under, or 0 for the desktop.</summary>
        public nint pidlRoot;

        /// <summary>Buffer (MAX_PATH chars) receiving the display name of the chosen folder.</summary>
        public nint pszDisplayName;

        /// <summary>The label shown above the tree, or 0.</summary>
        public nint lpszTitle;

        /// <summary>Behavior flags (<c>BIF_*</c>).</summary>
        public uint ulFlags;

        /// <summary>Unused (browse callback).</summary>
        public nint lpfn;

        /// <summary>Unused (callback data).</summary>
        public nint lParam;

        /// <summary>Receives the image index of the chosen folder.</summary>
        public int iImage;
    }

    /// <summary>The <c>ChooseColorW</c> request. <c>lpCustColors</c> must point at 16 writable COLORREFs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CHOOSECOLORW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint lStructSize;

        /// <summary>Owner window, or 0.</summary>
        public nint hwndOwner;

        /// <summary>Unused (template instance handle).</summary>
        public nint hInstance;

        /// <summary>In: the preselected color (with <see cref="CC_RGBINIT"/>). Out: the chosen COLORREF (<c>0x00BBGGRR</c>).</summary>
        public uint rgbResult;

        /// <summary>Pointer to the 16 custom-color slots the dialog edits.</summary>
        public nint lpCustColors;

        /// <summary>Behavior flags (<c>CC_*</c>).</summary>
        public uint Flags;

        /// <summary>Unused (hook data).</summary>
        public nint lCustData;

        /// <summary>Unused (hook procedure).</summary>
        public nint lpfnHook;

        /// <summary>Unused (template name).</summary>
        public nint lpTemplateName;
    }

    /// <summary>The <c>ChooseFontW</c> request. <c>lpLogFont</c> points at a caller-owned in/out <see cref="LOGFONTW"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CHOOSEFONTW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint lStructSize;

        /// <summary>Owner window, or 0.</summary>
        public nint hwndOwner;

        /// <summary>Unused (printer DC).</summary>
        public nint hDC;

        /// <summary>Pointer to the in/out <see cref="LOGFONTW"/>.</summary>
        public nint lpLogFont;

        /// <summary>Out: the chosen size in tenths of a point.</summary>
        public int iPointSize;

        /// <summary>Behavior flags (<c>CF_*</c>).</summary>
        public uint Flags;

        /// <summary>In/out text color (with <see cref="CF_EFFECTS"/>); unused here.</summary>
        public uint rgbColors;

        /// <summary>Unused (hook data).</summary>
        public nint lCustData;

        /// <summary>Unused (hook procedure).</summary>
        public nint lpfnHook;

        /// <summary>Unused (template name).</summary>
        public nint lpTemplateName;

        /// <summary>Unused (template instance handle).</summary>
        public nint hInstance;

        /// <summary>Unused (style name buffer).</summary>
        public nint lpszStyle;

        /// <summary>Out: the chosen font's type (screen/printer/simulated).</summary>
        public ushort nFontType;

        /// <summary>Explicit alignment padding, exactly as in the SDK header.</summary>
        public ushort ___MISSING_ALIGNMENT__;

        /// <summary>Minimum selectable size in points (with <c>CF_LIMITSIZE</c>); unused here.</summary>
        public int nSizeMin;

        /// <summary>Maximum selectable size in points (with <c>CF_LIMITSIZE</c>); unused here.</summary>
        public int nSizeMax;
    }

    /// <summary>Shows the classic application-modal message box; returns the pressed button's id (<c>IDOK</c> …) or 0 on failure.</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

    /// <summary>Activates a window; used to hand focus back to a dialog's owner.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SetActiveWindow(nint hWnd);

    /// <summary>Shows the Explorer-style open dialog; returns <see langword="false"/> on cancel or error.</summary>
    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

    /// <summary>Shows the Explorer-style save dialog; returns <see langword="false"/> on cancel or error.</summary>
    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSaveFileNameW(ref OPENFILENAMEW ofn);

    /// <summary>Shows the color picker; returns <see langword="false"/> on cancel or error.</summary>
    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChooseColorW(ref CHOOSECOLORW cc);

    /// <summary>Shows the font picker; returns <see langword="false"/> on cancel or error.</summary>
    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChooseFontW(ref CHOOSEFONTW cf);

    /// <summary>Shows the folder browser; returns a PIDL to free with <see cref="CoTaskMemFree"/>, or 0 on cancel.</summary>
    [LibraryImport("shell32.dll")]
    internal static partial nint SHBrowseForFolderW(ref BROWSEINFOW lpbi);

    /// <summary>Converts a PIDL to a file-system path written into the MAX_PATH buffer.</summary>
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SHGetPathFromIDListW(nint pidl, char* pszPath);

    /// <summary>Frees memory the shell allocated (for example the PIDL from <see cref="SHBrowseForFolderW"/>).</summary>
    [LibraryImport("ole32.dll")]
    internal static partial void CoTaskMemFree(nint pv);
}
