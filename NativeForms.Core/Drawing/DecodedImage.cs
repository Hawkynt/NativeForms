namespace Hawkynt.NativeForms.Drawing;

/// <summary>One frame of a decoded image: its row-major 32-bit ARGB pixels (0xAARRGGBB, straight alpha)
/// and how long the frame is shown before the next. A static image is a single frame with delay 0.</summary>
public sealed class ImageFrame(int[] argb, int delayMilliseconds)
{
    /// <summary>The frame's row-major ARGB pixels.</summary>
    public int[] Argb { get; } = argb;

    /// <summary>How long the frame is shown, in milliseconds; 0 for a static image.</summary>
    public int DelayMilliseconds { get; } = delayMilliseconds;
}

/// <summary>
/// A decoded image: its pixel size, one or more <see cref="ImageFrame"/>s and, for an animation, how
/// many times it loops. The frame pixels are all the decoded image's size, so a control can blit any
/// of them into the same rectangle.
/// </summary>
public sealed class DecodedImage
{
    /// <summary>Creates a decoded image from its frames.</summary>
    public DecodedImage(int width, int height, IReadOnlyList<ImageFrame> frames, int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            throw new ArgumentException("A decoded image needs at least one frame.", nameof(frames));

        this.Width = width;
        this.Height = height;
        this.Frames = frames;
        this.LoopCount = Math.Max(0, loopCount);
    }

    /// <summary>The pixel width.</summary>
    public int Width { get; }

    /// <summary>The pixel height.</summary>
    public int Height { get; }

    /// <summary>The frames, in play order; one for a static image.</summary>
    public IReadOnlyList<ImageFrame> Frames { get; }

    /// <summary>How many times an animation plays before stopping on its last frame; 0 loops forever.</summary>
    public int LoopCount { get; }

    /// <summary>Whether the image carries more than one frame.</summary>
    public bool IsAnimated => this.Frames.Count > 1;

    /// <summary>The total duration of one loop, in milliseconds.</summary>
    public int TotalDurationMilliseconds
    {
        get
        {
            var total = 0;
            for (var i = 0; i < this.Frames.Count; ++i)
                total += Math.Max(1, this.Frames[i].DelayMilliseconds);

            return total;
        }
    }

    /// <summary>The shortest per-frame delay (floored at 1 ms) — the finest cadence an animation needs.</summary>
    public int ShortestFrameDelayMilliseconds
    {
        get
        {
            var shortest = int.MaxValue;
            for (var i = 0; i < this.Frames.Count; ++i)
                shortest = Math.Min(shortest, Math.Max(1, this.Frames[i].DelayMilliseconds));

            return shortest == int.MaxValue ? 1 : shortest;
        }
    }
}
