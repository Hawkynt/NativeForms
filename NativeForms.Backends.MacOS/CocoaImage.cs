using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A bitmap the backend owns: the 32-bit ARGB the core handed over, and the <c>CGImage</c> the
/// painter draws it as.
/// </summary>
/// <remarks>
/// The native image is built on the first draw and kept, not minted per frame. A repaint has to
/// allocate nothing (PRD §4), and converting a bitmap costs a colour space, a bitmap context and a
/// pass over every pixel — per frame, per icon, that is the whole cost of a grid full of them. The
/// straight-alpha pixels stay alongside it because they are what a second conversion would need and
/// what a greyed sibling would be computed from.
/// </remarks>
internal sealed partial class CocoaImage(int width, int height, ReadOnlySpan<int> argb) : IImage
{
    /// <summary>The pixels, row-major, as 0xAARRGGBB.</summary>
    internal int[] Pixels { get; } = argb.ToArray();

    private nint _handle;

    /// <summary>Whether <see cref="_handle"/> has been settled, so a refusal is not retried per frame.</summary>
    private bool _converted;

    /// <inheritdoc/>
    public int Width { get; } = width;

    /// <inheritdoc/>
    public int Height { get; } = height;

    /// <summary>
    /// The <c>CGImage</c> for these pixels, or zero if CoreGraphics declined. Built once: the first
    /// caller pays, every later frame reads the field.
    /// </summary>
    internal nint Handle
    {
        get
        {
            if (_converted)
                return _handle;

            _converted = true;
            return _handle = CreateCGImage(this.Width, this.Height, this.Pixels);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_handle == 0)
            return;

        CocoaNative.CGImageRelease(_handle);
        _handle = 0;

        // Left marked as converted, so a draw after a dispose paints nothing instead of quietly
        // rebuilding the image the caller just gave up.
        _converted = true;
    }

    /// <summary>
    /// Builds a <c>CGImage</c> from 32-bit ARGB pixels, or zero. The caller owns the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through a bitmap context rather than <c>CGImageCreate</c>, which takes twelve arguments and
    /// would put half of them on the stack: Apple's AArch64 ABI packs stack arguments to their natural
    /// size instead of a slot each, so a signature that is merely plausible reads the wrong bytes and
    /// answers something that looks like an image. Seven arguments all fit in registers.
    /// </para>
    /// <para>
    /// The context is asked to allocate its own pixels. A managed buffer would have to stay pinned for
    /// as long as CoreGraphics might read it, and how long that is is not something the caller can know.
    /// </para>
    /// <para>
    /// The alpha is premultiplied on the way in because CoreGraphics has no eight-bit format that is
    /// not — <c>kCGImageAlphaFirst</c> is rejected outright by a bitmap context — and the toolkit's
    /// pixels are straight. Doing it here rather than pretending is what keeps a half-transparent icon
    /// from coming out too bright.
    /// </para>
    /// <para>
    /// The row stride is read back rather than assumed. A bitmap context is free to answer with rows
    /// wider than the width asked for — alignment is the reason it would — and writing tightly packed
    /// rows into a padded buffer shears the picture diagonally, one row further off than the last.
    /// </para>
    /// </remarks>
    internal static unsafe nint CreateCGImage(int width, int height, ReadOnlySpan<int> argb)
    {
        if (width <= 0 || height <= 0 || argb.Length < width * height)
            return 0;

        var space = CocoaNative.CGColorSpaceCreateDeviceRGB();
        if (space == 0)
            return 0;

        // kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little: alpha, red, green, blue as one
        // little-endian word, which is exactly how the core's 0xAARRGGBB integers already sit in memory.
        const uint format = 2 | (2u << 12);

        var context = CocoaNative.CGBitmapContextCreate(0, width, height, 8, width * 4, space, format);
        CocoaNative.CGColorSpaceRelease(space);
        if (context == 0)
            return 0;

        var rows = (byte*)CocoaNative.CGBitmapContextGetData(context);
        if (rows != null)
        {
            var stride = (int)CocoaNative.CGBitmapContextGetBytesPerRow(context);
            if (stride < width * 4)
                stride = width * 4;

            for (var y = 0; y < height; ++y)
            {
                var pixels = (int*)(rows + ((nint)y * stride));
                var source = argb.Slice(y * width, width);
                for (var x = 0; x < width; ++x)
                    pixels[x] = Premultiplied(source[x]);
            }
        }

        var image = CocoaNative.CGBitmapContextCreateImage(context);
        CocoaNative.CGContextRelease(context);
        return image;
    }

    /// <summary>
    /// Builds an <c>NSImage</c> from 32-bit ARGB pixels, or zero — the shape AppKit wants wherever an
    /// application hands the toolkit an icon rather than a drawing.
    /// </summary>
    internal static nint CreateNSImage(int width, int height, ReadOnlySpan<int> argb)
    {
        var image = CreateCGImage(width, height, argb);
        if (image == 0)
            return 0;

        var allocated = CocoaRuntime.Allocate("NSImage");
        var wrapped = allocated == 0
            ? 0
            : SendImage(
                allocated,
                CocoaRuntime.sel_registerName("initWithCGImage:size:"),
                image,
                new CocoaRuntime.CGSize(width, height));

        CocoaNative.CGImageRelease(image);
        return wrapped;
    }

    /// <summary>One pixel with its colour scaled by its own alpha.</summary>
    private static int Premultiplied(int argb)
    {
        var alpha = (argb >> 24) & 0xFF;
        if (alpha == 0xFF)
            return argb;
        if (alpha == 0)
            return 0;

        var red = (((argb >> 16) & 0xFF) * alpha / 255) & 0xFF;
        var green = (((argb >> 8) & 0xFF) * alpha / 255) & 0xFF;
        var blue = ((argb & 0xFF) * alpha / 255) & 0xFF;
        return (alpha << 24) | (red << 16) | (green << 8) | blue;
    }

    /// <summary>Wraps a <c>CGImage</c> at a stated size: one pointer and two doubles.</summary>
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint SendImage(nint receiver, nint selector, nint image, CocoaRuntime.CGSize size);
}
