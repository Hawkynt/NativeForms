using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Draws and measures the strings GDI would get wrong — the ones carrying colour glyphs — through
/// Direct2D onto the very device context the rest of the paint path is using (PRD §13).
/// </summary>
/// <remarks>
/// <para>
/// GDI rasterizes one monochrome alpha mask per glyph, so the COLR/CPAL layer table that Segoe UI Emoji
/// carries is invisible to it and an emoji comes out as flat outlines. Direct2D asks DirectWrite for the
/// layers and composites them, which is the whole feature; <c>ID2D1DCRenderTarget</c> is the seam that
/// lets it happen on a GDI HDC, so the canvas peer, the double buffer and the clip stack are untouched.
/// </para>
/// <para>
/// Everything here is best-effort. Direct2D or DirectWrite may be absent, the factories may refuse, the
/// device context may not accept a binding — in every case the caller falls back to GDI and the string
/// renders in monochrome, which is a worse picture and not a broken one. A failure latches, so the
/// attempt is made once per process rather than once per paint.
/// </para>
/// </remarks>
internal static unsafe class Win32ColorText
{
    /// <summary>Set once anything on the Direct2D path fails; from then on every call declines at once.</summary>
    private static bool _unavailable;

    /// <summary>
    /// Whether this path has given up. Read by the real-Win32 test tier, which cannot assert that colour
    /// was used without knowing whether colour was possible — the answer is a property of the machine.
    /// </summary>
    internal static bool Unavailable => _unavailable;

    /// <summary>
    /// How many strings this path has actually drawn. The tier asserts that the count moves for a string
    /// with an emoji in it and does not move for one without, which is the whole divert-or-not contract.
    /// </summary>
    internal static int ColorRuns { get; private set; }

    private static nint _d2dFactory;
    private static nint _writeFactory;

    /// <summary>The DC render target, reused across calls — creating one per string would dominate the cost.</summary>
    private static nint _target;

    /// <summary>The cached brush, recoloured by recreating it when the colour changes.</summary>
    private static nint _brush;
    private static Color _brushColor;

    /// <summary>The cached text format and the font it was built for.</summary>
    private static nint _format;
    private static Font _formatFont;
    private static int _formatDpi;
    private static ContentAlignment _formatAlignment;

    /// <summary>
    /// Draws <paramref name="text"/> in colour, reporting whether it could. A <see langword="false"/>
    /// result means nothing was drawn and the caller should use its own path.
    /// </summary>
    public static bool TryDraw(nint hdc, string text, Font font, int dpi, Color color, Rectangle bounds, ContentAlignment alignment)
    {
        if (_unavailable || hdc == 0 || !EnsureFactories())
            return false;

        var target = EnsureTarget();
        if (target == 0)
            return false;

        // The target is bound to the caller's DC for the duration of this string; the rectangle is the
        // area Direct2D is allowed to touch, in device pixels.
        var clip = new NativeMethods.RECT
        {
            left = bounds.Left,
            top = bounds.Top,
            right = bounds.Right,
            bottom = bounds.Bottom,
        };

        if (Failed(NativeMethods.BindDC(target, hdc, clip)))
            return false;

        var format = EnsureFormat(font, dpi, alignment);
        var brush = EnsureBrush(target, color);
        if (format == 0 || brush == 0)
            return false;

        // BindDC makes the bound rectangle the target's own space, so the text is laid out from its
        // origin rather than from the control's.
        var layout = new NativeMethods.D2D1_RECT_F
        {
            left = 0,
            top = 0,
            right = bounds.Width,
            bottom = bounds.Height,
        };

        NativeMethods.BeginDraw(target);
        fixed (char* chars = text)
            NativeMethods.D2DDrawText(
                target,
                chars,
                (uint)text.Length,
                format,
                layout,
                brush,
                NativeMethods.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT,
                NativeMethods.DWRITE_MEASURING_MODE_GDI_CLASSIC);

        if (Failed(NativeMethods.EndDraw(target)))
            return false;

        ++ColorRuns;
        return true;
    }

    /// <summary>
    /// Measures <paramref name="text"/> with the renderer that would paint it, so hit-testing and layout
    /// agree with what lands on screen. Reports whether it could.
    /// </summary>
    public static bool TryMeasure(string text, Font font, int dpi, out Size size)
    {
        size = Size.Empty;
        if (_unavailable || !EnsureFactories())
            return false;

        var format = EnsureFormat(font, dpi, ContentAlignment.TopLeft);
        if (format == 0)
            return false;

        nint layout = 0;
        try
        {
            fixed (char* chars = text)
                if (Failed(NativeMethods.CreateTextLayout(_writeFactory, chars, (uint)text.Length, format, float.MaxValue, float.MaxValue, out layout))
                    || layout == 0)
                    return false;

            if (Failed(NativeMethods.GetTextMetrics(layout, out var metrics)))
                return false;

            size = new Size((int)Math.Ceiling(metrics.widthIncludingTrailingWhitespace), (int)Math.Ceiling(metrics.height));
            return true;
        }
        finally
        {
            NativeMethods.Release(layout);
        }
    }

