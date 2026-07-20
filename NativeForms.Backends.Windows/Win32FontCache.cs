using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Realizes <see cref="Font"/> descriptors into GDI <c>HFONT</c>s, one per distinct (spec, DPI)
/// pair, cached for the process lifetime. Both the paint path (<see cref="Win32Graphics"/>) and the
/// widget path (<c>WM_SETFONT</c>) draw from the same cache, so a font is created once and never
/// per frame; the handles are shared and must never be deleted by callers. UI-thread only, like all
/// USER32/GDI32 access in this backend.
/// </summary>
internal static class Win32FontCache
{
    private static readonly Dictionary<(Font Font, int Dpi), nint> _fonts = new();

    private static int _screenDpi;

    /// <summary>The vertical DPI of the primary screen, read once and cached.</summary>
    internal static int ScreenDpi
    {
        get
        {
            if (_screenDpi != 0)
                return _screenDpi;

            var hdc = NativeMethods.GetDC(0);
            var dpi = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
            NativeMethods.ReleaseDC(0, hdc);
            return _screenDpi = dpi > 0 ? dpi : 96;
        }
    }

    /// <summary>The shared <c>HFONT</c> for a descriptor at the given DPI (0 on GDI failure).</summary>
    internal static nint Get(Font font, int dpi)
    {
        if (_fonts.TryGetValue((font, dpi), out var handle))
            return handle;

        handle = Create(font, dpi);
        if (handle != 0)
            _fonts[(font, dpi)] = handle;

        return handle;
    }

    /// <summary>Creates the GDI font: point size scaled to the DPI, style flags mapped onto weight/italic/underline/strikeout.</summary>
    private static nint Create(Font font, int dpi)
    {
        var height = -NativeMethods.MulDiv((int)Math.Round(font.SizeInPoints), dpi, 72);
        var weight = (font.Style & FontStyle.Bold) != 0 ? NativeMethods.FW_BOLD : NativeMethods.FW_NORMAL;
        var italic = (font.Style & FontStyle.Italic) != 0 ? 1u : 0u;
        var underline = (font.Style & FontStyle.Underline) != 0 ? 1u : 0u;
        var strikeout = (font.Style & FontStyle.Strikeout) != 0 ? 1u : 0u;

        return NativeMethods.CreateFontW(
            height,
            0,
            0,
            0,
            weight,
            italic,
            underline,
            strikeout,
            NativeMethods.DEFAULT_CHARSET,
            0,
            0,
            0,
            0,
            font.Family);
    }
}
