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

        var pixelCount = width * height;
        var destination = new Span<uint>((void*)bits, pixelCount);
        var count = Math.Min(pixelCount, argb.Length);
        for (var i = 0; i < count; ++i)
            destination[i] = Premultiply(unchecked((uint)argb[i]));
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
        if (this._bitmap == 0)
            return;

        NativeMethods.DeleteObject(this._bitmap);
        this._bitmap = 0;
    }
}
