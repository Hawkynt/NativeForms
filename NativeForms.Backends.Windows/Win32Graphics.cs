using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// A GDI-backed <see cref="IGraphics"/> valid for the duration of one <c>WM_PAINT</c>. It borrows the
/// paint <c>HDC</c> (it does not own it); the owning canvas peer keeps one instance alive and
/// <see cref="Bind"/>s it to each paint's DC, so a steady-state repaint allocates no managed memory.
/// Pens, brushes and fonts come from small process-wide caches keyed by color/thickness/font instead
/// of being created and destroyed per call — the GDI-object churn would otherwise dominate the paint
/// path. Clipping is layered on the DC's own save/restore stack, mirrored by <see cref="_clipStack"/>
/// so <see cref="PopClip"/> restores the matching state.
/// </summary>
internal sealed class Win32Graphics : IGraphics
{
    /// <summary>The cache ceiling; reaching it flushes everything (a paint immediately re-primes the
    /// handful of theme entries, so the flush is a rare, cheap safety valve against unbounded growth).</summary>
    private const int _CacheLimit = 64;

    // GDI objects are process-global (not DC-bound), so one UI-thread cache serves every DC.
    private static readonly Dictionary<uint, nint> _brushes = new();
    private static readonly Dictionary<ulong, nint> _pens = new();
    private static readonly Dictionary<FontKey, nint> _fonts = new();

    /// <summary>The identity a realized <c>HFONT</c> is cached under.</summary>
    private readonly record struct FontKey(string Family, float SizeInPoints, FontStyle Style, int Dpi);

    private nint _hdc;
    private int _dpi;
    private readonly Stack<int> _clipStack = new();

    /// <summary>Wraps a paint device context, caching its vertical DPI for font sizing.</summary>
    public Win32Graphics(nint hdc) => this.Bind(hdc);

    /// <summary>Rebinds the instance to the next paint's device context — the peer-side reuse hook.</summary>
    internal void Bind(nint hdc)
    {
        this._hdc = hdc;
        var dpi = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
        this._dpi = dpi > 0 ? dpi : 96;
        this._clipStack.Clear();
    }

    /// <inheritdoc/>
    public void FillRectangle(Color color, Rectangle bounds)
    {
        var brush = GetBrush(color);
        if (brush == 0)
            return;

        var rect = ToRect(bounds);
        NativeMethods.FillRect(this._hdc, in rect, brush);
    }

    /// <inheritdoc/>
    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
    {
        if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var brush = GetBrush(color);
        if (brush == 0)
            return;

        var t = Math.Min(thickness, Math.Min(bounds.Width, bounds.Height));

        // Four filled edges honor an arbitrary thickness without a pen's centered-stroke quirks.
        FillEdge(bounds.X, bounds.Y, bounds.Width, t);                                   // top
        FillEdge(bounds.X, bounds.Bottom - t, bounds.Width, t);                          // bottom
        FillEdge(bounds.X, bounds.Y + t, t, bounds.Height - 2 * t);                      // left
        FillEdge(bounds.Right - t, bounds.Y + t, t, bounds.Height - 2 * t);             // right

        void FillEdge(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return;

            var rect = new NativeMethods.RECT { left = x, top = y, right = x + w, bottom = y + h };
            NativeMethods.FillRect(this._hdc, in rect, brush);
        }
    }

    /// <inheritdoc/>
    public void FillEllipse(Color color, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // A matching pen paints the boundary so the whole inscribed ellipse is covered.
        var brush = GetBrush(color);
        var pen = GetPen(color, 1);
        if (brush == 0 || pen == 0)
            return;

        var oldBrush = NativeMethods.SelectObject(this._hdc, brush);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        NativeMethods.Ellipse(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        NativeMethods.SelectObject(this._hdc, oldPen);
        NativeMethods.SelectObject(this._hdc, oldBrush);
    }

    /// <inheritdoc/>
    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
    {
        if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var pen = GetPen(color, thickness);
        if (pen == 0)
            return;

        // A stock hollow brush leaves the interior untouched — only the outline is stroked.
        var nullBrush = NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        var oldBrush = NativeMethods.SelectObject(this._hdc, nullBrush);
        NativeMethods.Ellipse(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        NativeMethods.SelectObject(this._hdc, oldBrush);
        NativeMethods.SelectObject(this._hdc, oldPen);
    }

    /// <inheritdoc/>
    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        radius = ClampRadius(radius, bounds);
        if (radius <= 0)
        {
            this.FillRectangle(color, bounds);
            return;
        }

        // A matching pen covers the boundary, exactly like FillEllipse.
        var brush = GetBrush(color);
        var pen = GetPen(color, 1);
        if (brush == 0 || pen == 0)
            return;

        var oldBrush = NativeMethods.SelectObject(this._hdc, brush);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        NativeMethods.RoundRect(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, 2 * radius, 2 * radius);
        NativeMethods.SelectObject(this._hdc, oldPen);
        NativeMethods.SelectObject(this._hdc, oldBrush);
    }

    /// <inheritdoc/>
    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1)
    {
        if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        radius = ClampRadius(radius, bounds);
        if (radius <= 0)
        {
            this.DrawRectangle(color, bounds, thickness);
            return;
        }

        var pen = GetPen(color, thickness);
        if (pen == 0)
            return;

        var nullBrush = NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        var oldBrush = NativeMethods.SelectObject(this._hdc, nullBrush);
        NativeMethods.RoundRect(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, 2 * radius, 2 * radius);
        NativeMethods.SelectObject(this._hdc, oldBrush);
        NativeMethods.SelectObject(this._hdc, oldPen);
    }

