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
