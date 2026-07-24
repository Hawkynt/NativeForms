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

    private GtkImage? _disabled;

    /// <inheritdoc />
    public IImage? DisabledImage => _disabled ??= this.CreateGrayscale();

    /// <summary>Realises a greyscale sibling for the disabled look, un-premultiplying this surface's own
    /// pixels so no separate source copy has to be kept alive.</summary>
    private GtkImage CreateGrayscale()
    {
        var argb = new int[this.Width * this.Height];
        var stride = NativeMethods.cairo_format_stride_for_width(NativeMethods.CAIRO_FORMAT_ARGB32, this.Width);
        unsafe
        {
            var rows = (byte*)_buffer;
            for (var y = 0; y < this.Height; ++y)
            {
                var pixel = (uint*)(rows + (y * stride));
                for (var x = 0; x < this.Width; ++x)
                {
                    var p = pixel[x];
                    var a = (p >> 24) & 0xFF;
                    uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                    if (a > 0) // undo Cairo's premultiplication before weighting the channels
                    {
                        r = r * 255 / a;
                        g = g * 255 / a;
                        b = b * 255 / a;
                    }

                    var lum = ((r * 77) + (g * 150) + (b * 29)) >> 8;
                    argb[(y * this.Width) + x] = unchecked((int)((a << 24) | (lum << 16) | (lum << 8) | lum));
                }
            }
        }

        return new GtkImage(this.Width, this.Height, argb);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disabled?.Dispose();
        _disabled = null;

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
