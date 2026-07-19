using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// A GDI-backed <see cref="IGraphics"/> valid for the duration of one <c>WM_PAINT</c>. It borrows the
/// paint <c>HDC</c> (it does not own it) and renders through immediate GDI calls, creating and deleting
/// each pen/brush/font it uses so no GDI object leaks. Clipping is layered on the DC's own save/restore
/// stack, mirrored by <see cref="_clipStack"/> so <see cref="PopClip"/> restores the matching state.
/// </summary>
internal sealed class Win32Graphics : IGraphics
{
    private readonly nint _hdc;
    private readonly int _dpi;
    private readonly Stack<int> _clipStack = new();

    /// <summary>Wraps a paint device context, caching its vertical DPI for font sizing.</summary>
    public Win32Graphics(nint hdc)
    {
        this._hdc = hdc;
        var dpi = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
        this._dpi = dpi > 0 ? dpi : 96;
    }

    /// <inheritdoc/>
    public void FillRectangle(Color color, Rectangle bounds)
    {
        var brush = NativeMethods.CreateSolidBrush(ToColorRef(color));
        if (brush == 0)
            return;

        var rect = ToRect(bounds);
        NativeMethods.FillRect(this._hdc, in rect, brush);
        NativeMethods.DeleteObject(brush);
    }

    /// <inheritdoc/>
    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
    {
        if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var brush = NativeMethods.CreateSolidBrush(ToColorRef(color));
        if (brush == 0)
            return;

        var t = Math.Min(thickness, Math.Min(bounds.Width, bounds.Height));

        // Four filled edges honor an arbitrary thickness without a pen's centered-stroke quirks.
        FillEdge(bounds.X, bounds.Y, bounds.Width, t);                                   // top
        FillEdge(bounds.X, bounds.Bottom - t, bounds.Width, t);                          // bottom
        FillEdge(bounds.X, bounds.Y + t, t, bounds.Height - 2 * t);                      // left
        FillEdge(bounds.Right - t, bounds.Y + t, t, bounds.Height - 2 * t);             // right

        NativeMethods.DeleteObject(brush);

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

        var brush = NativeMethods.CreateSolidBrush(ToColorRef(color));
        if (brush == 0)
            return;

        // A matching pen paints the boundary so the whole inscribed ellipse is covered.
        var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, ToColorRef(color));
        if (pen == 0)
        {
            NativeMethods.DeleteObject(brush);
            return;
        }

        var oldBrush = NativeMethods.SelectObject(this._hdc, brush);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        NativeMethods.Ellipse(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        NativeMethods.SelectObject(this._hdc, oldPen);
        NativeMethods.SelectObject(this._hdc, oldBrush);
        NativeMethods.DeleteObject(pen);
        NativeMethods.DeleteObject(brush);
    }

    /// <inheritdoc/>
    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
    {
        if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, thickness, ToColorRef(color));
        if (pen == 0)
            return;

        // A stock hollow brush leaves the interior untouched — only the outline is stroked.
        var nullBrush = NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH);
        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        var oldBrush = NativeMethods.SelectObject(this._hdc, nullBrush);
        NativeMethods.Ellipse(this._hdc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        NativeMethods.SelectObject(this._hdc, oldBrush);
        NativeMethods.SelectObject(this._hdc, oldPen);
        NativeMethods.DeleteObject(pen);
    }

    /// <inheritdoc/>
    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
    {
        var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, thickness, ToColorRef(color));
        if (pen == 0)
            return;

        var oldPen = NativeMethods.SelectObject(this._hdc, pen);
        NativeMethods.MoveToEx(this._hdc, x1, y1, 0);
        NativeMethods.LineTo(this._hdc, x2, y2);
        NativeMethods.SelectObject(this._hdc, oldPen);
        NativeMethods.DeleteObject(pen);
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

        var hFont = this.CreateFont(font);
        if (hFont == 0)
            return;

        var oldFont = NativeMethods.SelectObject(this._hdc, hFont);
        NativeMethods.SetBkMode(this._hdc, NativeMethods.TRANSPARENT);
        NativeMethods.SetTextColor(this._hdc, ToColorRef(color));

        var rect = ToRect(bounds);
        NativeMethods.DrawTextW(this._hdc, text, text.Length, ref rect, FormatFor(alignment));

        NativeMethods.SelectObject(this._hdc, oldFont);
        NativeMethods.DeleteObject(hFont);
    }

    /// <inheritdoc/>
    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Empty;

        var hFont = this.CreateFont(font);
        if (hFont == 0)
            return Size.Empty;

        var oldFont = NativeMethods.SelectObject(this._hdc, hFont);
        Size result;

        if (text.IndexOf('\n') < 0)
        {
            NativeMethods.GetTextExtentPoint32W(this._hdc, text, text.Length, out var size);
            result = new Size(size.cx, size.cy);
        }
        else
        {
            var rect = new NativeMethods.RECT();
            NativeMethods.DrawTextW(
                this._hdc,
                text,
                text.Length,
                ref rect,
                NativeMethods.DT_CALCRECT | NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK | NativeMethods.DT_NOPREFIX);
            result = new Size(rect.right - rect.left, rect.bottom - rect.top);
        }

        NativeMethods.SelectObject(this._hdc, oldFont);
        NativeMethods.DeleteObject(hFont);
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

    /// <summary>Realizes a <see cref="Font"/> descriptor into a GDI <c>HFONT</c> sized for this DC's DPI.</summary>
    private nint CreateFont(Font font)
    {
        var height = -NativeMethods.MulDiv((int)Math.Round(font.SizeInPoints), this._dpi, 72);
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
