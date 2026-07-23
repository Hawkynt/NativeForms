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
    private readonly List<string?> _keys = [];
    private readonly List<AnimatedImage?> _animated = [];
    private IPlatformBackend? _backend;

    /// <summary>
    /// Raised as any animated entry's frame advances, so every control drawing from this list can
    /// repaint. A control subscribes when the list is assigned and unsubscribes when it is replaced;
    /// the list keeps the shared <see cref="AnimationClock"/> ticking while it holds an animated entry.
    /// </summary>
    public event EventHandler? FrameChanged;

    private void RaiseFrameChanged() => this.FrameChanged?.Invoke(this, EventArgs.Empty);

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
        _keys.Add(null);
        _animated.Add(null);
        return _pixels.Count - 1;
    }

    /// <summary>
    /// Adds a still or animated <see cref="AnimatedImage"/> as an entry and returns its index — the
    /// same collection interface, so any control that draws from an image list (tree, list, tab, tool
    /// strip …) can carry an animated icon. The list registers an animated entry with the shared
    /// <see cref="AnimationClock"/> and raises <see cref="FrameChanged"/> as it advances; each frame is
    /// realized against the drawing backend and scaled into the icon slot exactly like a still entry.
    /// </summary>
    public int Add(AnimatedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _pixels.Add([]);
        _realized.Add(null);
        _keys.Add(null);
        _animated.Add(image);
        if (image.IsAnimated)
            AnimationClock.Instance.Register(image, image, static () => true, this.RaiseFrameChanged);

        return _pixels.Count - 1;
    }

    /// <summary>Adds an <see cref="AnimatedImage"/> under a lookup <paramref name="key"/>.</summary>
    public int Add(string key, AnimatedImage image) => this.SetKey(this.Add(image), key);

    /// <summary>Adds an image under a lookup <paramref name="key"/> and returns its index — the keyed
    /// counterpart of <see cref="Add(ReadOnlySpan{int})"/>, so controls can reference it by
    /// <c>ImageKey</c> as well as index.</summary>
    public int Add(string key, ReadOnlySpan<int> argb) => this.SetKey(this.Add(argb), key);

    /// <summary>Adds a decoded PNG under a lookup <paramref name="key"/>; see <see cref="AddPng(ReadOnlySpan{byte})"/>.</summary>
    public int AddPng(string key, ReadOnlySpan<byte> png) => this.SetKey(this.AddPng(png), key);

    /// <summary>Adds a decoded ICO under a lookup <paramref name="key"/>; see <see cref="AddIco(ReadOnlySpan{byte})"/>.</summary>
    public int AddIco(string key, ReadOnlySpan<byte> ico) => this.SetKey(this.AddIco(ico), key);

    /// <summary>Records the lookup key for an entry and returns its index.</summary>
    private int SetKey(int index, string? key)
    {
        _keys[index] = key;
        return index;
    }

    /// <summary>
    /// The index of the image stored under <paramref name="key"/>, or <c>-1</c> when the key is empty
    /// or unknown. Keys are matched case-insensitively, exactly like the Windows Forms namesake.
    /// </summary>
    public int IndexOfKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return -1;

        for (var i = 0; i < _keys.Count; ++i)
            if (string.Equals(_keys[i], key, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    /// <summary>Whether an image is stored under <paramref name="key"/>.</summary>
    public bool ContainsKey(string? key) => this.IndexOfKey(key) >= 0;

    /// <summary>
    /// Resolves a control's image reference to a concrete index: an explicit <paramref name="imageIndex"/>
    /// (&gt;= 0) wins; otherwise <paramref name="imageKey"/> is looked up in <paramref name="images"/>.
    /// Returns <c>-1</c> when neither resolves — the shared rule behind every control's <c>ImageKey</c>.
    /// </summary>
    internal static int ResolveIndex(ImageList? images, int imageIndex, string? imageKey)
        => imageIndex >= 0 ? imageIndex : images?.IndexOfKey(imageKey) ?? -1;

    /// <summary>
    /// Adds an image from encoded PNG bytes (the <see cref="ImageDecoder"/> subset: 8-bit
    /// non-interlaced grayscale/RGB/RGBA/palette) and returns its index. A decoded size other than
    /// <see cref="ImageSize"/> is resampled to fit with nearest-neighbor scaling — crisp for the
    /// icon-sized art this list holds, no filtering.
    /// </summary>
    /// <exception cref="FormatException">The bytes are not a PNG in the supported subset.</exception>
    public int AddPng(ReadOnlySpan<byte> png)
    {
        var (width, height, argb) = ImageDecoder.DecodePng(png);
        return this.AddScaled(width, height, argb);
    }

    /// <summary>
    /// Adds an image from encoded ICO bytes, letting the decoder pick the entry closest to
    /// <see cref="ImageSize"/>, and returns its index. A decoded size other than
    /// <see cref="ImageSize"/> is resampled to fit with nearest-neighbor scaling.
    /// </summary>
    /// <exception cref="FormatException">The bytes are not an ICO in the supported subset.</exception>
    public int AddIco(ReadOnlySpan<byte> ico)
    {
        var (width, height, argb) = ImageDecoder.DecodeIco(ico, this.ImageSize.Width);
        return this.AddScaled(width, height, argb);
    }

    /// <summary>Adds decoded pixels, nearest-neighbor-resampling them to <see cref="ImageSize"/> when needed.</summary>
    private int AddScaled(int width, int height, int[] argb)
    {
        var targetWidth = this.ImageSize.Width;
        var targetHeight = this.ImageSize.Height;
        if (width == targetWidth && height == targetHeight)
            return this.Add(argb);

        var scaled = new int[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; ++y)
        {
            var sourceRow = (int)((long)y * height / targetHeight) * width;
            var targetRow = y * targetWidth;
            for (var x = 0; x < targetWidth; ++x)
                scaled[targetRow + x] = argb[sourceRow + (int)((long)x * width / targetWidth)];
        }

        return this.Add(scaled);
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
        _keys.Add(null);
        _animated.Add(null);
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
        _keys.Clear();
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

        // An animated entry realizes its current frame against the backend (the frame caches inside the
        // AnimatedImage); a still entry realizes and caches its single bitmap here.
        if (_animated[index] is { } animated)
            return animated.FrameImage(backend, animated.CurrentFrameIndex(Environment.TickCount64));

        return _realized[index] ??= backend.CreateImage(this.ImageSize.Width, this.ImageSize.Height, _pixels[index]);
    }

    /// <summary>Disposes all realized native bitmaps; the pixel data stays usable.</summary>
    public void Dispose()
    {
        foreach (var animated in _animated)
            if (animated is not null)
                AnimationClock.Instance.Unregister(animated);

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
