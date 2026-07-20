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
/// The header sits at the top only for now; <c>Alignment</c> (bottom/left/right) and per-tab close
/// buttons are pending. Header hit zones come from the most recent paint, which is also when tab
/// captions are measured through <see cref="IGraphics.MeasureText"/>.
/// </remarks>
public class TabControl : OwnerDrawnControl
{
    private const int _TabPadding = 10;
    private const int _IconGap = 4;
    private const int _HeaderChrome = 6;
    private const int _UnderlineThickness = 2;
    private const int _ArrowWidth = 16;
    private const int _ArrowGlyphSize = 8;

    private readonly List<int> _tabWidths = [];
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

            field = value;
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

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>The pixel height of the header strip.</summary>
    public int HeaderHeight => this.Theme.RowHeight + _HeaderChrome;

    /// <summary>Whether the last paint needed the header scroll arrows (tabs overflow the width).</summary>
    internal bool ShowsOverflowArrows => _overflow;

    /// <summary>The index of the leftmost tab currently shown in the header strip.</summary>
    internal int FirstVisibleTab => _firstVisibleTab;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>The client rectangle below the header strip that pages fill.</summary>
    private Rectangle GetContentArea() => new(0, this.HeaderHeight, this.Width, Math.Max(0, this.Height - this.HeaderHeight));

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

        // The theme (and with it the header height) is only known now — refresh the page bounds
        // before the pages themselves realize.
        for (var i = 0; i < this.TabPages.Count; ++i)
            this.TabPages[i].Bounds = this.GetContentArea();
    }

    /// <summary>Re-applies the content-area bounds to every page — the tab control owns its pages'
    /// bounds, so the base Anchor/Dock engine is replaced wholesale. Each page then lays out its
    /// own children per their Anchor/Dock like any plain container.</summary>
    private protected override void OnLayout()
    {
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

    /// <summary>Recomputes the cached per-tab header widths from the current captions and icons.</summary>
    private void MeasureTabs(IGraphics g)
    {
        _tabWidths.Clear();
        var font = this.Theme.DefaultFont;
        var iconWidth = this.ImageList is { } images ? images.ImageSize.Width + _IconGap : 0;
        for (var i = 0; i < this.TabPages.Count; ++i)
        {
            var page = this.TabPages[i];
            var width = (2 * _TabPadding) + g.MeasureText(page.Text, font).Width;
            if (iconWidth > 0 && page.ImageIndex >= 0)
                width += iconWidth;

            _tabWidths.Add(width);
        }
    }

    /// <summary>The x-coordinate where the tab strip ends (arrows start there when overflowing).</summary>
    private int GetTabStripRight() => _overflow ? Math.Max(0, this.Width - (2 * _ArrowWidth)) : this.Width;

    /// <summary>The tab index under the given header x-coordinate, from the last paint's widths.</summary>
    private int HitTestTab(int x)
    {
        if (x >= this.GetTabStripRight())
            return -1;

        var right = 0;
        for (var i = _firstVisibleTab; i < _tabWidths.Count; ++i)
        {
            right += _tabWidths[i];
            if (x < right)
                return i;
        }

        return -1;
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left || e.Y >= this.HeaderHeight)
            return;

        if (_overflow && e.X >= this.GetTabStripRight())
        {
            var scrollRight = e.X >= this.Width - _ArrowWidth;
            _firstVisibleTab = Math.Clamp(_firstVisibleTab + (scrollRight ? 1 : -1), 0, Math.Max(0, this.TabPages.Count - 1));
            this.Invalidate();
            return;
        }

        var hit = this.HitTestTab(e.X);
        if (hit >= 0)
            this.SelectedIndex = hit;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = e.Y < this.HeaderHeight ? this.HitTestTab(e.X) : -1;
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
        var headerHeight = this.HeaderHeight;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, this.Width, headerHeight));
        g.DrawLine(theme.Border, 0, headerHeight - 1, this.Width - 1, headerHeight - 1);

        this.MeasureTabs(g);
        var totalWidth = 0;
        for (var i = 0; i < _tabWidths.Count; ++i)
            totalWidth += _tabWidths[i];

        _overflow = totalWidth > this.Width;
        if (!_overflow)
            _firstVisibleTab = 0;

        var stripRight = this.GetTabStripRight();
        g.PushClip(new Rectangle(0, 0, stripRight, headerHeight));
        var x = 0;
        for (var i = _firstVisibleTab; i < _tabWidths.Count && x < stripRight; ++i)
        {
            this.PaintTab(g, theme, i, x, headerHeight);
            x += _tabWidths[i];
        }

        g.PopClip();

        if (_overflow)
            this.PaintOverflowArrows(g, theme, stripRight, headerHeight);
    }

    private void PaintTab(IGraphics g, ITheme theme, int index, int x, int headerHeight)
    {
        var width = _tabWidths[index];
        var active = index == _selectedIndex;
        if (active || index == _hotTab)
            g.FillRectangle(theme.ControlBackground, new Rectangle(x, 0, width, headerHeight));

        if (active)
            g.FillRectangle(theme.Accent, new Rectangle(x, headerHeight - _UnderlineThickness, width, _UnderlineThickness));

        var page = this.TabPages[index];
        var textLeft = x + _TabPadding;
        if (this.ImageList is { } images && page.ImageIndex >= 0 && page.ImageIndex < images.Count && this.Backend is { } backend)
        {
            var iconSize = images.ImageSize;
            var iconTop = (headerHeight - iconSize.Height) / 2;
            g.DrawImage(images.GetImage(page.ImageIndex, backend), new Rectangle(textLeft, iconTop, iconSize.Width, iconSize.Height));
            textLeft += iconSize.Width + _IconGap;
        }

        var textRect = new Rectangle(textLeft, 0, x + width - _TabPadding - textLeft, headerHeight);
        g.DrawText(page.Text, theme.DefaultFont, active ? theme.ControlText : theme.HeaderText, textRect, ContentAlignment.MiddleLeft);
    }

    private void PaintOverflowArrows(IGraphics g, ITheme theme, int stripRight, int headerHeight)
    {
        var glyphTop = (headerHeight - _ArrowGlyphSize) / 2;
        var glyphInset = (_ArrowWidth - _ArrowGlyphSize) / 2;
        Glyphs.PaintTriangle(
            g,
            theme.ControlText,
            new Rectangle(stripRight + glyphInset, glyphTop, _ArrowGlyphSize, _ArrowGlyphSize),
            GlyphDirection.Left);
        Glyphs.PaintTriangle(
            g,
            theme.ControlText,
            new Rectangle(stripRight + _ArrowWidth + glyphInset, glyphTop, _ArrowGlyphSize, _ArrowGlyphSize),
            GlyphDirection.Right);
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
