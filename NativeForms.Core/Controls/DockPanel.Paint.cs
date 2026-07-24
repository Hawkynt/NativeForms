using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

public partial class DockPanel
{
    private const int _TabMaxWidth = 160;
    private const int _MinVerticalTabWidth = 40;
    private const int _IconGap = 4;
    private const int _CaptionInset = 6;

    /// <summary>The caption button kinds, right to left.</summary>
    private enum CaptionButton
    {
        None,
        Close,
        Float,
        Pin,
    }

    // --- Painting ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        if (_root is not null)
            this.PaintNode(g, theme, _root);

        this.PaintAutoHideStrips(g, theme);

        if (_root is null && (_autoHide is null || _autoHide.Count == 0))
            this.PaintEmptyHint(g, theme);
    }

    private void PaintNode(IGraphics g, ITheme theme, DockNode node)
    {
        switch (node)
        {
            case DockSplitNode split:
                this.PaintSplitter(g, theme, split.Splitter, split.Orientation);
                this.PaintNode(g, theme, split.First);
                this.PaintNode(g, theme, split.Second);
                break;
            case DockTabGroupNode group:
                this.PaintGroup(g, theme, group);
                break;
        }
    }

    private void PaintSplitter(IGraphics g, ITheme theme, Rectangle bar, Orientation orientation)
    {
        if (bar.IsEmpty)
            return;

        g.FillRectangle(theme.ControlBackground, bar);
        var cx = bar.X + (bar.Width / 2);
        var cy = bar.Y + (bar.Height / 2);
        for (var i = -1; i <= 1; ++i)
        {
            var dot = orientation == Orientation.Vertical
                ? new Rectangle(cx - 1, cy + (i * 6) - 1, 2, 2)
                : new Rectangle(cx + (i * 6) - 1, cy - 1, 2, 2);
            g.FillRectangle(theme.Border, dot);
        }
    }

    private void PaintGroup(IGraphics g, ITheme theme, DockTabGroupNode group)
    {
        if (group.Active is not { } active)
            return;

        this.PaintCaption(g, theme, group, active);

        if (!group.TabStripBounds.IsEmpty)
            this.PaintTabStrip(g, theme, group);

        // One frame around the whole well (tab strip + body), drawn after the fills so nothing erases
        // it; the strip's own separating line and the caption line read as the dividers within it.
        var well = group.TabStripBounds.IsEmpty
            ? group.ContentBounds
            : Rectangle.Union(group.TabStripBounds, group.ContentBounds);
        if (well is { Width: > 1, Height: > 1 })
            g.DrawRectangle(theme.Border, new Rectangle(well.X, well.Y, well.Width - 1, well.Height - 1));
    }

    private void PaintCaption(IGraphics g, ITheme theme, DockTabGroupNode group, DockContent active)
    {
        var bounds = group.CaptionBounds;
        if (bounds.Height <= 0)
            return;

        var isActive = ReferenceEquals(_active, active);
        var back = isActive ? theme.Accent : theme.HeaderBackground;
        var fore = isActive ? theme.SelectionText : theme.HeaderText;
        g.FillRectangle(back, bounds);
        g.DrawLine(theme.Border, bounds.X, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);

        var buttonsWidth = this.CaptionButtonsWidth(active);
        var textLeft = bounds.X + _CaptionInset;
        var activeImage = ImageList.ResolveIndex(this.ImageList, active.ImageIndex, active.ImageKey);
        if (this.ImageList is { } images && activeImage >= 0 && activeImage < images.Count && this.Backend is { } backend)
        {
            var size = images.ImageSize;
            var top = bounds.Y + ((bounds.Height - size.Height) / 2);
            g.DrawImage(images.GetImage(activeImage, backend), new Rectangle(textLeft, top, size.Width, size.Height));
            textLeft += size.Width + _IconGap;
        }

        var textRight = bounds.Right - buttonsWidth - _CaptionInset;
        var textRect = new Rectangle(textLeft, bounds.Y, Math.Max(0, textRight - textLeft), bounds.Height);
        // Clip the caption to its text cell so a long title's glyphs never spill over the icon or the
        // caption buttons that follow it.
        g.PushClip(textRect);
        g.DrawText(active.Title, theme.DefaultFont, fore, textRect, ContentAlignment.MiddleLeft);
        g.PopClip();

        this.PaintCaptionButtons(g, theme, group, active, fore);
    }

    private void PaintCaptionButtons(IGraphics g, ITheme theme, DockTabGroupNode group, DockContent active, Color fore)
    {
        var bounds = group.CaptionBounds;
        var size = bounds.Height;
        var x = bounds.Right - size;
        var count = this.CaptionButtonCount(active);
        for (var slot = 0; slot < count; ++slot)
        {
            var kind = this.NthButton(active, slot);
            var cell = new Rectangle(x, bounds.Y, size, size);
            if (this.HotButton(group, kind))
                g.FillRectangle(theme.SelectionBackground, cell);

            this.PaintButtonGlyph(g, fore, cell, kind, active);
            x -= size;
        }
    }

    private void PaintButtonGlyph(IGraphics g, Color color, Rectangle cell, CaptionButton kind, DockContent active)
    {
        var m = cell.Width / 3;
        var box = new Rectangle(cell.X + m, cell.Y + m, cell.Width - (2 * m), cell.Height - (2 * m));
        switch (kind)
        {
            case CaptionButton.Close:
                g.DrawLine(color, box.Left, box.Top, box.Right, box.Bottom);
                g.DrawLine(color, box.Left, box.Bottom, box.Right, box.Top);
                break;
            case CaptionButton.Float:
                g.DrawRectangle(color, box);
                g.DrawLine(color, box.Left, box.Top + 1, box.Right, box.Top + 1);
                break;
            case CaptionButton.Pin:
                // A filled pin when auto-hidden, an outline otherwise.
                var dir = active.DockState == DockState.AutoHide ? GlyphDirection.Right : GlyphDirection.Down;
                Glyphs.PaintTriangle(g, color, box, dir);
                break;
        }
    }

    private void PaintTabStrip(IGraphics g, ITheme theme, DockTabGroupNode group)
    {
        var strip = group.TabStripBounds;
        g.FillRectangle(theme.HeaderBackground, strip);

        var edge = this.DocumentTabStripEdge;
        var vertical = edge is TabAlignment.Left or TabAlignment.Right;

        // A separating line along the edge the strip shares with the content it labels.
        switch (edge)
        {
            case TabAlignment.Top: g.DrawLine(theme.Border, strip.X, strip.Bottom - 1, strip.Right - 1, strip.Bottom - 1); break;
            case TabAlignment.Left: g.DrawLine(theme.Border, strip.Right - 1, strip.Y, strip.Right - 1, strip.Bottom - 1); break;
            case TabAlignment.Right: g.DrawLine(theme.Border, strip.X, strip.Y, strip.X, strip.Bottom - 1); break;
            default: g.DrawLine(theme.Border, strip.X, strip.Y, strip.Right - 1, strip.Y); break; // Bottom
        }

        var count = group.Contents.Count;
        var extent = vertical ? strip.Height : strip.Width;
        var cellSize = this.TabCellSize(extent, count, vertical);
        if (cellSize <= 0)
            return;

        for (var i = 0; i < count; ++i)
        {
            var cell = TabCellRect(strip, i, cellSize, vertical);
            if (vertical ? cell.Bottom > strip.Bottom : cell.Right > strip.Right)
                break;

            var selected = i == group.ActiveIndex;
            if (selected)
                g.FillRectangle(theme.ControlBackground, cell);
            else if (this.HotTab(group, i))
                g.FillRectangle(theme.SelectionBackground, cell);

            if (selected)
                g.FillRectangle(theme.Accent, TabAccentRect(cell, edge));

            var text = new Rectangle(cell.X + 6, cell.Y, cell.Width - 12, cell.Height);
            g.PushClip(text);
            g.DrawText(group.Contents[i].Title, theme.DefaultFont, theme.ControlText, text, ContentAlignment.MiddleLeft);
            g.PopClip();
        }
    }

    /// <summary>The per-tab extent along the strip's axis: capped to the max tab width when horizontal
    /// and to a row height when vertical, shrinking to share the strip when the tabs are many.</summary>
    private int TabCellSize(int stripExtent, int count, bool vertical)
        => count <= 0 ? 0 : Math.Min(vertical ? this.TabStripHeight : _TabMaxWidth, stripExtent / count);

    /// <summary>The i-th tab cell laid out along the strip's axis.</summary>
    private static Rectangle TabCellRect(Rectangle strip, int i, int cellSize, bool vertical)
        => vertical
            ? new Rectangle(strip.X, strip.Y + (i * cellSize), strip.Width, cellSize)
            : new Rectangle(strip.X + (i * cellSize), strip.Y, cellSize, strip.Height);

    /// <summary>The 2-px selected-tab accent, on the cell edge that faces the content.</summary>
    private static Rectangle TabAccentRect(Rectangle cell, TabAlignment edge) => edge switch
    {
        TabAlignment.Top => new(cell.X, cell.Bottom - 2, cell.Width, 2),
        TabAlignment.Left => new(cell.Right - 2, cell.Y, 2, cell.Height),
        TabAlignment.Right => new(cell.X, cell.Y, 2, cell.Height),
        _ => new(cell.X, cell.Y, cell.Width, 2), // Bottom: content is above
    };

    private void PaintAutoHideStrips(IGraphics g, ITheme theme)
    {
        if (_autoHide is null || _autoHide.Count == 0)
            return;

        for (var edge = 0; edge < 4; ++edge)
            this.PaintAutoHideStrip(g, theme, (DockEdge)edge);
    }

    private void PaintAutoHideStrip(IGraphics g, ITheme theme, DockEdge edge)
    {
        var strip = this.AutoHideStripBounds(edge);
        if (strip.IsEmpty)
            return;

        g.FillRectangle(theme.HeaderBackground, strip);
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        if (vertical)
            g.DrawLine(theme.Border, edge == DockEdge.Left ? strip.Right - 1 : strip.X, strip.Y, edge == DockEdge.Left ? strip.Right - 1 : strip.X, strip.Bottom - 1);
        else
            g.DrawLine(theme.Border, strip.X, edge == DockEdge.Top ? strip.Bottom - 1 : strip.Y, strip.Right - 1, edge == DockEdge.Top ? strip.Bottom - 1 : strip.Y);

        var index = 0;
        for (var i = 0; i < _autoHide!.Count; ++i)
        {
            var pane = _autoHide[i];
            if (pane.DockEdge != edge)
                continue;

            var cell = this.AutoHideTabBounds(edge, index++);
            var hot = ReferenceEquals(_flyout, pane) || this.HotAutoHide(pane);
            if (hot)
                g.FillRectangle(theme.SelectionBackground, cell);
            g.DrawRectangle(theme.Border, new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1));

            var iconLeft = cell.X + 4;
            var paneImage = ImageList.ResolveIndex(this.ImageList, pane.ImageIndex, pane.ImageKey);
            if (this.ImageList is { } images && paneImage >= 0 && paneImage < images.Count && this.Backend is { } backend)
            {
                var size = images.ImageSize;
                var top = cell.Y + ((cell.Height - size.Height) / 2);
                g.DrawImage(images.GetImage(paneImage, backend), new Rectangle(iconLeft, top, size.Width, size.Height));
                iconLeft += size.Width + _IconGap;
            }

            if (!vertical)
            {
                var text = new Rectangle(iconLeft, cell.Y, cell.Right - iconLeft - 4, cell.Height);
                g.DrawText(pane.Title, theme.DefaultFont, theme.HeaderText, text, ContentAlignment.MiddleLeft);
            }
        }
    }

    private void PaintEmptyHint(IGraphics g, ITheme theme)
        => g.DrawText(
            "Dock panes here",
            theme.DefaultFont,
            theme.DisabledText,
            new Rectangle(0, 0, this.Width, this.Height),
            ContentAlignment.MiddleCenter);

    // --- Geometry (deterministic; no per-frame measuring) -----------------------------------------


    private int CaptionButtonsWidth(DockContent active) => this.CaptionButtonCount(active) * this.CaptionHeight;

    private int CaptionButtonCount(DockContent active)
    {
        var n = 0;
        if (active.AllowClose) ++n;
        if (active.AllowFloat) ++n;
        if (active.AllowAutoHide) ++n;
        return n;
    }

    /// <summary>The <paramref name="slot"/>-th allowed caption button in right-to-left order (close is
    /// slot 0), or <see cref="CaptionButton.None"/>. Index-based rather than an iterator so the paint
    /// path allocates nothing.</summary>
    private CaptionButton NthButton(DockContent active, int slot)
    {
        var seen = 0;
        if (active.AllowClose) { if (seen == slot) return CaptionButton.Close; ++seen; }
        if (active.AllowFloat) { if (seen == slot) return CaptionButton.Float; ++seen; }
        if (active.AllowAutoHide) { if (seen == slot) return CaptionButton.Pin; }
        return CaptionButton.None;
    }

    private CaptionButton CaptionButtonAt(DockTabGroupNode group, DockContent active, Point pt)
    {
        var bounds = group.CaptionBounds;
        if (!bounds.Contains(pt))
            return CaptionButton.None;

        var size = bounds.Height;
        var x = bounds.Right - size;
        var count = this.CaptionButtonCount(active);
        for (var slot = 0; slot < count; ++slot)
        {
            if (new Rectangle(x, bounds.Y, size, size).Contains(pt))
                return this.NthButton(active, slot);
            x -= size;
        }

        return CaptionButton.None;
    }

    private Rectangle AutoHideStripBounds(DockEdge edge)
    {
        if (!this.HasAutoHide(edge))
            return Rectangle.Empty;

        var t = this.AutoHideThickness;
        return edge switch
        {
            DockEdge.Left => new Rectangle(0, 0, t, this.Height),
            DockEdge.Right => new Rectangle(this.Width - t, 0, t, this.Height),
            DockEdge.Top => new Rectangle(0, 0, this.Width, t),
            _ => new Rectangle(0, this.Height - t, this.Width, t),
        };
    }

    private Rectangle AutoHideTabBounds(DockEdge edge, int index)
    {
        var strip = this.AutoHideStripBounds(edge);
        if (strip.IsEmpty)
            return Rectangle.Empty;

        const int cellV = 24;   // stacked-cell height for left/right strips
        const int cellH = 120;  // cell width for top/bottom strips
        return edge switch
        {
            DockEdge.Left or DockEdge.Right => new Rectangle(strip.X + 1, strip.Y + 2 + (index * cellV), strip.Width - 2, cellV - 2),
            _ => new Rectangle(strip.X + 2 + (index * cellH), strip.Y + 1, cellH - 2, strip.Height - 2),
        };
    }

    private DockContent? AutoHideTabAt(Point pt)
    {
        if (_autoHide is null)
            return null;

        for (var edge = 0; edge < 4; ++edge)
        {
            var index = 0;
            for (var i = 0; i < _autoHide.Count; ++i)
            {
                var pane = _autoHide[i];
                if ((int)pane.DockEdge != edge)
                    continue;
                if (this.AutoHideTabBounds((DockEdge)edge, index++).Contains(pt))
                    return pane;
            }
        }

        return null;
    }

    private DockSplitNode? SplitterAt(Point pt) => SplitterAt(_root, pt);

    private static DockSplitNode? SplitterAt(DockNode? node, Point pt)
    {
        if (node is not DockSplitNode split)
            return null;
        if (split.Splitter.Contains(pt))
            return split;
        return SplitterAt(split.First, pt) ?? SplitterAt(split.Second, pt);
    }

    private DockTabGroupNode? GroupAt(Point pt) => GroupAt(_root, pt);

    private static DockTabGroupNode? GroupAt(DockNode? node, Point pt)
    {
        switch (node)
        {
            case DockTabGroupNode group:
                return group.Bounds.Contains(pt) ? group : null;
            case DockSplitNode split:
                return GroupAt(split.First, pt) ?? GroupAt(split.Second, pt);
            default:
                return null;
        }
    }

    private int TabIndexAt(DockTabGroupNode group, Point pt)
    {
        var strip = group.TabStripBounds;
        if (strip.IsEmpty || !strip.Contains(pt))
            return -1;

        var count = group.Contents.Count;
        var vertical = this.DocumentTabStripEdge is TabAlignment.Left or TabAlignment.Right;
        var cellSize = this.TabCellSize(vertical ? strip.Height : strip.Width, count, vertical);
        if (cellSize <= 0)
            return -1;

        var i = (vertical ? pt.Y - strip.Y : pt.X - strip.X) / cellSize;
        return i >= 0 && i < count ? i : -1;
    }
}
