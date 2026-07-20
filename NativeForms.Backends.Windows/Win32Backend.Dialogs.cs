using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The native common dialogs: <c>MessageBoxW</c> and the classic COMDLG32/SHELL32 pickers. Each call
/// is application-modal and blocks on the calling thread; COMDLG32 pumps its own message loop, so no
/// managed loop nesting is involved. The <see cref="Font"/>/<see cref="Color"/> conversions live
/// here too, keeping the core seam purely managed.
/// </summary>
public sealed partial class Win32Backend
{
    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="MessageBoxButtons"/> and <see cref="MessageBoxIcon"/> reuse the <c>MB_*</c> flag
    /// values and <see cref="DialogResult"/> the <c>ID*</c> return ids, so both directions map by cast.
    /// </remarks>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
    {
        var ownerHandle = (owner as Win32ControlPeer)?.Handle ?? 0;
        var pressed = NativeMethods.MessageBoxW(ownerHandle, text, caption, (uint)buttons | (uint)icon);
        return pressed <= 0 ? DialogResult.None : (DialogResult)pressed;
    }

    /// <inheritdoc/>
    public unsafe string[]? ShowFileDialog(in FileDialogOptions options)
    {
        if (options.Kind == FileDialogKind.SelectFolder)
            return ShowFolderDialog(in options);

        // Explorer-style multiselect returns "dir NUL name NUL name NUL NUL", so the buffer must be
        // generous; 32k characters is the documented practical maximum for the dialog.
        const int BufferChars = 32768;
        var file = new char[BufferChars];
        var seed = options.FileName;
        if (!string.IsNullOrEmpty(seed) && seed.Length < BufferChars)
            seed.CopyTo(file);

        var filter = BuildFilterString(options.Filters);

        fixed (char* filterPtr = filter)
        fixed (char* filePtr = file)
        fixed (char* dirPtr = NullIfEmpty(options.InitialDirectory))
        fixed (char* titlePtr = NullIfEmpty(options.Title))
        {
            var ofn = new NativeMethods.OPENFILENAMEW
            {
                lStructSize = (uint)sizeof(NativeMethods.OPENFILENAMEW),
                lpstrFilter = (nint)filterPtr,
                nFilterIndex = (uint)Math.Max(1, options.FilterIndex),
                lpstrFile = (nint)filePtr,
                nMaxFile = BufferChars,
                lpstrInitialDir = (nint)dirPtr,
                lpstrTitle = (nint)titlePtr,
                Flags = options.Kind == FileDialogKind.Save
                    ? NativeMethods.OFN_EXPLORER | NativeMethods.OFN_HIDEREADONLY | NativeMethods.OFN_PATHMUSTEXIST | NativeMethods.OFN_OVERWRITEPROMPT
                    : NativeMethods.OFN_EXPLORER | NativeMethods.OFN_HIDEREADONLY | NativeMethods.OFN_PATHMUSTEXIST | NativeMethods.OFN_FILEMUSTEXIST
                        | (options.Multiselect ? NativeMethods.OFN_ALLOWMULTISELECT : 0),
            };

            var confirmed = options.Kind == FileDialogKind.Save
                ? NativeMethods.GetSaveFileNameW(ref ofn)
                : NativeMethods.GetOpenFileNameW(ref ofn);

            return confirmed ? ParseFileBuffer(file) : null;
        }
    }

    /// <inheritdoc/>
    public unsafe Color? ShowColorDialog(Color color)
    {
        // The dialog insists on writable custom-color slots even when the caller ignores them.
        var custom = stackalloc uint[16];
        var cc = new NativeMethods.CHOOSECOLORW
        {
            lStructSize = (uint)sizeof(NativeMethods.CHOOSECOLORW),
            rgbResult = (uint)(color.R | color.G << 8 | color.B << 16),
            lpCustColors = (nint)custom,
            Flags = NativeMethods.CC_RGBINIT | NativeMethods.CC_FULLOPEN,
        };

        if (!NativeMethods.ChooseColorW(ref cc))
            return null;

        return Color.FromArgb((byte)cc.rgbResult, (byte)(cc.rgbResult >> 8), (byte)(cc.rgbResult >> 16));
    }

