using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// A decoded image — still or animated — ready to paint. It keeps the raw frame pixels and lazily
/// realizes each frame's <see cref="IImage"/> against whatever backend paints it. Which frame shows is
/// a pure function of <em>elapsed time</em>, not of a tick counter, so an animation stays in sync
/// whether or not it has been on screen: a control that was hidden and comes back paints the exact
/// frame it would have shown had it never been hidden (the elapsed time is taken modulo the loop).
/// </summary>
public sealed class AnimatedImage : IDisposable
{
    private readonly DecodedImage _decoded;
    private IPlatformBackend? _backend;
    private IImage?[] _images;
    private IImage?[] _grayImages;
    private long _pausedAtTick;   // 0 = running; otherwise the tick the animation was frozen at
    private long _pausedTotalMs;  // total time spent paused, subtracted from the elapsed clock

    /// <summary>Wraps a decoded image.</summary>
    public AnimatedImage(DecodedImage decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        _decoded = decoded;
        _images = new IImage?[decoded.Frames.Count];
        _grayImages = new IImage?[decoded.Frames.Count];
        this.LoopCount = decoded.LoopCount;
        this.StartTick = Environment.TickCount64;
    }

    /// <summary>Decodes bytes of any supported format into an animated image.</summary>
    public static AnimatedImage Decode(ReadOnlySpan<byte> data) => new(ImageDecoder.Decode(data));

    /// <summary>The pixel width, shared by every frame.</summary>
    public int Width => _decoded.Width;

    /// <summary>The pixel height, shared by every frame.</summary>
    public int Height => _decoded.Height;

    /// <summary>The number of frames.</summary>
    public int FrameCount => _decoded.Frames.Count;

    /// <summary>Whether there is more than one frame.</summary>
    public bool IsAnimated => _decoded.IsAnimated;

    /// <summary>How many times the animation plays before it stops on the last frame: 0 loops forever,
    /// 1 plays once (no loop), N plays N times. Defaults to the value the format declared.</summary>
    public int LoopCount { get; set; }

    /// <summary>The monotonic tick the animation's clock started from.</summary>
    internal long StartTick { get; }

    /// <summary>The shortest per-frame delay — the finest cadence this animation needs.</summary>
    internal int ShortestDelayMilliseconds => _decoded.ShortestFrameDelayMilliseconds;

    /// <summary>
    /// The frame index shown <paramref name="elapsedMilliseconds"/> after the start. Time is taken
    /// modulo one loop's duration, so the result is the same whether or not the image was on screen the
    /// whole time; once <see cref="LoopCount"/> loops have elapsed it stays on the final frame.
    /// </summary>
    public int FrameIndexAt(long elapsedMilliseconds)
    {
        var count = _decoded.Frames.Count;
        if (count <= 1 || elapsedMilliseconds <= 0)
            return 0;

        var total = _decoded.TotalDurationMilliseconds;
        if (total <= 0)
            return 0;

        if (this.LoopCount != 0 && elapsedMilliseconds / total >= this.LoopCount)
            return count - 1; // played out — hold the last frame

        var withinLoop = elapsedMilliseconds % total;
        var accumulated = 0L;
        for (var i = 0; i < count; ++i)
        {
            accumulated += Math.Max(1, _decoded.Frames[i].DelayMilliseconds);
            if (withinLoop < accumulated)
                return i;
        }

        return count - 1;
    }

    /// <summary>Whether the animation is currently frozen (the hosting control is disabled).</summary>
    internal bool IsPaused => _pausedAtTick != 0;

    /// <summary>Freezes the animation at its current frame — the elapsed clock stops advancing.</summary>
    internal void Pause(long nowTick)
    {
        if (_pausedAtTick == 0)
            _pausedAtTick = nowTick == 0 ? 1 : nowTick;
    }

    /// <summary>Resumes a frozen animation, continuing from the frame it stopped on (the paused span is
    /// excluded from the elapsed clock).</summary>
    internal void Resume(long nowTick)
    {
        if (_pausedAtTick == 0)
            return;

        _pausedTotalMs += nowTick - _pausedAtTick;
        _pausedAtTick = 0;
    }

    /// <summary>The current frame's index for a monotonic <paramref name="nowTick"/>, holding still
    /// while paused and excluding paused time so a resume continues where it froze.</summary>
    internal int CurrentFrameIndex(long nowTick)
    {
        var reference = _pausedAtTick != 0 ? _pausedAtTick : nowTick;
        return this.FrameIndexAt(reference - this.StartTick - _pausedTotalMs);
    }

    /// <summary>Realizes (and caches) the given frame's <see cref="IImage"/> for a backend.</summary>
    internal IImage FrameImage(IPlatformBackend backend, int index)
    {
        this.EnsureBackend(backend);
        return _images[index] ??= backend.CreateImage(_decoded.Width, _decoded.Height, _decoded.Frames[index].Argb);
    }

    /// <summary>Realizes (and caches) a grayscale version of the given frame — the disabled look.</summary>
    internal IImage FrameImageGray(IPlatformBackend backend, int index)
    {
        this.EnsureBackend(backend);
        return _grayImages[index] ??= backend.CreateImage(_decoded.Width, _decoded.Height, Grayscale(_decoded.Frames[index].Argb));
    }

    /// <inheritdoc/>
    public void Dispose() => this.DisposeImages();

    private void EnsureBackend(IPlatformBackend backend)
    {
        if (ReferenceEquals(_backend, backend))
            return;

        this.DisposeImages();
        _backend = backend;
        _images = new IImage?[_decoded.Frames.Count];
        _grayImages = new IImage?[_decoded.Frames.Count];
    }

    /// <summary>The grayscale transform, exposed for tests (the backend image discards pixels).</summary>
    internal static int[] GrayscaleForTest(int[] argb) => Grayscale(argb);

    /// <summary>Converts ARGB pixels to grayscale (luminance-weighted), keeping the alpha.</summary>
    private static int[] Grayscale(int[] argb)
    {
        var gray = new int[argb.Length];
        for (var i = 0; i < argb.Length; ++i)
        {
            var pixel = argb[i];
            var luminance = (((pixel >> 16) & 0xFF) * 77 + ((pixel >> 8) & 0xFF) * 150 + (pixel & 0xFF) * 29) >> 8;
            gray[i] = (int)((uint)pixel & 0xFF000000) | (luminance << 16) | (luminance << 8) | luminance;
        }

        return gray;
    }

    private void DisposeImages()
    {
        for (var i = 0; i < _images.Length; ++i)
        {
            _images[i]?.Dispose();
            _images[i] = null;
        }

        for (var i = 0; i < _grayImages.Length; ++i)
        {
            _grayImages[i]?.Dispose();
            _grayImages[i] = null;
        }
    }
}
