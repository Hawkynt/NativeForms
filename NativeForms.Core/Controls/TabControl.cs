using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn tab control. The header strip along the top is painted in the native theme —
/// caption (and optional <see cref="ImageList"/> icon) per tab, an accent underline on the active
/// tab, hover feedback, and scroll arrows when the tabs overflow — while every <see cref="TabPage"/>
/// is a real nested container whose children realize as native peers. Exactly one page is visible at
/// a time; switching flips page visibility and re-applies the content-area bounds.
/// </summary>
/// <remarks>
/// The strip sits on any edge through <see cref="Alignment"/>. Top and bottom lay the tabs out
/// horizontally, their widths measured from the captions; left and right run a vertical strip whose
/// width is the widest caption and stack the tabs as themed rows with horizontal captions (the
/// toolkit has no rotated-text primitive, so side tabs read left-to-right rather than rotated — a
/// documented deviation from Windows Forms). Header hit zones come from the most recent paint, which
/// is also when tab captions are measured through <see cref="IGraphics.MeasureText"/>.
/// </remarks>
public class TabControl : OwnerDrawnControl
{
    private const int _TabPadding = 10;
    private const int _IconGap = 4;
    private const int _HeaderChrome = 6;
    private const int _UnderlineThickness = 2;
    private const int _ArrowWidth = 16;
    private const int _ArrowGlyphSize = 8;
    private const int _MinVerticalStripWidth = 24;
    private const int _CloseZone = 18;
    private const int _CloseGlyphSize = 8;

    private readonly List<int> _tabWidths = []; // per-tab size along the flow axis
    private TabAlignment _alignment = TabAlignment.Top;
    private int _verticalStripWidth = _MinVerticalStripWidth;
    private int _selectedIndex = -1;
    private int _firstVisibleTab;
    private int _hotTab = -1;
    private bool _overflow;

    /// <summary>Creates an empty tab control.</summary>
    public TabControl() => this.TabPages = new(this);

    /// <summary>The pages, in tab order. Adding a page parents it into <see cref="Control.Controls"/>.</summary>
    public TabPageCollection TabPages { get; }