    /// <summary>Creates the two factories once, latching unavailability on any failure.</summary>
    private static bool EnsureFactories()
    {
        if (_d2dFactory != 0 && _writeFactory != 0)
            return true;

        try
        {
            if (_d2dFactory == 0
                && Failed(NativeMethods.D2D1CreateFactory(0, NativeMethods.IID_ID2D1Factory, 0, out _d2dFactory)))
                return false;

            if (_writeFactory == 0
                && Failed(NativeMethods.DWriteCreateFactory(0, NativeMethods.IID_IDWriteFactory, out _writeFactory)))
                return false;
        }
        catch (DllNotFoundException)
        {
            // A Windows build without Direct2D or DirectWrite: monochrome text is the answer, forever.
            _unavailable = true;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _unavailable = true;
            return false;
        }

        return _d2dFactory != 0 && _writeFactory != 0;
    }

    /// <summary>Creates the DC render target once.</summary>
    private static nint EnsureTarget()
    {
        if (_target != 0)
            return _target;

        var properties = new NativeMethods.D2D1_RENDER_TARGET_PROPERTIES
        {
            type = 0,                                            // whichever of hardware/software is available
            // B8G8R8A8_UNORM with alpha IGNORE (3, not 2 — 2 is STRAIGHT, which a DC target rejects with
            // WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT). A GDI device context has no alpha to honour anyway.
            pixelFormat = new() { format = 87, alphaMode = 3 },
            dpiX = 0,
            dpiY = 0,
            usage = 2,                                           // GDI_COMPATIBLE, which BindDC requires
            minLevel = 0,
        };

        return Failed(NativeMethods.CreateDCRenderTarget(_d2dFactory, properties, out _target)) ? 0 : _target;
    }

    /// <summary>Builds the text format for a font, reusing the last one when nothing changed.</summary>
    private static nint EnsureFormat(Font font, int dpi, ContentAlignment alignment)
    {
        if (_format != 0 && _formatDpi == dpi && _formatAlignment == alignment && font.Equals(_formatFont))
            return _format;

        NativeMethods.Release(_format);
        _format = 0;

        // DirectWrite takes DIPs, and a DIP is 1/96 inch — the same conversion the GDI font cache makes
        // when it turns a point size into logical units.
        var size = font.SizeInPoints * dpi / 72f;
        if (Failed(NativeMethods.CreateTextFormat(
                _writeFactory,
                font.Family,
                (font.Style & FontStyle.Bold) != 0 ? NativeMethods.DWRITE_FONT_WEIGHT_BOLD : NativeMethods.DWRITE_FONT_WEIGHT_NORMAL,
                (font.Style & FontStyle.Italic) != 0 ? NativeMethods.DWRITE_FONT_STYLE_ITALIC : NativeMethods.DWRITE_FONT_STYLE_NORMAL,
                NativeMethods.DWRITE_FONT_STRETCH_NORMAL,
                size,
                string.Empty,
                out _format)))
            return _format = 0;

        NativeMethods.SetWordWrapping(_format, NativeMethods.DWRITE_WORD_WRAPPING_NO_WRAP);
        NativeMethods.SetTextAlignment(_format, HorizontalOf(alignment));
        NativeMethods.SetParagraphAlignment(_format, VerticalOf(alignment));

        _formatFont = font;
        _formatDpi = dpi;
        _formatAlignment = alignment;
        return _format;
    }

    /// <summary>Builds the brush for a colour, reusing the last one when the colour is unchanged.</summary>
    private static nint EnsureBrush(nint target, Color color)
    {
        if (_brush != 0 && _brushColor == color)
            return _brush;

        NativeMethods.Release(_brush);
        _brush = 0;

        var value = new NativeMethods.D2D1_COLOR_F
        {
            r = color.R / 255f,
            g = color.G / 255f,
            b = color.B / 255f,
            a = color.A / 255f,
        };

        if (Failed(NativeMethods.CreateSolidColorBrush(target, value, out _brush)))
            return _brush = 0;

        _brushColor = color;
        return _brush;
    }

    /// <summary>The DirectWrite horizontal alignment matching one of ours.</summary>
    private static uint HorizontalOf(ContentAlignment alignment)
        => alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => NativeMethods.DWRITE_TEXT_ALIGNMENT_CENTER,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => NativeMethods.DWRITE_TEXT_ALIGNMENT_TRAILING,
            _ => NativeMethods.DWRITE_TEXT_ALIGNMENT_LEADING,
        };

    /// <summary>The DirectWrite vertical alignment matching one of ours.</summary>
    private static uint VerticalOf(ContentAlignment alignment)
        => alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => NativeMethods.DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => NativeMethods.DWRITE_PARAGRAPH_ALIGNMENT_FAR,
            _ => NativeMethods.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
        };

    /// <summary>
    /// Whether an <c>HRESULT</c> failed, latching the whole path off when one does. One failure is enough:
    /// the causes are all environmental, so retrying per string would pay the cost forever for no chance
    /// of a different answer.
    /// </summary>
    private static bool Failed(int hr)
    {
        if (hr >= 0)
            return false;

        _unavailable = true;
        return true;
    }
}
