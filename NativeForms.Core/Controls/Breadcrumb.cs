using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An Explorer-style breadcrumb bar (§7.9): a row of <see cref="BreadcrumbItem"/> path segments
/// separated by chevrons, each hover-highlit and clickable. When the segments outgrow the width the
/// leading ones fold behind a "…" overflow chip (the trailing path and the last segment always stay
/// visible), so a deep path never spills out of frame. Owner-drawn in the platform theme.
/// </summary>
public class Breadcrumb : OwnerDrawnControl
{
    private const int _Padding = 8;       // horizontal padding inside a segment
    private const int _ChevronSize = 6;   // chevron glyph edge
    private const int _ChevronGap = 5;    // gap on each side of a chevron
    private const int _IconGap = 4;       // gap between a segment's icon and caption
    private const int _IconSize = 16;

    /// <summary>One hit zone painted this frame: the x-range and the item it maps to (-1 = overflow chip).</summary>
    private readonly List<(int Left, int Right, int Index)> _zones = [];

    /// <summary>One chevron hit zone painted this frame: the x-range and the segment index it follows
    /// (whose children a click drops down); -1 when it trails the overflow chip.</summary>
    private readonly List<(int Left, int Right, int Segment)> _chevronZones = [];
    private MenuDropDown? _menu;
    private int _hot = -1;

    /// <summary>Creates an empty breadcrumb.</summary>
    public Breadcrumb() => this.Items = new(this);

    /// <summary>The path segments, left to right.</summary>
    public BreadcrumbItemCollection Items { get; }

    /// <summary>The icons the segments' <see cref="BreadcrumbItem.ImageIndex"/> point into.</summary>
    public ImageList? ImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>
    /// Whether clicking a segment trims the path to it — the classic "navigate up" gesture — before
    /// <see cref="ItemClicked"/> is raised. On by default; turn off to keep the whole path and drive
    /// navigation entirely from the event.
    /// </summary>
    public bool TrimOnClick { get; set; } = true;

    /// <summary>Raised when a segment is clicked (after any <see cref="TrimOnClick"/> trim).</summary>
    public event EventHandler<BreadcrumbItemEventArgs>? ItemClicked;

    /// <summary>
    /// The string that joins segments into a path and splits a typed path back into segments — "/" by
    /// default. Purely a text convention, so a caller walking an archive or any other virtual namespace
    /// picks whatever delimiter that namespace uses.
    /// </summary>
    public string PathSeparator
    {
        get => field;
        set => field = value ?? "/";
    } = "/";

    /// <summary>
    /// Supplies the children of a segment for its chevron drop-down — the "folder walk" hook. Given a
    /// segment (or <see langword="null"/> for the level before the first one), it returns that node's
    /// child entries; a click on the following chevron lists them, and choosing one navigates into it.
    /// Nothing here touches the filesystem, so it serves a real directory, an archive or any virtual
    /// tree equally. Left <see langword="null"/>, chevrons stay inert separators.
    /// </summary>
    public Func<BreadcrumbItem?, IReadOnlyList<BreadcrumbItem>>? SubItemsProvider { get; set; }

    /// <summary>Raised when a child chosen from a chevron drop-down is navigated into.</summary>
    public event EventHandler<BreadcrumbItemEventArgs>? SubItemSelected;

    /// <summary>Raises <see cref="SubItemSelected"/>.</summary>
    protected virtual void OnSubItemSelected(BreadcrumbItemEventArgs e) => this.SubItemSelected?.Invoke(this, e);

    /// <summary>Raises <see cref="ItemClicked"/>.</summary>
    protected virtual void OnItemClicked(BreadcrumbItemEventArgs e) => this.ItemClicked?.Invoke(this, e);

    /// <summary>Repaints after a segment set/text/icon change.</summary>
    internal void OnItemsChanged() => this.Invalidate();

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The width one segment occupies: padding, an optional icon, and the measured caption.</summary>
    private int SegmentWidth(IGraphics g, BreadcrumbItem item, Font font)
    {
        var width = (2 * _Padding) + g.MeasureText(item.Text, font).Width;
        if (this.ImageList is { } images && item.ResolveImageIndex(images) >= 0)
            width += _IconSize + _IconGap;

        return width;
    }

    /// <summary>The advance a chevron separator adds between two segments.</summary>
    private static int ChevronAdvance => _ChevronSize + (2 * _ChevronGap);

    /// <summary>
    /// The first segment index to show: everything fits from 0 when it can, otherwise the leading
    /// segments fold away (behind the "…" chip) until the trailing path — the last segment always
    /// included — fits the width.
    /// </summary>
    private int FirstVisible(IGraphics g, Font font, out bool overflow)
    {
        var total = 0;
        for (var i = 0; i < this.Items.Count; ++i)
            total += this.SegmentWidth(g, this.Items[i], font) + (i < this.Items.Count - 1 ? ChevronAdvance : 0);

        if (total <= this.Width)
        {
            overflow = false;
            return 0;
        }

        overflow = true;
        var available = this.Width - (this.SegmentWidth(g, EllipsisItem, font) + ChevronAdvance);
        var used = 0;
        var first = this.Items.Count - 1;
        for (var i = this.Items.Count - 1; i >= 0; --i)
        {
            used += this.SegmentWidth(g, this.Items[i], font) + (i < this.Items.Count - 1 ? ChevronAdvance : 0);
            if (used > available)
                break;

            first = i;
        }

        return first;
    }

    private static readonly BreadcrumbItem EllipsisItem = new("…");

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        _zones.Clear();
        _chevronZones.Clear();
        if (this.Items.Count == 0)
            return;

