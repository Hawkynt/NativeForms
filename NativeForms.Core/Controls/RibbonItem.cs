using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for everything a <see cref="RibbonGroup"/> hosts. A ribbon item is a
/// <see cref="ToolStripItem"/>, so it already carries the caption and mnemonic, the icon (direct or
/// <see cref="ToolStripItem.ImageList"/> + index), the enabled/visible flags and the
/// <see cref="ToolStripItem.Command"/> wiring — and a group that collapses into a drop-down can hand
/// its items straight to the shared <see cref="MenuDropDown"/> engine without a translation layer.
/// </summary>
/// <remarks>
/// The measured caption width is cached on the item and keyed by the theme font, so hit-testing a
/// ribbon on every mouse move never re-measures text natively. The cache is dropped whenever the
/// owning group reports a change (a new caption, a new icon) or the font snapshot moves.
/// </remarks>
public abstract class RibbonItem : ToolStripItem
{
    private int _cachedTextWidth = -1;

    /// <summary>The two-line wrap of a large caption, allocated lazily so a small item — or a large
    /// item that never wraps — keeps a single null reference rather than three inline fields.</summary>
    private sealed class WrapCache
    {
        public string Line1 = string.Empty;
        public string? Line2;
        public int Width;
    }

    private WrapCache? _wrap;

    /// <summary>Whether the item takes the full group height or one of three stacked rows.</summary>
    public RibbonItemSize ItemSize
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = RibbonItemSize.Large;

    /// <summary>
    /// The measured pixel width of the caption, from the cache when it is warm and measured (then
    /// cached) otherwise. A measurement taken before the control is realized is <em>not</em> cached:
    /// there is no backend to ask yet, and latching that zero in would leave every item the width of
    /// its padding for the rest of its life.
    /// </summary>
    /// <remarks>
    /// The font the cache was taken with is <em>not</em> kept here. The owning <see cref="Ribbon"/>
    /// holds one font snapshot for the whole control and drops every measurement when it moves, so a
    /// ribbon with two hundred buttons carries one font key rather than two hundred — the same
    /// arrangement <see cref="ToolStrip"/> uses.
    /// </remarks>
    internal int TextWidth(IPlatformBackend? backend, Font font)
    {
        if (_cachedTextWidth >= 0)
            return _cachedTextWidth;

        if (this.DisplayText.Length == 0)
            return _cachedTextWidth = 0;

        return backend is null ? 0 : _cachedTextWidth = backend.MeasureText(this.DisplayText, font).Width;
    }

    /// <summary>Drops the measured width, so the next paint measures the caption again.</summary>
    internal void InvalidateMeasurement()
    {
        _cachedTextWidth = -1;
        _wrap = null;
    }

    /// <summary>
    /// The large-button caption wrapped onto at most two lines so a long multi-word caption keeps the
    /// button compact: the single line while it fits within <paramref name="maxWidth"/> (or has no
    /// space to break at), otherwise the space split that makes the wider of the two lines as narrow
    /// as possible. Cached — keyed, like the width, by the ribbon's one font snapshot — so the paint
    /// path allocates nothing once warm; only a cache miss splits the string.
    /// </summary>
    internal (string Line1, string? Line2, int Width) WrapLarge(IPlatformBackend? backend, Font font, int maxWidth)
    {
        if (_wrap is { } cached)
            return (cached.Line1, cached.Line2, cached.Width);

        var text = this.DisplayText;
        var single = this.TextWidth(backend, font);
        if (backend is null)
            return (text, null, single); // not cached: no backend to measure the split lines with yet

        if (single <= maxWidth || text.IndexOf(' ') < 0)
            return this.CacheWrap(text, null, single);

        var bestWider = int.MaxValue;
        var bestSplit = -1;
        for (var i = text.IndexOf(' '); i >= 0; i = text.IndexOf(' ', i + 1))
        {
            var left = backend.MeasureText(text[..i], font).Width;
            var right = backend.MeasureText(text[(i + 1)..], font).Width;
            var wider = Math.Max(left, right);
            if (wider < bestWider)
            {
                bestWider = wider;
                bestSplit = i;
            }
        }

        return bestSplit < 0
            ? this.CacheWrap(text, null, single)
            : this.CacheWrap(text[..bestSplit], text[(bestSplit + 1)..], bestWider);
    }

    private (string, string?, int) CacheWrap(string line1, string? line2, int width)
    {
        _wrap = new WrapCache { Line1 = line1, Line2 = line2, Width = width };
        return (line1, line2, width);
    }
}
