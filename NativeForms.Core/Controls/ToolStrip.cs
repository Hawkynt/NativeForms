using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn toolbar: a horizontal row of <see cref="ToolStripButton"/>s (icon + optional
/// caption, hover/pressed/toggled states, command wiring), separators, and drop-down/split buttons
/// whose menus open through the shared <see cref="MenuDropDown"/> engine. When the bar is too narrow
/// the trailing items collapse behind an overflow chevron that opens them as a popup menu.
/// </summary>
public class ToolStrip : OwnerDrawnControl
{
    /// <summary>The horizontal padding inside a button, and the gap between its icon and caption.</summary>
    internal const int ButtonPadding = 4;

    /// <summary>The edge length of a button icon.</summary>
    internal const int IconSize = 16;

    /// <summary>The pixel width of a separator item.</summary>
    internal const int SeparatorWidth = 7;

    /// <summary>The width of the arrow zone of drop-down and split buttons.</summary>
    internal const int ArrowZoneWidth = 12;

    /// <summary>The width of the overflow chevron zone at the right edge.</summary>
    internal const int ChevronWidth = 16;

    private MenuDropDown? _dropDown;
    private int _hoverIndex = -1;
    private int _pressedIndex = -1;
    private List<ToolStripItem>? _overflow;

    /// <summary>Cached per-item pixel widths, index-aligned with <see cref="Items"/>; 0 marks an
    /// unmeasured slot. Invalidated by the Items.Changed hook, by (un)realization and by a theme
    /// font swap, so per-event hit-testing stops re-measuring text natively on every mouse move.</summary>
    private int[]? _itemWidths;

    /// <summary>The theme font the cache was measured with; a different snapshot voids it.</summary>
    private Font _measuredFont;

    /// <summary>Creates an empty toolbar.</summary>
    public ToolStrip()
    {
        this.Items = new();
        this.Items.Changed += (_, _) =>
        {
            _itemWidths = null;
            this.Invalidate();
        };
    }

    /// <summary>The toolbar items. Mutating the collection (or any item in it) repaints the bar.</summary>
    public ToolStripItemCollection Items { get; }

    /// <summary>Whether the bar currently needs the overflow chevron.</summary>
    public bool HasOverflow => this.FirstOverflowIndex() < this.Items.Count;

    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        _itemWidths = null; // measurements now come from the live backend
    }

    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _dropDown?.CloseAll();
        _dropDown = null;
        _itemWidths = null;
    }

    /// <summary>The lazily created drop-down engine shared by drop-down buttons and the chevron, with
    /// its owning window refreshed on every access so each cascade is anchored to the current form.</summary>
    private MenuDropDown Engine
    {
        get
        {
            var engine = _dropDown ??= new(this.Backend!, this.Theme);
            engine.Owner = this.OwnerWindowPeer;
            return engine;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var height = this.Height;
        g.FillRectangle(theme.ControlBackground, new(0, 0, this.Width, height));

        var firstOverflow = this.FirstOverflowIndex();
        var x = 0;
        for (var i = 0; i < firstOverflow; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            var width = this.ItemWidth(i, item);
            this.PaintItem(g, item, i, new(x, 0, width, height));
            x += width;
        }

        if (firstOverflow < this.Items.Count)
        {
            // The chevron: a down-pointing triangle over a bar, hugging the right edge.
            var zone = new Rectangle(this.Width - ChevronWidth, 0, ChevronWidth, height);
            g.DrawLine(theme.ControlText, zone.X + 4, (height / 2) - 6, zone.X + 11, (height / 2) - 6);
            Glyphs.PaintTriangle(g, theme.ControlText, new(zone.X + 4, (height / 2) - 3, 8, 5), GlyphDirection.Down);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        if (this.HasOverflow && e.X >= this.Width - ChevronWidth)
        {
            this.OpenOverflow();
            return;
        }

        var index = this.ItemAt(e.X, out var left);
        if (index < 0)
            return;

        var item = this.Items[index];
        if (item is ToolStripSeparator || !item.Enabled)
            return;

        switch (item)
        {
            case ToolStripSplitButton split when e.X >= left + this.ItemWidth(index, split) - ArrowZoneWidth:
            case ToolStripDropDownButton:
                this.OpenItemDropDown((ToolStripDropDownItem)item, left);
                return;

            default:
                _pressedIndex = index;
                this.Invalidate();
                return;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressedIndex;
        if (pressed < 0)
            return;

        _pressedIndex = -1;
        this.Invalidate();
        if (e.Button == MouseButtons.Left && this.ItemAt(e.X, out _) == pressed)
            this.Items[pressed].PerformClick();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = this.ItemAt(e.X, out _);
        if (index == _hoverIndex)
            return;

        _hoverIndex = index;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex < 0 && _pressedIndex < 0)
            return;

        _hoverIndex = -1;
        _pressedIndex = -1;
        this.Invalidate();
    }

    /// <summary>Paints one inline item in its current hover/pressed/checked state.</summary>
    private void PaintItem(IGraphics g, ToolStripItem item, int index, Rectangle bounds)
    {
        var theme = this.Theme;
        if (item is ToolStripSeparator)
        {
            var mid = bounds.X + (bounds.Width / 2);
            g.DrawLine(theme.Border, mid, 3, mid, bounds.Height - 4);
            return;
        }

        var isChecked = item is ToolStripButton { Checked: true };
        var pressed = index == _pressedIndex;
        var hovered = index == _hoverIndex && item.Enabled;
        if (pressed)
            GlyphRenderer.FillSelection(g, theme, bounds);
        else if (hovered || isChecked)
            g.FillRectangle(theme.HeaderBackground, bounds);

        if (isChecked)
            g.DrawRectangle(theme.Accent, new(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));

        var textColor = !item.Enabled ? theme.DisabledText : pressed ? theme.SelectionText : theme.ControlText;
        var x = bounds.X + ButtonPadding;
        var icon = item.ResolveImage(this.Backend);
        if (icon is not null)
        {
            g.DrawImage(icon, new(x, bounds.Y + ((bounds.Height - IconSize) / 2), IconSize, IconSize));
            x += IconSize + (item.DisplayText.Length > 0 ? ButtonPadding : 0);
        }

        if (item.DisplayText.Length > 0)
        {
            var textRect = new Rectangle(x, bounds.Y, bounds.Right - x, bounds.Height);
            ToolStripRenderer.PaintMnemonicText(g, theme.DefaultFont, textColor, item, textRect);
        }

        if (item is ToolStripDropDownItem)
        {
            var arrowLeft = bounds.Right - ArrowZoneWidth;
            if (item is ToolStripSplitButton)
                g.DrawLine(theme.Border, arrowLeft, 3, arrowLeft, bounds.Height - 4);

            Glyphs.PaintTriangle(g, textColor, new(arrowLeft + 3, (bounds.Height / 2) - 1, 6, 4), GlyphDirection.Down);
        }
    }

    /// <summary>Opens a drop-down/split button's menu below the bar, left-aligned with the item.</summary>
    private void OpenItemDropDown(ToolStripDropDownItem item, int left)
    {
        if (this.Backend is null || !item.HasDropDownItems)
            return;

        this.Engine.Open(item.DropDownItems, this.PointToScreen(new(left, this.Height)));
    }

    /// <summary>Opens the overflow popup: every item that did not fit, right-aligned under the chevron.</summary>
    private void OpenOverflow()
    {
        if (this.Backend is null)
            return;

        var overflow = _overflow ??= [];
        overflow.Clear();
        for (var i = this.FirstOverflowIndex(); i < this.Items.Count; ++i)
            if (this.Items[i].Visible)
                overflow.Add(this.Items[i]);

        if (overflow.Count == 0)
            return;

        var engine = this.Engine;
        var size = engine.ComputeSize(overflow);
        engine.Open(overflow, this.PointToScreen(new(this.Width - size.Width, this.Height)));
    }

    /// <summary>
    /// The index of the first item pushed into the overflow, or the item count when everything fits.
    /// Items overflow as a suffix: layout stops at the first visible item whose right edge would
    /// cross into the chevron zone.
    /// </summary>
    private int FirstOverflowIndex()
    {
        var total = 0;
        var count = this.Items.Count;
        for (var i = 0; i < count; ++i)
            if (this.Items[i].Visible)
                total += this.ItemWidth(i, this.Items[i]);

        if (total <= this.Width)
            return count;

        var limit = this.Width - ChevronWidth;
        var x = 0;
        for (var i = 0; i < count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            x += this.ItemWidth(i, item);
            if (x > limit)
                return i;
        }

        return count;
    }

    /// <summary>The index of the inline item under x-coordinate <paramref name="x"/> (its left edge
    /// in <paramref name="left"/>), or -1 for none, the chevron zone or an overflowed item.</summary>
    private int ItemAt(int x, out int left)
    {
        left = 0;
        var firstOverflow = this.FirstOverflowIndex();
        var position = 0;
        for (var i = 0; i < firstOverflow; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            var width = this.ItemWidth(i, item);
            if (x >= position && x < position + width)
            {
                left = position;
                return i;
            }

            position += width;
        }

        return -1;
    }

    /// <summary>The pixel width of the item at <paramref name="index"/>, from the cache when it is
    /// warm, measured (and cached) otherwise.</summary>
    private int ItemWidth(int index, ToolStripItem item)
    {
        var font = this.Theme.DefaultFont;
        var cache = _itemWidths;
        if (cache is null || cache.Length != this.Items.Count || _measuredFont != font)
        {
            _itemWidths = cache = new int[this.Items.Count];
            _measuredFont = font;
        }

        var width = cache[index];
        if (width == 0)
            cache[index] = width = this.MeasureItemWidth(item);

        return width;
    }

    /// <summary>Measures one item: padding, icon, caption and arrow zone as applicable.</summary>
    private int MeasureItemWidth(ToolStripItem item)
    {
        if (item is ToolStripSeparator)
            return SeparatorWidth;

        var width = 2 * ButtonPadding;
        var hasIcon = item.Image is not null || (item.ImageList is not null && item.ImageIndex >= 0);
        if (hasIcon)
            width += IconSize;

        if (item.DisplayText.Length > 0)
        {
            if (hasIcon)
                width += ButtonPadding;

            width += this.Backend?.MeasureText(item.DisplayText, this.Theme.DefaultFont).Width ?? 0;
        }

        if (item is ToolStripDropDownItem)
            width += ArrowZoneWidth;

        return width;
    }
}
