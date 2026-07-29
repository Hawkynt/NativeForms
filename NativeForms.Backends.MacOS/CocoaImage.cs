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
internal sealed class CocoaImage(int width, int height, ReadOnlySpan<int> argb) : IImage
{
    /// <summary>The pixels, row-major, as 0xAARRGGBB.</summary>
    internal int[] Pixels { get; } = argb.ToArray();

    /// <inheritdoc/>
    public int Width { get; } = width;

    /// <inheritdoc/>
    public int Height { get; } = height;

    /// <inheritdoc/>
    public void Dispose() { }
}
