using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// A native bitmap backed by a Cairo image surface. Built decoder-free from 32-bit ARGB pixels: the
/// straight-alpha source is premultiplied into a private, unmanaged buffer kept alive for the surface's
/// lifetime, since <c>cairo_image_surface_create_for_data</c> does not copy the pixels it wraps.
/// </summary>
internal sealed class GtkImage : IImage
{
    private nint _surface;
    private nint _buffer;

    /// <summary>Premultiplies the ARGB pixels into a Cairo ARGB32 surface (native-endian 0xAARRGGBB).</summary>
    internal GtkImage(int width, int height, ReadOnlySpan<int> argb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        this.Width = width;
        this.Height = height;

        var stride = NativeMethods.cairo_format_stride_for_width(NativeMethods.CAIRO_FORMAT_ARGB32, width);
        _buffer = Marshal.AllocHGlobal(stride * height);

        unsafe
        {
            var row = (byte*)_buffer;
            for (var y = 0; y < height; ++y)
            {
                var pixel = (uint*)(row + y * stride);
                for (var x = 0; x < width; ++x)
                {
                    var source = unchecked((uint)argb[y * width + x]);
                    var a = (source >> 24) & 0xFF;
                    var r = (source >> 16) & 0xFF;
                    var g = (source >> 8) & 0xFF;
                    var b = source & 0xFF;

                    // Cairo ARGB32 stores premultiplied, native-endian 0xAARRGGBB (little-endian: B,G,R,A).
                    r = r * a / 255;
                    g = g * a / 255;
                    b = b * a / 255;
                    pixel[x] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }

        _surface = NativeMethods.cairo_image_surface_create_for_data(
            _buffer, NativeMethods.CAIRO_FORMAT_ARGB32, width, height, stride);
        NativeMethods.cairo_surface_mark_dirty(_surface);
    }

    /// <inheritdoc />
    public int Width { get; }

    /// <inheritdoc />
    public int Height { get; }

    /// <summary>The underlying Cairo surface handle, drawn by <see cref="GtkGraphics.DrawImage"/>.</summary>
    internal nint Surface => _surface;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_surface != 0)
        {
            NativeMethods.cairo_surface_destroy(_surface);
            _surface = 0;
        }

        if (_buffer != 0)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = 0;
        }
    }
}