    /// <inheritdoc/>
    public unsafe Font? ShowFontDialog(Font font)
    {
        var dpi = 96;
        var hdc = NativeMethods.GetDC(0);
        if (hdc != 0)
        {
            var queried = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
            NativeMethods.ReleaseDC(0, hdc);
            if (queried > 0)
                dpi = queried;
        }

        var lf = new NativeMethods.LOGFONTW
        {
            lfHeight = -(int)Math.Round(font.SizeInPoints * dpi / 72.0),
            lfWeight = (font.Style & FontStyle.Bold) != 0 ? NativeMethods.FW_BOLD : NativeMethods.FW_NORMAL,
            lfItalic = (font.Style & FontStyle.Italic) != 0 ? (byte)1 : (byte)0,
            lfUnderline = (font.Style & FontStyle.Underline) != 0 ? (byte)1 : (byte)0,
        };
        var face = font.Family.AsSpan(0, Math.Min(font.Family.Length, 31));
        face.CopyTo(new Span<char>(lf.lfFaceName, 31));

        var cf = new NativeMethods.CHOOSEFONTW
        {
            lStructSize = (uint)sizeof(NativeMethods.CHOOSEFONTW),
            lpLogFont = (nint)(&lf),
            Flags = NativeMethods.CF_SCREENFONTS | NativeMethods.CF_INITTOLOGFONTSTRUCT | NativeMethods.CF_EFFECTS,
        };

        if (!NativeMethods.ChooseFontW(ref cf))
            return null;

        var style = FontStyle.Regular;
        if (lf.lfWeight >= NativeMethods.FW_BOLD)
            style |= FontStyle.Bold;
        if (lf.lfItalic != 0)
            style |= FontStyle.Italic;
        if (lf.lfUnderline != 0)
            style |= FontStyle.Underline;

        var family = new string((char*)lf.lfFaceName);
        return new Font(
            family.Length == 0 ? font.Family : family,
            cf.iPointSize > 0 ? cf.iPointSize / 10f : font.SizeInPoints,
            style);
    }

    /// <summary>Shows the shell folder browser. The initial location is not seeded — that needs the
    /// <c>BFFM_INITIALIZED</c> callback, deliberately left out to keep this hook-free.</summary>
    private static unsafe string[]? ShowFolderDialog(in FileDialogOptions options)
    {
        const int MaxPath = 260;
        var display = stackalloc char[MaxPath];

        fixed (char* titlePtr = NullIfEmpty(options.Title))
        {
            var bi = new NativeMethods.BROWSEINFOW
            {
                pszDisplayName = (nint)display,
                lpszTitle = (nint)titlePtr,
                ulFlags = NativeMethods.BIF_RETURNONLYFSDIRS | NativeMethods.BIF_NEWDIALOGSTYLE,
            };

            var pidl = NativeMethods.SHBrowseForFolderW(ref bi);
            if (pidl == 0)
                return null;

            try
            {
                var path = stackalloc char[MaxPath];
                return NativeMethods.SHGetPathFromIDListW(pidl, path) ? [new string(path)] : null;
            }
            finally
            {
                NativeMethods.CoTaskMemFree(pidl);
            }
        }
    }

    /// <summary>
    /// Encodes filter pairs into the COMDLG32 wire format — <c>name\0patterns\0…\0\0</c> — as a
    /// pinnable char array, or <see langword="null"/> for no filter.
    /// </summary>
    private static char[]? BuildFilterString(FileDialogFilter[] filters)
    {
        if (filters is not { Length: > 0 })
            return null;

        var length = 1; // trailing second NUL
        foreach (var filter in filters)
            length += filter.Name.Length + filter.Patterns.Length + 2;

        var result = new char[length];
        var offset = 0;
        foreach (var filter in filters)
        {
            filter.Name.CopyTo(result.AsSpan(offset));
            offset += filter.Name.Length + 1;
            filter.Patterns.CopyTo(result.AsSpan(offset));
            offset += filter.Patterns.Length + 1;
        }

        return result;
    }

    /// <summary>
    /// Decodes the dialog's out buffer: a single NUL-terminated path, or — for a multiselect —
    /// the directory followed by NUL-separated file names, ending in a double NUL.
    /// </summary>
    private static string[] ParseFileBuffer(char[] buffer)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < buffer.Length; ++i)
        {
            if (buffer[i] != '\0')
                continue;

            if (i == start)
                break;

            parts.Add(new string(buffer, start, i - start));
            start = i + 1;
        }

        if (parts.Count <= 1)
            return [.. parts];

        var directory = parts[0];
        var result = new string[parts.Count - 1];
        for (var i = 1; i < parts.Count; ++i)
            result[i - 1] = Path.Combine(directory, parts[i]);

        return result;
    }

    /// <summary>Maps an empty option string to <see langword="null"/> so pinning yields a NULL pointer.</summary>
    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
