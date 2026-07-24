using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// A native image backed by a 32bpp top-down DIB section (an <c>HBITMAP</c>). Constructed decoder-free
/// from ARGB pixels (0xAARRGGBB): the channels are premultiplied and stored in the DIB's native BGRA
/// byte order so <c>AlphaBlend</c> can composite it with correct per-pixel alpha. The handle is owned by
/// this instance and released on <see cref="Dispose"/>.
/// </summary>
internal sealed unsafe class Win32Image : IImage
{
    private nint _bitmap;

    /// <summary>Creates the DIB section and fills it with premultiplied pixels from <paramref name="argb"/>.</summary>
    public Win32Image(int width, int height, ReadOnlySpan<int> argb)
    {
        this.Width = width;
        this.Height = height;

        if (width <= 0 || height <= 0)
            return;

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = width,

                // Negative height => a top-down DIB whose first row is the top of the image.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };

        this._bitmap = NativeMethods.CreateDIBSection(0, in info, NativeMethods.DIB_RGB_COLORS, out var bits, 0, 0);
        if (this._bitmap == 0 || bits == 0)
            return;

        this._bits = bits;
        var pixelCount = width * height;
        var destination = new Span<uint>((void*)bits, pixelCount);
        var count = Math.Min(pixelCount, argb.Length);
        for (var i = 0; i < count; ++i)
            destination[i] = Premultiply(unchecked((uint)argb[i]));
    }

    private nint _bits;
    private Win32Image? _disabled;

    /// <inheritdoc/>
    public IImage? DisabledImage => this._bitmap == 0 || this._bits == 0 ? null : (this._disabled ??= this.CreateGrayscale());

    /// <summary>Realises a greyscale sibling for the disabled look, un-premultiplying the DIB's own
    /// pixels so no separate source copy is retained.</summary>
    private Win32Image CreateGrayscale()
    {
        var argb = new int[this.Width * this.Height];
        var source = new Span<uint>((void*)this._bits, argb.Length);
        for (var i = 0; i < argb.Length; ++i)
        {
            var p = source[i];
            var a = (p >> 24) & 0xFF;
            uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
            if (a > 0) // undo the premultiplication before weighting the channels
            {
                r = r * 255 / a;
                g = g * 255 / a;
                b = b * 255 / a;
            }

            var lum = ((r * 77) + (g * 150) + (b * 29)) >> 8;
            argb[i] = unchecked((int)((a << 24) | (lum << 16) | (lum << 8) | lum));
        }

        return new Win32Image(this.Width, this.Height, argb);
    }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <summary>The underlying DIB section handle, or 0 if creation failed / after disposal.</summary>
    internal nint Handle => this._bitmap;

    /// <summary>
    /// Premultiplies an <c>0xAARRGGBB</c> pixel's color channels by its alpha. The little-endian layout
    /// of the returned <see cref="uint"/> is B, G, R, A in memory — exactly the BGRA order a Windows DIB
    /// (and <c>AlphaBlend</c> with <c>AC_SRC_ALPHA</c>) expects.
    /// </summary>
    private static uint Premultiply(uint argb)
    {
        var a = (argb >> 24) & 0xFF;
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;

        r = r * a / 255;
        g = g * a / 255;
        b = b * a / 255;

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this._disabled?.Dispose();
        this._disabled = null;
        this._bits = 0; // the bits belong to the DIB section freed below

        if (this._bitmap == 0)
            return;

        NativeMethods.DeleteObject(this._bitmap);
        this._bitmap = 0;
    }
}
