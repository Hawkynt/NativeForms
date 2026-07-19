using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Backend-independent icon storage shared by list, tree, combo and toolbar controls. Images are
/// added as raw 32-bit ARGB pixels long before any backend exists — typically while a form is being
/// constructed — and are materialized into native <see cref="IImage"/>s lazily, the first time a
/// realized control paints them. All images share one fixed <see cref="ImageSize"/>, exactly like
/// its Windows Forms namesake.
/// </summary>
public sealed class ImageList : IDisposable
{
    private readonly List<int[]> _pixels = [];
    private readonly List<IImage?> _realized = [];
    private IPlatformBackend? _backend;

    /// <summary>Creates a list whose images are all <paramref name="imageSize"/> pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    public ImageList(Size imageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(imageSize.Width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(imageSize.Height, 0);
        this.ImageSize = imageSize;
    }

    /// <summary>Creates a list of square <paramref name="edgeLength"/>-pixel images.</summary>
    public ImageList(int edgeLength) : this(new Size(edgeLength, edgeLength)) { }

    /// <summary>The fixed pixel size every image in this list has.</summary>
    public Size ImageSize { get; }

    /// <summary>The number of images stored.</summary>
    public int Count => _pixels.Count;

    /// <summary>
    /// Adds an image from row-major 32-bit ARGB pixels and returns its index.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The pixel count is not <c>ImageSize.Width * ImageSize.Height</c>.
    /// </exception>
    public int Add(ReadOnlySpan<int> argb)
    {
        var expected = this.ImageSize.Width * this.ImageSize.Height;
        if (argb.Length != expected)
            throw new ArgumentException($"Expected {expected} pixels ({this.ImageSize.Width}×{this.ImageSize.Height}), got {argb.Length}.", nameof(argb));

        _pixels.Add(argb.ToArray());
        _realized.Add(null);
        return _pixels.Count - 1;
    }

    /// <summary>
    /// Adds a copy of the image at <paramref name="baseIndex"/> with a badge composed onto it —
    /// small status overlays like "modified" or "locked" — and returns the new entry's index. The
    /// badge pixels (row-major 32-bit ARGB, <paramref name="badgeWidth"/>×<paramref name="badgeHeight"/>)
    /// are blended over the base with a straight alpha-over: opaque badge pixels overwrite, fully
    /// transparent ones preserve the base. <paramref name="corner"/> anchors the badge within the
    /// image, bottom-right by default. The base entry stays untouched.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="baseIndex"/> is out of range, or the badge is empty or larger than
    /// <see cref="ImageSize"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel count is not <c>badgeWidth * badgeHeight</c>.
    /// </exception>
    public int AddBadged(int baseIndex, ReadOnlySpan<int> badgeArgb, int badgeWidth, int badgeHeight, ContentAlignment corner = ContentAlignment.BottomRight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(baseIndex, _pixels.Count);
        var width = this.ImageSize.Width;
        var height = this.ImageSize.Height;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(badgeWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(badgeHeight, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(badgeWidth, width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(badgeHeight, height);
        if (badgeArgb.Length != badgeWidth * badgeHeight)
            throw new ArgumentException($"Expected {badgeWidth * badgeHeight} pixels ({badgeWidth}×{badgeHeight}), got {badgeArgb.Length}.", nameof(badgeArgb));

        var left = corner switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => (width - badgeWidth) / 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => width - badgeWidth,
            _ => 0,
        };
        var top = corner switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => (height - badgeHeight) / 2,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => height - badgeHeight,
            _ => 0,
        };

        var pixels = (int[])_pixels[baseIndex].Clone();
        for (var y = 0; y < badgeHeight; ++y)
            for (var x = 0; x < badgeWidth; ++x)
            {
                var target = ((top + y) * width) + left + x;
                pixels[target] = BlendOver(badgeArgb[(y * badgeWidth) + x], pixels[target]);
            }

        _pixels.Add(pixels);
        _realized.Add(null);
        return _pixels.Count - 1;
    }

    /// <summary>Composes one straight-alpha ARGB source pixel over a destination pixel (Porter-Duff
    /// "over" in integer math). The fast paths keep opaque and transparent badges exact.</summary>
    private static int BlendOver(int source, int destination)
    {
        var sourceAlpha = (int)((uint)source >> 24);
        if (sourceAlpha == 0xFF)
            return source;

        if (sourceAlpha == 0)
            return destination;

        var inverse = 0xFF - sourceAlpha;
        var destinationAlpha = (int)((uint)destination >> 24);
        var alpha = sourceAlpha + (destinationAlpha * inverse / 0xFF);
        if (alpha == 0)
            return 0;

        var red = BlendChannel((source >> 16) & 0xFF, (destination >> 16) & 0xFF, sourceAlpha, destinationAlpha, inverse, alpha);
        var green = BlendChannel((source >> 8) & 0xFF, (destination >> 8) & 0xFF, sourceAlpha, destinationAlpha, inverse, alpha);
        var blue = BlendChannel(source & 0xFF, destination & 0xFF, sourceAlpha, destinationAlpha, inverse, alpha);
        return (alpha << 24) | (red << 16) | (green << 8) | blue;
    }

    /// <summary>One channel of the alpha-over blend, un-premultiplied by the result alpha.</summary>
    private static int BlendChannel(int source, int destination, int sourceAlpha, int destinationAlpha, int inverse, int alpha)
        => ((source * sourceAlpha) + (destination * destinationAlpha * inverse / 0xFF)) / alpha;

    /// <summary>The raw stored pixels of one entry — the seam badge composition is tested through.</summary>
    internal int[] GetPixels(int index) => _pixels[index];

    /// <summary>Removes all images and disposes any realized native bitmaps.</summary>
    public void Clear()
    {
        this.DisposeRealized();
        _pixels.Clear();
        _realized.Clear();
    }

    /// <summary>
    /// The native bitmap for <paramref name="index"/>, created against <paramref name="backend"/> on
    /// first use and cached. A backend change (only possible in tests) drops the previous cache.
    /// </summary>
    internal IImage GetImage(int index, IPlatformBackend backend)
    {
        if (!ReferenceEquals(_backend, backend))
        {
            this.DisposeRealized();
            _backend = backend;
        }

        return _realized[index] ??= backend.CreateImage(this.ImageSize.Width, this.ImageSize.Height, _pixels[index]);
    }

    /// <summary>Disposes all realized native bitmaps; the pixel data stays usable.</summary>
    public void Dispose()
    {
        this.DisposeRealized();
        _backend = null;
    }

    private void DisposeRealized()
    {
        for (var i = 0; i < _realized.Count; ++i)
        {
            _realized[i]?.Dispose();
            _realized[i] = null;
        }
    }
}
