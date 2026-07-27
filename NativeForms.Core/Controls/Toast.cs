using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Shows transient in-app notifications — small <see cref="InfoBar"/>s anchored to the bottom-right of a
/// form that fade in, stack upward when several are live at once, and fade out while sliding down when they
/// expire or are dismissed. The in-window counterpart of the OS tray balloon, for "Saved", "Update
/// available" and the like.
/// </summary>
public static class Toast
{
    private const int _Gap = 8;
    private const int _Margin = 12;
    private const int _BarHeight = 36;

    // One live stack per form, holding its animation timer and the ordered toasts (newest at the bottom).
    private static readonly List<ToastStack> _stacks = [];

    /// <summary>Pops a toast on <paramref name="form"/> with the given text and severity; it stacks above any
    /// live toasts and removes itself after <paramref name="durationMs"/> milliseconds or when dismissed.</summary>
    public static void Show(Form form, string title, string message, InfoBarSeverity severity = InfoBarSeverity.Info, int durationMs = 3000)
    {
        ArgumentNullException.ThrowIfNull(form);
        StackFor(form).Add(title, message, severity, Math.Max(1, durationMs));
    }

    /// <summary>The live toasts on a form, newest last — for headless tests.</summary>
    internal static IReadOnlyList<InfoBar> ActiveToasts(Form form)
    {
        foreach (var stack in _stacks)
            if (ReferenceEquals(stack.Form, form))
                return stack.Bars;

        return [];
    }

    private static ToastStack StackFor(Form form)
    {
        foreach (var stack in _stacks)
            if (ReferenceEquals(stack.Form, form))
                return stack;

        var created = new ToastStack(form, () => _stacks.RemoveAll(s => ReferenceEquals(s.Form, form)));
        _stacks.Add(created);
        return created;
    }

    /// <summary>The animated toast column for one form.</summary>
    private sealed class ToastStack
    {
        private const int _StepMs = 16;
        private const double _Ease = 0.28;    // fraction of the remaining distance closed each frame

        private readonly List<Entry> _entries = [];
        private readonly Timer _timer;
        private readonly Action _onEmpty;

        public ToastStack(Form form, Action onEmpty)
        {
            this.Form = form;
            _onEmpty = onEmpty;
            _timer = new Timer { Interval = _StepMs };
            _timer.Tick += (_, _) => this.Step();
        }

        public Form Form { get; }

        public IReadOnlyList<InfoBar> Bars => _entries.ConvertAll(e => e.Bar);

        public void Add(string title, string message, InfoBarSeverity severity, int durationMs)
        {
            // Retire the oldest toasts as soon as the column would not fit the form: an uncapped stack walks
            // off the top edge (and a child above the client area is not visible anyway).
            var capacity = Math.Max(1, (this.Form.ClientSize.Height - (2 * _Margin)) / (_BarHeight + _Gap));
            for (var i = 0; i < _entries.Count && this.LiveCount() >= capacity; ++i)
                if (!_entries[i].Leaving)
                    _entries[i].Leaving = true;

            var width = Math.Min(360, Math.Max(160, this.Form.ClientSize.Width - (2 * _Margin)));
            var bar = new InfoBar
            {
                Title = title,
                Message = message,
                Severity = severity,
                Opacity = 0,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Bounds = new Rectangle(this.Form.ClientSize.Width - width - _Margin, this.Form.ClientSize.Height - _BarHeight - _Margin, width, _BarHeight),
            };

            var entry = new Entry(bar, durationMs);
            bar.Closed += (_, _) => entry.Leaving = true;
            _entries.Add(entry);
            this.Form.Controls.Add(bar);
            this.Relayout();
            _timer.Start();
        }

        /// <summary>The toasts still counted for stacking (the leaving ones are already collapsing away).</summary>
        private int LiveCount()
        {
            var live = 0;
            foreach (var entry in _entries)
                if (!entry.Leaving)
                    ++live;

            return live;
        }

        // Newest toast sits at the bottom; older ones stack above it. Sets each entry's resting Y target and
        // snaps non-leaving toasts to it, so the column is correctly stacked even before the first animation
        // frame; the timer then only drives the fade and the leaving slide-down.
        private void Relayout()
        {
            var y = this.Form.ClientSize.Height - _Margin;
            for (var i = _entries.Count - 1; i >= 0; --i)
            {
                var entry = _entries[i];
                if (entry.Leaving)
                    continue; // collapsing in place; it no longer holds a slot in the column

                y -= _BarHeight;
                entry.TargetY = Math.Max(_Margin, y);
                var b = entry.Bar.Bounds;
                entry.Bar.Bounds = new Rectangle(b.X, entry.TargetY, b.Width, b.Height);
                y -= _Gap;
            }
        }

        private void Step()
        {
            for (var i = _entries.Count - 1; i >= 0; --i)
            {
                var entry = _entries[i];
                entry.Life -= _StepMs;
                if (entry.Life <= 0)
                    entry.Leaving = true;

                var bar = entry.Bar;
                if (entry.Leaving)
                {
                    // Exit by collapsing toward the bottom edge (bottom pinned) and fading — never past the
                    // form's client area, so the window is not resized to fit a child that slid off-screen.
                    var bottom = entry.TargetY + _BarHeight;
                    var newHeight = (int)Math.Round(bar.Bounds.Height * (1 - _Ease));
                    bar.Opacity += (0.0 - bar.Opacity) * _Ease;
                    bar.Bounds = new Rectangle(bar.Bounds.X, bottom - newHeight, bar.Bounds.Width, Math.Max(0, newHeight));

                    if (bar.Opacity <= 0.05 || newHeight <= 2)
                    {
                        bar.Parent?.Controls.Remove(bar);
                        _entries.RemoveAt(i);
                        this.Relayout();
                    }
                }
                else
                {
                    var newY = (int)Math.Round(bar.Bounds.Y + ((entry.TargetY - bar.Bounds.Y) * _Ease));
                    bar.Bounds = new Rectangle(bar.Bounds.X, newY, bar.Bounds.Width, _BarHeight);
                    bar.Opacity += (1.0 - bar.Opacity) * _Ease;
                }
            }

            if (_entries.Count != 0)
                return;

            _timer.Stop();
            _timer.Dispose();
            _onEmpty();
        }

        private sealed class Entry(InfoBar bar, int life)
        {
            public InfoBar Bar { get; } = bar;
            public double Life { get; set; } = life;
            public bool Leaving { get; set; }
            public int TargetY { get; set; }
        }
    }
}
