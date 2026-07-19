using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The native Win32 theme: colors from <c>GetSysColor</c>, the UI font from the system message font
/// (<c>SPI_GETNONCLIENTMETRICS</c>), and metrics from <c>GetSystemMetrics</c>. Reading the live system
/// values is what lets owner-drawn controls track the user's color scheme and DPI. Every value is
/// snapshotted once at construction; a fresh instance picks up any later theme change.
/// </summary>
internal sealed unsafe class Win32Theme : ITheme
{
    private readonly Font _defaultFont;
    private readonly int _rowHeight;
    private readonly int _scrollBarSize;

    /// <summary>Reads the current desktop palette, font and metrics into an immutable snapshot.</summary>
    public Win32Theme()
    {
        this.WindowBackground = SysColor(NativeMethods.COLOR_BTNFACE);
        this.ControlBackground = SysColor(NativeMethods.COLOR_BTNFACE);
        this.ControlText = SysColor(NativeMethods.COLOR_WINDOWTEXT);
        this.DisabledText = SysColor(NativeMethods.COLOR_GRAYTEXT);
        this.FieldBackground = SysColor(NativeMethods.COLOR_WINDOW);
        this.Accent = SysColor(NativeMethods.COLOR_HIGHLIGHT);
        this.SelectionBackground = SysColor(NativeMethods.COLOR_HIGHLIGHT);
        this.SelectionText = SysColor(NativeMethods.COLOR_HIGHLIGHTTEXT);
        this.Border = SysColor(NativeMethods.COLOR_3DSHADOW);
        this.GridLine = SysColor(NativeMethods.COLOR_ACTIVEBORDER);
        this.HeaderBackground = SysColor(NativeMethods.COLOR_BTNFACE);
        this.HeaderText = SysColor(NativeMethods.COLOR_WINDOWTEXT);

        var dpiY = DeviceDpiY();
        this._defaultFont = ReadMessageFont(dpiY, out var fontPixelHeight);
        this._rowHeight = fontPixelHeight + 10;
        this._scrollBarSize = Metric(NativeMethods.SM_CXVSCROLL, fallback: 16);
    }

    /// <inheritdoc/>
    public Color WindowBackground { get; }

    /// <inheritdoc/>
    public Color ControlBackground { get; }

    /// <inheritdoc/>
    public Color ControlText { get; }

    /// <inheritdoc/>
    public Color DisabledText { get; }

    /// <inheritdoc/>
    public Color FieldBackground { get; }

    /// <inheritdoc/>
    public Color Accent { get; }

    /// <inheritdoc/>
    public Color SelectionBackground { get; }

    /// <inheritdoc/>
    public Color SelectionText { get; }

    /// <inheritdoc/>
    public Color Border { get; }

    /// <inheritdoc/>
    public Color GridLine { get; }

    /// <inheritdoc/>
    public Color HeaderBackground { get; }

    /// <inheritdoc/>
    public Color HeaderText { get; }

    /// <inheritdoc/>
    public Font DefaultFont => this._defaultFont;

    /// <inheritdoc/>
    public int RowHeight => this._rowHeight;

    /// <inheritdoc/>
    public int ScrollBarSize => this._scrollBarSize;

    /// <summary>Reads a system color and converts the <c>COLORREF</c> (0x00BBGGRR) to an opaque <see cref="Color"/>.</summary>
    private static Color SysColor(int index) => ColorRefToColor(NativeMethods.GetSysColor(index));

    /// <summary>Converts a Win32 <c>COLORREF</c> (0x00BBGGRR) to a fully opaque managed color.</summary>
    internal static Color ColorRefToColor(uint colorRef)
    {
        var r = (int)(colorRef & 0xFF);
        var g = (int)((colorRef >> 8) & 0xFF);
        var b = (int)((colorRef >> 16) & 0xFF);
        return Color.FromArgb(0xFF, r, g, b);
    }

    /// <summary>Reads a system metric, substituting <paramref name="fallback"/> when it comes back as 0.</summary>
    private static int Metric(int index, int fallback)
    {
        var value = NativeMethods.GetSystemMetrics(index);
        return value > 0 ? value : fallback;
    }

    /// <summary>Returns the screen's vertical DPI, defaulting to 96 when unavailable.</summary>
    private static int DeviceDpiY()
    {
        var screen = NativeMethods.GetDC(0);
        if (screen == 0)
            return 96;

        var dpi = NativeMethods.GetDeviceCaps(screen, NativeMethods.LOGPIXELSY);
        NativeMethods.ReleaseDC(0, screen);
        return dpi > 0 ? dpi : 96;
    }

    /// <summary>
    /// Builds the default UI font from the system message font, deriving its point size from
    /// <c>lfHeight</c> and <paramref name="dpiY"/>, and reports the corresponding pixel height. Falls
    /// back to Segoe UI 9pt if the query fails or yields an empty face name.
    /// </summary>
    private static Font ReadMessageFont(int dpiY, out int pixelHeight)
    {
        var metrics = default(NativeMethods.NONCLIENTMETRICSW);
        metrics.cbSize = (uint)sizeof(NativeMethods.NONCLIENTMETRICSW);

        if (NativeMethods.SystemParametersInfoW(NativeMethods.SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0))
        {
            var lf = metrics.lfMessageFont;
            var face = new string(lf.lfFaceName);
            var nul = face.IndexOf('\0');
            if (nul >= 0)
                face = face[..nul];

            if (!string.IsNullOrEmpty(face) && lf.lfHeight != 0)
            {
                pixelHeight = Math.Abs(lf.lfHeight);
                var points = NativeMethods.MulDiv(pixelHeight, 72, dpiY);
                var style = FontStyle.Regular;
                if (lf.lfWeight >= NativeMethods.FW_BOLD)
                    style |= FontStyle.Bold;
                if (lf.lfItalic != 0)
                    style |= FontStyle.Italic;
                if (lf.lfUnderline != 0)
                    style |= FontStyle.Underline;

                return new Font(face, points <= 0 ? 9f : points, style);
            }
        }

        pixelHeight = NativeMethods.MulDiv(9, dpiY, 72);
        return new Font("Segoe UI", 9f);
    }
}