    /// <inheritdoc/>
    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
    {
        var pen = GetPen(color, thickness);
        if (pen == 0)
            return;

        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        NativeMethods.MoveToEx(this._hdc, x1, y1, 0);
        NativeMethods.LineTo(this._hdc, x2, y2);
        NativeMethods.SelectObject(this._hdc, oldPen);
    }

    /// <inheritdoc/>
    public void DrawText(
        string text,
        Font font,
        Color color,
        Rectangle bounds,
        ContentAlignment alignment = ContentAlignment.TopLeft)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var hFont = GetFont(font, this._dpi);
        if (hFont == 0)
            return;

        var oldFont = NativeMethods.SelectObject(this._hdc, hFont);
        NativeMethods.SetBkMode(this._hdc, NativeMethods.TRANSPARENT);
        NativeMethods.SetTextColor(this._hdc, ToColorRef(color));

        var rect = ToRect(bounds);
        NativeMethods.DrawTextW(this._hdc, text, text.Length, ref rect, FormatFor(alignment));

        NativeMethods.SelectObject(this._hdc, oldFont);
    }

    /// <inheritdoc/>
    public Size MeasureText(string text, Font font) => MeasureText(this._hdc, text, font, this._dpi);

    /// <summary>
    /// Measures <paramref name="text"/> in <paramref name="font"/> on an arbitrary DC — shared by the
    /// per-paint instance above and <see cref="Win32Backend.MeasureText"/>, which brings a screen DC.
    /// </summary>
    internal static Size MeasureText(nint hdc, string text, Font font, int dpi)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Empty;

        var hFont = GetFont(font, dpi);
        if (hFont == 0)
            return Size.Empty;

        var oldFont = NativeMethods.SelectObject(hdc, hFont);
        Size result;

        if (text.IndexOf('\n') < 0)
        {
            NativeMethods.GetTextExtentPoint32W(hdc, text, text.Length, out var size);
            result = new Size(size.cx, size.cy);
        }
        else
        {
            var rect = new NativeMethods.RECT();
            NativeMethods.DrawTextW(
                hdc,
                text,
                text.Length,
                ref rect,
                NativeMethods.DT_CALCRECT | NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK | NativeMethods.DT_NOPREFIX);
            result = new Size(rect.right - rect.left, rect.bottom - rect.top);
        }

        NativeMethods.SelectObject(hdc, oldFont);
        return result;
    }

    /// <inheritdoc/>
    public void DrawImage(IImage image, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image is not Win32Image win32Image || win32Image.Handle == 0)
            return;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var memoryDc = NativeMethods.CreateCompatibleDC(this._hdc);
        if (memoryDc == 0)
            return;

        var oldBitmap = NativeMethods.SelectObject(memoryDc, win32Image.Handle);

        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 0xFF,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };

        NativeMethods.AlphaBlend(
            this._hdc,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            memoryDc,
            0,
            0,
            win32Image.Width,
            win32Image.Height,
            blend);

        NativeMethods.SelectObject(memoryDc, oldBitmap);
        NativeMethods.DeleteDC(memoryDc);
    }

    /// <inheritdoc/>
    public void PushClip(Rectangle bounds)
    {
        var saved = NativeMethods.SaveDC(this._hdc);
        this._clipStack.Push(saved);
        NativeMethods.IntersectClipRect(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    /// <inheritdoc/>
    public void PopClip()
    {
        if (this._clipStack.Count == 0)
            return;

        var saved = this._clipStack.Pop();
        NativeMethods.RestoreDC(this._hdc, saved);
    }

    /// <summary>Limits a corner radius to half the rectangle's smaller dimension.</summary>
    private static int ClampRadius(int radius, Rectangle bounds)
        => Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);

    /// <summary>Returns the cached solid brush for a color, creating it on first use.</summary>
    private static nint GetBrush(Color color)
    {
        var key = ToColorRef(color);
        if (_brushes.TryGetValue(key, out var brush))
            return brush;

        TrimCache(_brushes);
        brush = NativeMethods.CreateSolidBrush(key);
        if (brush != 0)
            _brushes[key] = brush;

        return brush;
    }

    /// <summary>Returns the cached solid pen for a color and thickness, creating it on first use.</summary>
    private static nint GetPen(Color color, int thickness)
    {
        var key = ToColorRef(color) | ((ulong)(uint)thickness << 32);
        if (_pens.TryGetValue(key, out var pen))
            return pen;

        TrimCache(_pens);
        pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, thickness, ToColorRef(color));
        if (pen != 0)
            _pens[key] = pen;

        return pen;
    }

    /// <summary>Returns the cached <c>HFONT</c> for a font descriptor at a DPI, realizing it on first use.</summary>
    private static nint GetFont(Font font, int dpi)
    {
        var key = new FontKey(font.Family, font.SizeInPoints, font.Style, dpi);
        if (_fonts.TryGetValue(key, out var hFont))
            return hFont;

        TrimCache(_fonts);
        hFont = CreateFont(font, dpi);
        if (hFont != 0)
            _fonts[key] = hFont;

        return hFont;
    }

    /// <summary>Deletes and forgets every cached GDI object once <paramref name="cache"/> hits the ceiling.</summary>
    private static void TrimCache<TKey>(Dictionary<TKey, nint> cache) where TKey : notnull
    {
        if (cache.Count < _CacheLimit)
            return;

        foreach (var handle in cache.Values)
            NativeMethods.DeleteObject(handle);

        cache.Clear();
    }

    /// <summary>Converts a managed color to a Win32 <c>COLORREF</c> (0x00BBGGRR); alpha is dropped for GDI.</summary>
    private static uint ToColorRef(Color color)
        => (uint)(color.R | (color.G << 8) | (color.B << 16));

    /// <summary>Converts a managed rectangle to a Win32 edge <see cref="NativeMethods.RECT"/>.</summary>
    private static NativeMethods.RECT ToRect(Rectangle bounds)
        => new() { left = bounds.Left, top = bounds.Top, right = bounds.Right, bottom = bounds.Bottom };

    /// <summary>Maps a nine-point alignment to the corresponding <c>DT_*</c> format flags.</summary>
    private static uint FormatFor(ContentAlignment alignment)
    {
        var format = NativeMethods.DT_NOPREFIX;

        switch (alignment)
        {
            case ContentAlignment.TopCenter:
            case ContentAlignment.MiddleCenter:
            case ContentAlignment.BottomCenter:
                format |= NativeMethods.DT_CENTER;
                break;
            case ContentAlignment.TopRight:
            case ContentAlignment.MiddleRight:
            case ContentAlignment.BottomRight:
                format |= NativeMethods.DT_RIGHT;
                break;
            default:
                format |= NativeMethods.DT_LEFT;
                break;
        }

        switch (alignment)
        {
            case ContentAlignment.MiddleLeft:
            case ContentAlignment.MiddleCenter:
            case ContentAlignment.MiddleRight:
                // DT_VCENTER only takes effect on a single line.
                format |= NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE;
                break;
            case ContentAlignment.BottomLeft:
            case ContentAlignment.BottomCenter:
            case ContentAlignment.BottomRight:
                format |= NativeMethods.DT_BOTTOM | NativeMethods.DT_SINGLELINE;
                break;
            default:
                format |= NativeMethods.DT_TOP;
                break;
        }

        return format;
    }

    /// <summary>Realizes a <see cref="Font"/> descriptor into a GDI <c>HFONT</c> sized for the given DPI.</summary>
    private static nint CreateFont(Font font, int dpi)
    {
        var height = -NativeMethods.MulDiv((int)Math.Round(font.SizeInPoints), dpi, 72);
        var weight = (font.Style & FontStyle.Bold) != 0 ? NativeMethods.FW_BOLD : NativeMethods.FW_NORMAL;
        var italic = (font.Style & FontStyle.Italic) != 0 ? 1u : 0u;
        var underline = (font.Style & FontStyle.Underline) != 0 ? 1u : 0u;

        return NativeMethods.CreateFontW(
            height,
            0,
            0,
            0,
            weight,
            italic,
            underline,
            0,
            NativeMethods.DEFAULT_CHARSET,
            0,
            0,
            0,
            0,
            font.Family);
    }
}
