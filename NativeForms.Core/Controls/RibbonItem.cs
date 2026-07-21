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
    internal void InvalidateMeasurement() => _cachedTextWidth = -1;
}