    /// <summary>The icons referenced by each page's <see cref="TabPage.ImageIndex"/>, or <see langword="null"/>.</summary>
    public ImageList? ImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            this.BindImageListAnimation(field, value);
            field = value;
            this.Invalidate();
        }
    }

    /// <summary>Which edge the header strip is painted along; <see cref="TabAlignment.Top"/> by default.</summary>
    public TabAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment == value)
                return;

            _alignment = value;
            this.PerformLayout(); // the content area moved to a different edge
            this.Invalidate();
        }
    }

    /// <summary>The index of the visible page, or -1 while there are no pages.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < 0 || value >= this.TabPages.Count ? -1 : value;
            if (clamped == _selectedIndex)
                return;

            if (_selectedIndex >= 0 && _selectedIndex < this.TabPages.Count)
                this.TabPages[_selectedIndex].Visible = false;

            _selectedIndex = clamped;
            this.ShowSelectedPage();
            if (clamped >= 0 && clamped < _firstVisibleTab)
                _firstVisibleTab = clamped;

            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The visible page, or <see langword="null"/> while there are no pages.</summary>
    public TabPage? SelectedTab
    {
        get => _selectedIndex >= 0 && _selectedIndex < this.TabPages.Count ? this.TabPages[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.TabPages.IndexOf(value);
    }

    /// <summary>Whether each tab paints a close (×) button; off by default.</summary>
    public bool ShowCloseButtons
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PerformLayout(); // the reserved close zone changes a vertical strip's width
            this.Invalidate();
        }
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when a tab's close button is pressed, before the page is removed; cancelable.</summary>
    public event EventHandler<TabPageCancelEventArgs>? TabClosing;

    /// <summary>Raised after a tab's close button removed its page.</summary>
    public event EventHandler<TabPageEventArgs>? TabClosed;

    /// <summary>Raises <see cref="TabClosing"/>.</summary>
    protected virtual void OnTabClosing(TabPageCancelEventArgs e) => this.TabClosing?.Invoke(this, e);

    /// <summary>Raises <see cref="TabClosed"/>.</summary>
    protected virtual void OnTabClosed(TabPageEventArgs e) => this.TabClosed?.Invoke(this, e);

    /// <summary>The pixel height of a horizontal header strip (and the height of each stacked side tab).</summary>
    public int HeaderHeight => this.Theme.RowHeight + _HeaderChrome;

    /// <summary>Whether the strip runs down a side edge (tabs stacked) rather than along top/bottom.</summary>
    private bool Vertical => _alignment is TabAlignment.Left or TabAlignment.Right;

    /// <summary>The cross-axis thickness of the strip: the header height, or the vertical strip width.</summary>
    private int StripThickness => this.Vertical ? _verticalStripWidth : this.HeaderHeight;

    /// <summary>The length of the axis the tabs flow along: the width (horizontal) or the height (vertical).</summary>
    private int FlowExtent => this.Vertical ? this.Height : this.Width;

    /// <summary>The header strip rectangle on its aligned edge.</summary>
    private Rectangle GetHeaderRect()
    {
        var t = this.StripThickness;
        return _alignment switch
        {
            TabAlignment.Top => new(0, 0, this.Width, t),
            TabAlignment.Bottom => new(0, this.Height - t, this.Width, t),
            TabAlignment.Left => new(0, 0, t, this.Height),
            _ => new(this.Width - t, 0, t, this.Height),
        };
    }

    /// <summary>Remeasures the vertical strip width from the widest caption; a no-op while horizontal.</summary>
    private void RefreshStripWidth()
    {
        if (!this.Vertical || this.Backend is not { } backend)
            return;

        var font = this.Theme.DefaultFont;
        var iconWidth = this.ImageList is { } images ? images.ImageSize.Width + _IconGap : 0;
        var widest = _MinVerticalStripWidth;
        for (var i = 0; i < this.TabPages.Count; ++i)
        {
            var page = this.TabPages[i];
            var caption = (2 * _TabPadding) + backend.MeasureText(page.Text, font).Width;
            if (iconWidth > 0 && page.ResolveImageIndex(this.ImageList) >= 0)
                caption += iconWidth;
            if (this.ShowCloseButtons)
                caption += _CloseZone;

            widest = Math.Max(widest, caption);
        }

        _verticalStripWidth = widest;
    }

    /// <summary>Whether the last paint needed the header scroll arrows (tabs overflow the width).</summary>
    internal bool ShowsOverflowArrows => _overflow;

    /// <summary>The index of the leftmost tab currently shown in the header strip.</summary>
    internal int FirstVisibleTab => _firstVisibleTab;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>The client rectangle beside the header strip that pages fill.</summary>
    private Rectangle GetContentArea()
    {
        var t = this.StripThickness;
        return _alignment switch
        {
            TabAlignment.Top => new(0, t, this.Width, Math.Max(0, this.Height - t)),
            TabAlignment.Bottom => new(0, 0, this.Width, Math.Max(0, this.Height - t)),
            TabAlignment.Left => new(t, 0, Math.Max(0, this.Width - t), this.Height),
            _ => new(0, 0, Math.Max(0, this.Width - t), this.Height),
        };
    }

    /// <summary>Makes the selected page visible with up-to-date content-area bounds.</summary>
    private void ShowSelectedPage()
    {
        if (_selectedIndex < 0 || _selectedIndex >= this.TabPages.Count)
            return;

        var page = this.TabPages[_selectedIndex];
        page.Bounds = this.GetContentArea();
        page.Visible = true;
    }

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);

        // The theme (and with it the strip thickness) is only known now — refresh the page bounds
        // before the pages themselves realize.
        this.RefreshStripWidth();
        for (var i = 0; i < this.TabPages.Count; ++i)
            this.TabPages[i].Bounds = this.GetContentArea();
    }

    /// <summary>Re-applies the content-area bounds to every page — the tab control owns its pages'
    /// bounds, so the base Anchor/Dock engine is replaced wholesale. Each page then lays out its
    /// own children per their Anchor/Dock like any plain container.</summary>
    private protected override void OnLayout()
    {
        this.RefreshStripWidth();
        for (var i = 0; i < this.TabPages.Count; ++i)
            this.TabPages[i].Bounds = this.GetContentArea();

        this.Invalidate();
    }

    /// <summary>Called by <see cref="TabPageCollection.Add"/> after the page joined the list.</summary>
    internal void OnPageAdded(TabPage page)
    {
        this.OnPageAdopted(page);
        this.Controls.Add(page);
    }

    /// <summary>Selection and geometry bookkeeping for a page that just joined <see cref="TabPages"/> —
    /// shared by <see cref="TabPageCollection.Add"/> and the designer-style
    /// <c>Controls.Add(tabPage)</c> route.</summary>
    private void OnPageAdopted(TabPage page)
    {
        if (_selectedIndex < 0)
            _selectedIndex = 0;

        page.Visible = this.TabPages.Count - 1 == _selectedIndex;
        page.Bounds = this.GetContentArea();
        this.Invalidate();
    }

    /// <summary>
    /// Routes designer-style <c>Controls.Add(tabPage)</c> into <see cref="TabPages"/>, exactly like
    /// Windows Forms; anything that is not a <see cref="TabPage"/> is rejected — a tab control hosts
    /// pages only.
    /// </summary>
    /// <exception cref="InvalidOperationException">The child is not a <see cref="TabPage"/>.</exception>
    private protected override void OnChildAdded(Control child)
    {
        if (child is not TabPage page)
        {
            this.Controls.Remove(child);
            throw new InvalidOperationException(
                "Only TabPage instances can be added to a TabControl — add pages through TabPages or Controls.Add(tabPage), and put other controls onto a page.");
        }

        if (this.TabPages.IndexOf(page) < 0)
        {
            this.TabPages.Adopt(page);
            this.OnPageAdopted(page);
        }

        base.OnChildAdded(child);
    }

    /// <summary>Called by <see cref="TabPageCollection"/> after the page left the list at <paramref name="index"/>.</summary>
    internal void OnPageRemoved(TabPage page, int index)
    {
        this.Controls.Remove(page);
        page.Visible = true; // restore the page's stand-alone default

        if (this.TabPages.Count == 0)
        {
            _selectedIndex = -1;
            _firstVisibleTab = 0;
        }
        else if (index < _selectedIndex)
            --_selectedIndex;
        else if (index == _selectedIndex)
        {
            _selectedIndex = Math.Min(index, this.TabPages.Count - 1);
            this.ShowSelectedPage();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }

        _firstVisibleTab = Math.Clamp(_firstVisibleTab, 0, Math.Max(0, this.TabPages.Count - 1));
        this.Invalidate();
    }

    /// <summary>
    /// Recomputes each tab's size along the flow axis: the measured caption width when the strip runs
    /// horizontally, or one themed row height when it stacks down a side.
    /// </summary>
    private void MeasureTabs(IGraphics g)
    {
        _tabWidths.Clear();
        var font = this.Theme.DefaultFont;
        var iconWidth = this.ImageList is { } images ? images.ImageSize.Width + _IconGap : 0;
        for (var i = 0; i < this.TabPages.Count; ++i)
        {
            if (this.Vertical)
            {
                _tabWidths.Add(this.HeaderHeight);
                continue;
            }

            var page = this.TabPages[i];
            var width = (2 * _TabPadding) + g.MeasureText(page.Text, font).Width;
            if (iconWidth > 0 && page.ResolveImageIndex(this.ImageList) >= 0)
                width += iconWidth;
            if (this.ShowCloseButtons)
                width += _CloseZone;

            _tabWidths.Add(width);
        }
    }

    /// <summary>The flow coordinate where the tab area ends (arrows start there when overflowing).</summary>
    private int TabStripEnd => _overflow ? Math.Max(0, this.FlowExtent - (2 * _ArrowWidth)) : this.FlowExtent;

    /// <summary>The tab index under the given flow coordinate, from the last paint's sizes.</summary>
    private int HitTestTab(int flow)
    {
        if (flow >= this.TabStripEnd)
            return -1;

        var end = 0;
        for (var i = _firstVisibleTab; i < _tabWidths.Count; ++i)
        {
            end += _tabWidths[i];
            if (flow < end)
                return i;
        }

        return -1;
    }

    /// <summary>The flow coordinate (x horizontally, y vertically) of a header-strip point.</summary>
    private int FlowOf(Point p) => this.Vertical ? p.Y : p.X;

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left || !this.GetHeaderRect().Contains(e.Location))
            return;

        var flow = this.FlowOf(e.Location);
        if (_overflow && flow >= this.TabStripEnd)
        {
            var scrollForward = flow >= this.FlowExtent - _ArrowWidth;
            _firstVisibleTab = Math.Clamp(_firstVisibleTab + (scrollForward ? 1 : -1), 0, Math.Max(0, this.TabPages.Count - 1));
            this.Invalidate();
            return;
        }

        var hit = this.HitTestTab(flow);
        if (hit < 0)
            return;

        if (this.ShowCloseButtons && CloseBox(this.TabRectOf(hit)).Contains(e.Location))
        {
            this.CloseTab(hit);
            return;
        }

        this.SelectedIndex = hit;
    }

    /// <summary>The on-screen rectangle of a currently visible tab, or <see cref="Rectangle.Empty"/>.</summary>
    private Rectangle TabRectOf(int index)
    {
        if (index < _firstVisibleTab || index >= _tabWidths.Count)
            return Rectangle.Empty;

        var header = this.GetHeaderRect();
        var flow = 0;
        for (var i = _firstVisibleTab; i < index; ++i)
            flow += _tabWidths[i];

        return this.TabRectAt(header, flow, _tabWidths[index]);
    }

    /// <summary>The close-button glyph box at the trailing edge of a tab.</summary>
    private static Rectangle CloseBox(Rectangle tab)
    {
        if (tab.IsEmpty)
            return Rectangle.Empty;

        var x = tab.Right - _CloseZone + ((_CloseZone - _CloseGlyphSize) / 2);
        var y = tab.Y + ((tab.Height - _CloseGlyphSize) / 2);
        return new(x, y, _CloseGlyphSize, _CloseGlyphSize);
    }

    /// <summary>Fires the cancelable close for the tab and removes its page when nothing vetoes it.</summary>
    private void CloseTab(int index)
    {
        var page = this.TabPages[index];
        var closing = new TabPageCancelEventArgs(page, index);
        this.OnTabClosing(closing);
        if (closing.Cancel)
            return;

        this.TabPages.Remove(page);
        this.OnTabClosed(new TabPageEventArgs(page, index));
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = this.GetHeaderRect().Contains(e.Location) ? this.HitTestTab(this.FlowOf(e.Location)) : -1;
        if (hit == _hotTab)
            return;

        _hotTab = hit;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hotTab < 0)
            return;

        _hotTab = -1;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var count = this.TabPages.Count;
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Tab when e.Control && count > 0:
                this.SelectedIndex = e.Shift
                    ? (_selectedIndex - 1 + count) % count
                    : (_selectedIndex + 1) % count;
                break;
            case Keys.Left when _selectedIndex > 0:
                this.SelectedIndex = _selectedIndex - 1;
                break;
            case Keys.Right when _selectedIndex < count - 1:
                this.SelectedIndex = _selectedIndex + 1;
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var header = this.GetHeaderRect();
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        g.FillRectangle(theme.HeaderBackground, header);
        this.PaintStripBorder(g, theme, header);

        this.MeasureTabs(g);
        var totalFlow = 0;
        for (var i = 0; i < _tabWidths.Count; ++i)
            totalFlow += _tabWidths[i];

        _overflow = totalFlow > this.FlowExtent;
        if (!_overflow)
            _firstVisibleTab = 0;

        var stripEnd = this.TabStripEnd;
        g.PushClip(this.Vertical
            ? new Rectangle(header.X, 0, header.Width, stripEnd)
            : new Rectangle(0, header.Y, stripEnd, header.Height));

        var flow = 0;
        for (var i = _firstVisibleTab; i < _tabWidths.Count && flow < stripEnd; ++i)
        {
            this.PaintTab(g, theme, i, this.TabRectAt(header, flow, _tabWidths[i]));
            flow += _tabWidths[i];
        }

        g.PopClip();

        if (_overflow)
            this.PaintOverflowArrows(g, theme, header, stripEnd);
    }

    /// <summary>The rectangle a tab of the given flow size occupies at the given flow offset in the strip.</summary>
    private Rectangle TabRectAt(Rectangle header, int flowStart, int flowSize)
        => this.Vertical
            ? new(header.X, flowStart, header.Width, flowSize)
            : new(flowStart, header.Y, flowSize, header.Height);

    /// <summary>Draws the divider between the strip and the content, along the strip's content-facing edge.</summary>
    private void PaintStripBorder(IGraphics g, ITheme theme, Rectangle header)
    {
        switch (_alignment)
        {
            case TabAlignment.Top:
                g.DrawLine(theme.Border, 0, header.Bottom - 1, this.Width - 1, header.Bottom - 1);
                break;
            case TabAlignment.Bottom:
                g.DrawLine(theme.Border, 0, header.Y, this.Width - 1, header.Y);
                break;
            case TabAlignment.Left:
                g.DrawLine(theme.Border, header.Right - 1, 0, header.Right - 1, this.Height - 1);
                break;
            default:
                g.DrawLine(theme.Border, header.X, 0, header.X, this.Height - 1);
                break;
        }
    }

    private void PaintTab(IGraphics g, ITheme theme, int index, Rectangle tab)
    {
        var active = index == _selectedIndex;
        if (active || index == _hotTab)
            g.FillRectangle(theme.ControlBackground, tab);

        if (active)
            g.FillRectangle(theme.Accent, this.AccentRect(tab));

        var page = this.TabPages[index];
        var textLeft = tab.X + _TabPadding;
        var imageIndex = page.ResolveImageIndex(this.ImageList);
        if (this.ImageList is { } images && imageIndex >= 0 && imageIndex < images.Count && this.Backend is { } backend)
        {
            var iconSize = images.ImageSize;
            var iconTop = tab.Y + ((tab.Height - iconSize.Height) / 2);
            g.DrawImage(images.GetImage(imageIndex, backend), new Rectangle(textLeft, iconTop, iconSize.Width, iconSize.Height));
            textLeft += iconSize.Width + _IconGap;
        }

        var textRight = tab.Right - _TabPadding - (this.ShowCloseButtons ? _CloseZone : 0);
        var textRect = new Rectangle(textLeft, tab.Y, textRight - textLeft, tab.Height);
        g.DrawText(page.Text, theme.DefaultFont, active ? theme.ControlText : theme.HeaderText, textRect, ContentAlignment.MiddleLeft);

        if (this.ShowCloseButtons)
        {
            var box = CloseBox(tab);
            var ink = active ? theme.ControlText : theme.HeaderText;
            g.DrawLine(ink, box.Left, box.Top, box.Right, box.Bottom);
            g.DrawLine(ink, box.Left, box.Bottom, box.Right, box.Top);
        }
    }

    /// <summary>The accent bar for the active tab, on the edge that faces the content area.</summary>
    private Rectangle AccentRect(Rectangle tab) => _alignment switch
    {
        TabAlignment.Top => new(tab.X, tab.Bottom - _UnderlineThickness, tab.Width, _UnderlineThickness),
        TabAlignment.Bottom => new(tab.X, tab.Y, tab.Width, _UnderlineThickness),
        TabAlignment.Left => new(tab.Right - _UnderlineThickness, tab.Y, _UnderlineThickness, tab.Height),
        _ => new(tab.X, tab.Y, _UnderlineThickness, tab.Height),
    };

    private void PaintOverflowArrows(IGraphics g, ITheme theme, Rectangle header, int stripEnd)
    {
        var (back, forward) = this.Vertical ? (GlyphDirection.Up, GlyphDirection.Down) : (GlyphDirection.Left, GlyphDirection.Right);
        Glyphs.PaintTriangle(g, theme.ControlText, this.ArrowGlyphRect(header, stripEnd), back);
        Glyphs.PaintTriangle(g, theme.ControlText, this.ArrowGlyphRect(header, stripEnd + _ArrowWidth), forward);
    }

    /// <summary>Centers an arrow glyph in the <see cref="_ArrowWidth"/> cell that begins at the given flow offset.</summary>
    private Rectangle ArrowGlyphRect(Rectangle header, int flowStart)
    {
        var glyphInset = (_ArrowWidth - _ArrowGlyphSize) / 2;
        return this.Vertical
            ? new(header.X + ((header.Width - _ArrowGlyphSize) / 2), flowStart + glyphInset, _ArrowGlyphSize, _ArrowGlyphSize)
            : new(flowStart + glyphInset, header.Y + ((header.Height - _ArrowGlyphSize) / 2), _ArrowGlyphSize, _ArrowGlyphSize);
    }
}