        var font = theme.DefaultFont;
        var first = this.FirstVisible(g, font, out var overflow);
        var x = 0;

        if (overflow)
        {
            var width = this.SegmentWidth(g, EllipsisItem, font);
            this.PaintSegment(g, theme, EllipsisItem, new Rectangle(x, 0, width, this.Height), hot: _hot == -1, index: -1);
            x += width;
            this.PaintChevron(g, theme, x);
            _chevronZones.Add((x, x + ChevronAdvance, first - 1)); // drops down the deepest hidden segment
            x += ChevronAdvance;
        }

        for (var i = first; i < this.Items.Count && x < this.Width; ++i)
        {
            var item = this.Items[i];
            var width = this.SegmentWidth(g, item, font);
            this.PaintSegment(g, theme, item, new Rectangle(x, 0, width, this.Height), hot: _hot == i, index: i);
            x += width;
            if (i < this.Items.Count - 1)
            {
                this.PaintChevron(g, theme, x);
                _chevronZones.Add((x, x + ChevronAdvance, i));
                x += ChevronAdvance;
            }
        }
    }

    private void PaintSegment(IGraphics g, ITheme theme, BreadcrumbItem item, Rectangle rect, bool hot, int index)
    {
        if (hot)
            g.FillRectangle(theme.HeaderBackground, rect);

        var textLeft = rect.X + _Padding;
        if (index >= 0 && this.ImageList is { } images && item.ResolveImageIndex(images) is var icon && icon >= 0 && icon < images.Count && this.Backend is { } backend)
        {
            var top = rect.Y + ((rect.Height - _IconSize) / 2);
            g.DrawImage(images.GetImage(icon, backend), new Rectangle(textLeft, top, _IconSize, _IconSize));
            textLeft += _IconSize + _IconGap;
        }

        var color = hot ? theme.Accent : theme.ControlText;
        g.DrawText(item.Text, theme.DefaultFont, color, new Rectangle(textLeft, rect.Y, Math.Max(0, rect.Right - _Padding - textLeft), rect.Height), ContentAlignment.MiddleLeft);
        _zones.Add((rect.X, rect.Right, index));
    }

    private void PaintChevron(IGraphics g, ITheme theme, int left)
    {
        var top = (this.Height - _ChevronSize) / 2;
        Glyphs.PaintTriangle(g, theme.Border, new Rectangle(left + _ChevronGap, top, _ChevronSize, _ChevronSize), GlyphDirection.Right);
    }

    /// <summary>The item index (or -1 for the overflow chip, -2 for none) under a client x.</summary>
    private int HitTest(int x)
    {
        for (var i = 0; i < _zones.Count; ++i)
            if (x >= _zones[i].Left && x < _zones[i].Right)
                return _zones[i].Index;

        return -2;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = this.HitTest(e.X);
        var hot = hit == -2 ? int.MinValue : hit; // -1 is the overflow chip, a valid hot target
        if (hot == _hot)
            return;

        _hot = hot;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hot == int.MinValue)
            return;

        _hot = int.MinValue;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.SubItemsProvider is not null && this.ChevronAt(e.X) is { } segment)
        {
            this.OpenSubItems(segment, e.X);
            return;
        }

        var hit = this.HitTest(e.X);
        if (hit < 0)
            return; // the overflow chip and empty space are not navigable

        var item = this.Items[hit];
        if (this.TrimOnClick)
            this.Items.TrimAfter(hit);

        this.OnItemClicked(new BreadcrumbItemEventArgs(item, hit));
    }

    /// <summary>The segment index a chevron under a client x follows, or <see langword="null"/> for none.</summary>
    private int? ChevronAt(int x)
    {
        for (var i = 0; i < _chevronZones.Count; ++i)
            if (x >= _chevronZones[i].Left && x < _chevronZones[i].Right)
                return _chevronZones[i].Segment;

        return null;
    }

    /// <summary>Drops down the children of a segment (from <see cref="SubItemsProvider"/>) as a menu;
    /// choosing one navigates into it.</summary>
    private void OpenSubItems(int segmentIndex, int x)
    {
        if (this.SubItemsProvider is not { } provider || this.Backend is not { } backend)
            return;

        var parent = segmentIndex >= 0 && segmentIndex < this.Items.Count ? this.Items[segmentIndex] : null;
        var children = provider(parent);
        if (children is null || children.Count == 0)
            return;

        var menuItems = new List<ToolStripItem>(children.Count);
        foreach (var child in children)
        {
            var captured = child;
            var menuItem = new ToolStripMenuItem(child.Text);
            if (this.ImageList is { } images)
            {
                menuItem.ImageList = images;
                menuItem.ImageIndex = child.ResolveImageIndex(images);
            }

            menuItem.Click += (_, _) => this.NavigateInto(segmentIndex, captured);
            menuItems.Add(menuItem);
        }

        var menu = _menu ??= new MenuDropDown(backend, this.Theme);
        menu.Owner = this.OwnerWindowPeer;
        menu.Open(menuItems, this.PointToScreen(new Point(Math.Max(0, x - 8), this.Height)));
    }

    /// <summary>Navigates into a child chosen from a chevron drop-down: trims the path to the segment
    /// whose children were listed, appends the chosen child, and reports it. Internal so a headless
    /// test can drive the navigation without the popup.</summary>
    internal void NavigateInto(int segmentIndex, BreadcrumbItem child)
    {
        _menu?.CloseAll();
        this.Items.TrimAfter(segmentIndex);
        this.Items.Add(child);

        var e = new BreadcrumbItemEventArgs(child, this.Items.Count - 1);
        this.OnSubItemSelected(e);
        this.OnItemClicked(e);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _menu?.CloseAll();
        _menu = null;
    }
}
