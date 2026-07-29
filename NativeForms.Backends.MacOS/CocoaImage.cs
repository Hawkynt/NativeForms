using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A bitmap the backend owns, held as the 32-bit ARGB the core handed over.
/// </summary>
/// <remarks>
/// Deliberately managed pixels rather than a <c>CGImage</c> for now. Nothing draws yet, so a native
/// bitmap would be an object with no consumer, and creating one is the sort of work that looks like
/// progress while adding none; the pixels are kept in the form the eventual
/// <c>CGBitmapContextCreate</c> wants, so the change is local when drawing arrives.
/// </remarks>
internal sealed partial class CocoaImage(int width, int height, ReadOnlySpan<int> argb) : IImage
{
    /// <summary>The pixels, row-major, as 0xAARRGGBB.</summary>
    internal int[] Pixels { get; } = argb.ToArray();

    /// <inheritdoc/>
    public int Width { get; } = width;

    /// <inheritdoc/>
    public int Height { get; } = height;

    /// <inheritdoc/>
    public void Dispose() { }

    /// <summary>
    /// Builds an <c>NSImage</c> from 32-bit ARGB pixels, or zero — the shape AppKit wants wherever an
    /// application hands the toolkit an icon rather than a drawing.
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
    /// </remarks>
    internal static unsafe nint CreateNSImage(int width, int height, ReadOnlySpan<int> argb)
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

        var pixels = (int*)CocoaNative.CGBitmapContextGetData(context);
        if (pixels != null)
            for (var i = 0; i < width * height; ++i)
                pixels[i] = Premultiplied(argb[i]);

        var image = CocoaNative.CGBitmapContextCreateImage(context);
        CocoaNative.CGContextRelease(context);
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