/// <summary>
/// The ordered set of pages owned by a <see cref="TabControl"/>. Adding a page parents it into the
/// tab control (realizing it immediately when the control is live); removing it tears its peer tree
/// down and hands the selection to a neighboring page.
/// </summary>
public sealed class TabPageCollection : IReadOnlyList<TabPage>
{
    private readonly TabControl _owner;
    private readonly List<TabPage> _pages = [];

    internal TabPageCollection(TabControl owner) => _owner = owner;

    /// <summary>The number of pages.</summary>
    public int Count => _pages.Count;

    /// <summary>The page at the given index.</summary>
    public TabPage this[int index] => _pages[index];

    /// <summary>The index of the page, or -1 when it is not part of this control.</summary>
    public int IndexOf(TabPage page) => _pages.IndexOf(page);

    /// <summary>Appends a page; the first page added becomes the selected one.</summary>
    public void Add(TabPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _pages.Add(page);
        _owner.OnPageAdded(page);
    }

    /// <summary>Registers a page that arrived through <c>Controls.Add(tabPage)</c> — it is already a
    /// child control, so only the page list itself needs the entry.</summary>
    internal void Adopt(TabPage page) => _pages.Add(page);

    /// <summary>Appends several pages in order.</summary>
    public void AddRange(params TabPage[] pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        foreach (var page in pages)
            this.Add(page);
    }

    /// <summary>Removes a page, disposing its peer tree. Returns whether it was present.</summary>
    public bool Remove(TabPage page)
    {
        var index = _pages.IndexOf(page);
        if (index < 0)
            return false;

        _pages.RemoveAt(index);
        _owner.OnPageRemoved(page, index);
        return true;
    }

    /// <summary>Removes every page.</summary>
    public void Clear()
    {
        while (_pages.Count > 0)
            this.Remove(_pages[^1]);
    }

    /// <inheritdoc/>
    public IEnumerator<TabPage> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
