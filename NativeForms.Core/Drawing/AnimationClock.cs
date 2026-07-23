namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The one timer that drives every on-screen animation. Subscribers register an
/// <see cref="AnimatedImage"/> together with a visibility test and an invalidate callback; on each
/// tick the clock repaints only the visible ones whose time-derived frame has advanced. A hidden
/// subscriber is skipped — it neither ticks nor repaints — yet because the frame is a function of
/// elapsed time (see <see cref="AnimatedImage.FrameIndexAt"/>) it is exactly right the moment it is
/// shown again. One shared <see cref="Timer"/> serves all of them.
/// </summary>
internal sealed class AnimationClock
{
    /// <summary>The process-wide clock the controls share.</summary>
    public static AnimationClock Instance { get; } = new();

    private readonly List<Entry> _entries = [];
    private Timer? _timer;

    private sealed class Entry(object key, AnimatedImage image, Func<bool> isVisible, Action invalidate)
    {
        public object Key { get; } = key;
        public AnimatedImage Image { get; } = image;
        public Func<bool> IsVisible { get; } = isVisible;
        public Action Invalidate { get; } = invalidate;
        public int LastFrame { get; set; } = -1;
    }

    /// <summary>Registers (or re-registers) a subscriber. A still image is accepted but never ticks.</summary>
    public void Register(object key, AnimatedImage image, Func<bool> isVisible, Action invalidate)
    {
        this.Unregister(key);
        _entries.Add(new Entry(key, image, isVisible, invalidate));
        this.Sync();
    }

    /// <summary>Removes a subscriber; the timer stops once none remain.</summary>
    public void Unregister(object key)
    {
        for (var i = _entries.Count - 1; i >= 0; --i)
            if (ReferenceEquals(_entries[i].Key, key))
                _entries.RemoveAt(i);

        this.Sync();
    }

    /// <summary>Runs one tick against a monotonic <paramref name="nowTick"/>: repaints each visible
    /// subscriber whose frame has advanced. Internal so a test can drive it deterministically.</summary>
    internal void Advance(long nowTick)
    {
        // A snapshot, because an invalidate could re-enter and mutate the list.
        for (var i = 0; i < _entries.Count; ++i)
        {
            var entry = _entries[i];
            if (!entry.Image.IsAnimated || !entry.IsVisible())
                continue;

            var frame = entry.Image.CurrentFrameIndex(nowTick);
            if (frame == entry.LastFrame)
                continue;

            entry.LastFrame = frame;
            entry.Invalidate();
        }
    }

    private void Sync()
    {
        var animated = false;
        var interval = 100;
        foreach (var entry in _entries)
            if (entry.Image.IsAnimated)
            {
                animated = true;
                interval = Math.Min(interval, entry.Image.ShortestDelayMilliseconds);
            }

        if (!animated)
        {
            if (_timer is { } idle)
                idle.Enabled = false;

            return;
        }

        var timer = _timer ??= this.CreateTimer();
        timer.Interval = Math.Clamp(interval, 20, 100); // fine enough for the fastest frame, never a busy loop
        timer.Enabled = true;
    }

    private Timer CreateTimer()
    {
        var timer = new Timer();
        timer.Tick += (_, _) => this.Advance(Environment.TickCount64);
        return timer;
    }
}
