using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An Office-style ribbon (§7.9): a strip of <see cref="RibbonTab"/>s across the top, each showing its
/// <see cref="RibbonGroup"/>s side by side — every group a framed box with its caption running along
/// the bottom edge. Items come in two sizes: <see cref="RibbonItemSize.Large"/> (big icon over the
/// caption, full group height) and <see cref="RibbonItemSize.Small"/> (small icon beside the caption,
/// three stacked per column), and that stacking is what makes a ribbon read as a ribbon. Groups that
/// no longer fit collapse into a single drop-down button, and <see cref="Minimized"/> folds the group
/// area away entirely, leaving the tabs.
/// </summary>
/// <remarks>
/// Items are <see cref="ToolStripItem"/>s, not controls: the ribbon lays them out and paints them, so
/// a hundred buttons cost a hundred small objects rather than a hundred native widgets. A
/// <see cref="RibbonHostItem"/> is the exception — it hosts a real <see cref="Control"/>, which the
/// ribbon parents into itself and whose peer it hides while the owning tab is unselected, the ribbon
/// is minimized or the group has collapsed. Every measured caption width is cached on its item and
/// keyed by the theme font, so the pointer path never re-measures text.
/// </remarks>
public class Ribbon : OwnerDrawnControl
{
    /// <summary>Horizontal padding of a tab caption in the strip.</summary>
    private const int _TabPadding = 12;

    /// <summary>Extra height the tab strip carries over a plain theme row.</summary>
    private const int _TabChrome = 4;

    /// <summary>Thickness of the accent underline under the selected tab.</summary>
    private const int _UnderlineThickness = 2;

    /// <summary>Padding between a group's frame and its content.</summary>
    private const int _GroupPadding = 4;

    /// <summary>Gap between two adjacent groups.</summary>
    private const int _GroupGap = 2;

    /// <summary>Width a group folds down to once it has collapsed into its drop-down button.</summary>
    private const int _CollapsedGroupWidth = 68;

    /// <summary>Edge length of the icon on a large item.</summary>
    private const int _LargeIconSize = 32;

    /// <summary>Edge length of the icon on a small item.</summary>
    private const int _SmallIconSize = 16;

    /// <summary>Horizontal padding inside an item, and the gap between its icon and caption.</summary>
    private const int _ItemPadding = 4;

    /// <summary>The narrowest a large item may be, so a one-word caption still reads as a column.</summary>
    private const int _MinLargeItemWidth = 44;

    /// <summary>How many small items stack into one column.</summary>
    private const int _SmallRowsPerColumn = 3;

    private MenuDropDown? _dropDown;

    /// <summary>The theme font every cached caption width was measured with; a different snapshot
    /// voids them all. Held once per ribbon rather than once per item, so two hundred buttons carry
    /// one font key between them.</summary>
    private Font _measuredFont;

    private int _selectedIndex = -1;
    private int _hotTab = -1;
    private int _hotGroup = -1;
    private int _hotItem = -1;
    private int _pressedGroup = -1;
    private int _pressedItem = -1;

    /// <summary>Creates an empty ribbon.</summary>
    public Ribbon() => this.Tabs = new(this);

    /// <summary>The tabs, left to right. The first one added becomes the selected one.</summary>
    public RibbonTabCollection Tabs { get; }

    /// <summary>The icons <see cref="RibbonGroup.ImageIndex"/> and the items' image indices point
    /// into, or <see langword="null"/>.</summary>
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

    /// <summary>The index of the selected tab, or -1 while there are no tabs.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < 0 || value >= this.Tabs.Count ? -1 : value;
            if (clamped == _selectedIndex)
                return;

            _selectedIndex = clamped;
            _hotGroup = _hotItem = _pressedGroup = _pressedItem = -1;
            this.PerformLayout();
            this.PushHostedVisibility();
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected tab, or <see langword="null"/> while there are no tabs.</summary>
    public RibbonTab? SelectedTab
    {
        get => _selectedIndex >= 0 && _selectedIndex < this.Tabs.Count ? this.Tabs[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.Tabs.IndexOf(value);
    }

    /// <summary>
    /// Whether the group area is folded away, leaving only the tab strip — the Office "minimize the
    /// ribbon" state. Hosted controls go with it; the tabs stay clickable.
    /// </summary>
    public bool Minimized
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
            this.PushHostedVisibility();
            this.Invalidate();
            this.MinimizedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised after <see cref="Minimized"/> changes.</summary>
    public event EventHandler? MinimizedChanged;

    /// <summary>The pixel height of the tab strip along the top.</summary>
    public int TabStripHeight => this.Theme.RowHeight + _TabChrome;

    /// <summary>The pixel height of the group area below the tab strip; zero while minimized.</summary>
    public int GroupAreaHeight => this.Minimized ? 0 : Math.Max(0, this.Height - this.TabStripHeight);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    // --- Collection plumbing ----------------------------------------------------------------------

    /// <summary>Called by <see cref="RibbonTabCollection.Add"/> after the tab joined the list.</summary>
    internal void OnTabAdded(RibbonTab tab)
    {
        if (_selectedIndex < 0)
            _selectedIndex = this.Tabs.Count - 1;

        this.NotifyTabChanged();
    }

    /// <summary>Called by <see cref="RibbonTabCollection"/> after the tab left the list.</summary>
    internal void OnTabRemoved(RibbonTab tab, int index)
    {
        for (var g = 0; g < tab.Groups.Count; ++g)
        {
            var group = tab.Groups[g];
            for (var i = 0; i < group.Items.Count; ++i)
                if (group.Items[i] is RibbonHostItem host)
                {
                    host.Placed = false;
                    this.Controls.Remove(host.Control);
                }
        }

        if (this.Tabs.Count == 0)
            _selectedIndex = -1;
        else if (index <= _selectedIndex)
            _selectedIndex = Math.Clamp(_selectedIndex - 1, 0, this.Tabs.Count - 1);

        this.NotifyTabChanged();
    }

    /// <summary>A tab, group or item changed: adopt any new hosted control, re-lay out, repaint.</summary>
    internal void NotifyTabChanged()
    {
        this.SyncHostedControls();
        this.PerformLayout();
        this.PushHostedVisibility();
        this.Invalidate();
    }

    /// <summary>Parents every hosted control that is not a child of this ribbon yet.</summary>
    private void SyncHostedControls()
    {
        for (var t = 0; t < this.Tabs.Count; ++t)
        {
            var tab = this.Tabs[t];
            for (var g = 0; g < tab.Groups.Count; ++g)
            {
                var group = tab.Groups[g];
                for (var i = 0; i < group.Items.Count; ++i)
                    if (group.Items[i] is RibbonHostItem host && !ReferenceEquals(host.Control.Parent, this))
                        this.Controls.Add(host.Control);
            }
        }
    }

    /// <summary>Re-pushes the peer visibility of every hosted control through this ribbon's veto.</summary>
    private void PushHostedVisibility()
    {
        if (this.ChildrenOrNull is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
            children[i].PushPeerVisibleTree();
    }

    /// <summary>
    /// A hosted control is on screen only while the layout actually placed it — its tab is selected,
    /// the ribbon is not minimized and its group has not collapsed into a drop-down. Combined with the
    /// child's <em>own</em> flag, never with the effective <see cref="Control.Visible"/>, for the same
    /// reason <see cref="Expander"/> gives: that getter walks the ancestor chain.
    /// </summary>
    private protected override bool GetChildPeerVisible(Control child)
        => this.FindHost(child) is { } host ? host.Placed && child.IsVisibleLocal : child.IsVisibleLocal;

    /// <summary>The host item a child control belongs to, or <see langword="null"/> when the control
    /// was parented some other way.</summary>
    private RibbonHostItem? FindHost(Control child)
    {
        for (var t = 0; t < this.Tabs.Count; ++t)
        {
            var tab = this.Tabs[t];
            for (var g = 0; g < tab.Groups.Count; ++g)
            {
                var group = tab.Groups[g];
                for (var i = 0; i < group.Items.Count; ++i)
                    if (group.Items[i] is RibbonHostItem host && ReferenceEquals(host.Control, child))
                        return host;
            }
        }

        return null;
    }

    // --- Measurement ------------------------------------------------------------------------------

    /// <summary>
    /// The theme font measurements are taken with, dropping every cached width when the snapshot has
    /// moved (a theme change, a DPI change). Every measuring path goes through here, so a stale
    /// width cannot outlive the font it was taken with.
    /// </summary>
    private Font MeasurementFont()
    {
        var font = this.Theme.DefaultFont;
        if (_measuredFont.Equals(font))
            return font;

        _measuredFont = font;
        for (var i = 0; i < this.Tabs.Count; ++i)
            this.Tabs[i].InvalidateMeasurements();

        return font;
    }

    /// <summary>The pixel width of one item at its own size.</summary>
    private int ItemWidth(RibbonItem item)
    {
        var font = this.MeasurementFont();
        if (item is RibbonHostItem host)
            return host.HostWidth + (2 * _ItemPadding);

        var text = item.TextWidth(this.Backend, font);
        return item.ItemSize == RibbonItemSize.Large
            ? Math.Max(_MinLargeItemWidth, text + (2 * _ItemPadding))
            : (2 * _ItemPadding) + _SmallIconSize + _ItemPadding + text;
    }

    /// <summary>
    /// Collects the next column of a group into <paramref name="slots"/>: either one large item or up
    /// to three small ones. <paramref name="cursor"/> is advanced past everything consumed (invisible
    /// items included), and the return value is the column's pixel width.
    /// </summary>
    private int ScanColumn(RibbonGroup group, ref int cursor, Span<int> slots, out int count, out bool isLarge)
    {
        count = 0;
        isLarge = false;
        var items = group.Items;
        while (cursor < items.Count && (items[cursor] is not RibbonItem || !items[cursor].Visible))
            ++cursor;

        if (cursor >= items.Count)
            return 0;

        var first = (RibbonItem)items[cursor];
        if (first.ItemSize == RibbonItemSize.Large)
        {
            slots[0] = cursor++;
            count = 1;
            isLarge = true;
            return this.ItemWidth(first);
        }

        var width = 0;
        while (cursor < items.Count && count < _SmallRowsPerColumn)
        {
            if (items[cursor] is not RibbonItem item || !item.Visible)
            {
                ++cursor;
                continue;
            }

            if (item.ItemSize == RibbonItemSize.Large)
                break;

            width = Math.Max(width, this.ItemWidth(item));
            slots[count++] = cursor++;
        }

        return width;
    }

    /// <summary>The natural pixel width of a group — its columns, or its caption when that is wider.</summary>
    private int GroupWidth(RibbonGroup group)
    {
        Span<int> slots = stackalloc int[_SmallRowsPerColumn];
        var cursor = 0;
        var content = 0;
        while (cursor < group.Items.Count)
        {
            var width = this.ScanColumn(group, ref cursor, slots, out var count, out _);
            if (count == 0)
                break;

            content += width;
        }

        var caption = group.CaptionWidth(this.Backend, this.MeasurementFont());
        return Math.Max(content, caption) + (2 * _GroupPadding);
    }

    /// <summary>The height available to a group's items, above its caption strip.</summary>
    private int GroupContentHeight()
        => Math.Max(0, this.GroupAreaHeight - this.CaptionStripHeight() - (2 * _GroupPadding));

    /// <summary>The height of the caption strip along a group's bottom edge.</summary>
    private int CaptionStripHeight() => Math.Max(12, this.Theme.RowHeight - 6);

    // --- Layout -----------------------------------------------------------------------------------

    /// <summary>
    /// Assigns every group of the selected tab its rectangle, decides which ones have to collapse and
    /// positions the hosted controls. Idempotent and allocation-free, so both the layout pass and the
    /// paint path can run it and cannot disagree about where anything sits.
    /// </summary>
    private void ArrangeGroups()
    {
        // Every hosted control starts each pass unplaced; only the ones this tab actually lays out
        // earn their peer back.
        for (var t = 0; t < this.Tabs.Count; ++t)
        {
            var other = this.Tabs[t];
            for (var g = 0; g < other.Groups.Count; ++g)
            {
                var group = other.Groups[g];
                for (var i = 0; i < group.Items.Count; ++i)
                    if (group.Items[i] is RibbonHostItem host)
                        host.Placed = false;
            }
        }

        if (this.SelectedTab is not { } tab)
            return;

        var groups = tab.Groups;
        if (this.Minimized)
        {
            for (var g = 0; g < groups.Count; ++g)
            {
                groups[g].IsCollapsed = false;
                groups[g].Bounds = Rectangle.Empty;
            }

            return;
        }

        var available = this.Width;
        var total = 0;
        for (var g = 0; g < groups.Count; ++g)
        {
            groups[g].IsCollapsed = false;
            total += this.GroupWidth(groups[g]) + _GroupGap;
        }

        // Office folds the rightmost groups first; each one that goes shrinks to the chevron button.
        for (var g = groups.Count - 1; g >= 0 && total > available; --g)
        {
            var natural = this.GroupWidth(groups[g]);
            if (natural <= _CollapsedGroupWidth)
                continue;

            groups[g].IsCollapsed = true;
            total -= natural - _CollapsedGroupWidth;
        }

        var areaHeight = this.GroupAreaHeight;
        var top = this.TabStripHeight;
        var x = 0;
        for (var g = 0; g < groups.Count; ++g)
        {
            var group = groups[g];
            var width = group.IsCollapsed ? _CollapsedGroupWidth : this.GroupWidth(group);
            group.Bounds = new(x, top, width, areaHeight);
            x += width + _GroupGap;

            if (!group.IsCollapsed)
                this.PlaceHostedControls(group);
        }
    }

    /// <summary>Gives every hosted control of a laid-out group its bounds and marks it placed.</summary>
    private void PlaceHostedControls(RibbonGroup group)
    {
        Span<int> slots = stackalloc int[_SmallRowsPerColumn];
        var contentHeight = this.GroupContentHeight();
        var rowHeight = contentHeight / _SmallRowsPerColumn;
        var cursor = 0;
        var x = group.Bounds.X + _GroupPadding;
        var top = group.Bounds.Y + _GroupPadding;
        while (cursor < group.Items.Count)
        {
            var width = this.ScanColumn(group, ref cursor, slots, out var count, out var isLarge);
            if (count == 0)
                break;

            for (var j = 0; j < count; ++j)
            {
                if (group.Items[slots[j]] is not RibbonHostItem host)
                    continue;

                var bounds = isLarge
                    ? new Rectangle(x + _ItemPadding, top, width - (2 * _ItemPadding), contentHeight)
                    : new Rectangle(x + _ItemPadding, top + (j * rowHeight), width - (2 * _ItemPadding), rowHeight);

                host.Control.Bounds = bounds;
                host.Placed = true;
            }

            x += width;
        }
    }

    /// <inheritdoc/>
    private protected override void OnLayout()
    {
        this.ArrangeGroups();

        // The arrangement is what decides whether a hosted control is on screen, so its verdict has
        // to reach the peers in the same pass. Without this a resize that collapses a group leaves
        // that group's hosted control painted at its old place — Visible reports false, because the
        // getter asks the veto live, while the widget is still up. The property and the pixels must
        // not be allowed to disagree.
        this.PushHostedVisibility();
        this.Invalidate();
    }

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);

        // The theme — and with it every metric the arrangement is built on — is only known now, and
        // so is the backend every caption width has to be measured through.
        for (var i = 0; i < this.Tabs.Count; ++i)
            this.Tabs[i].InvalidateMeasurements();

        this.PerformLayout();
        this.PushHostedVisibility();
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _dropDown?.CloseAll();
        _dropDown = null;
    }

    /// <summary>The lazily created drop-down engine a collapsed group opens into, with its owning
    /// window refreshed on every access so each popup is anchored to the current form.</summary>
    private MenuDropDown Engine
    {
        get
        {
            var engine = _dropDown ??= new(this.Backend!, this.Theme);
            engine.Owner = this.OwnerWindowPeer;
            return engine;
        }
    }

    // --- Hit testing ------------------------------------------------------------------------------

    /// <summary>The tab index under header x-coordinate <paramref name="x"/>, or -1.</summary>
    private int HitTestTab(int x)
    {
        var font = this.MeasurementFont();
        var right = 0;
        for (var i = 0; i < this.Tabs.Count; ++i)
        {
            right += this.Tabs[i].TextWidth(this.Backend, font) + (2 * _TabPadding);
            if (x < right)
                return i;
        }

        return -1;
    }

    /// <summary>The index of the group under client point, or -1.</summary>
    private int HitTestGroup(int x, int y)
    {
        if (this.SelectedTab is not { } tab || this.Minimized)
            return -1;

        for (var g = 0; g < tab.Groups.Count; ++g)
            if (tab.Groups[g].Bounds.Contains(x, y))
                return g;

        return -1;
    }

    /// <summary>The index of the item within a group under a client point, or -1.</summary>
    private int HitTestItem(RibbonGroup group, int x, int y)
    {
        if (group.IsCollapsed)
            return -1;

        Span<int> slots = stackalloc int[_SmallRowsPerColumn];
        var contentHeight = this.GroupContentHeight();
        var rowHeight = contentHeight / _SmallRowsPerColumn;
        var cursor = 0;
        var left = group.Bounds.X + _GroupPadding;
        var top = group.Bounds.Y + _GroupPadding;
        while (cursor < group.Items.Count)
        {
            var width = this.ScanColumn(group, ref cursor, slots, out var count, out var isLarge);
            if (count == 0)
                break;

            if (x >= left && x < left + width)
                for (var j = 0; j < count; ++j)
                {
                    var bounds = isLarge
                        ? new Rectangle(left, top, width, contentHeight)
                        : new Rectangle(left, top + (j * rowHeight), width, rowHeight);

                    if (bounds.Contains(x, y))
                        return slots[j];
                }

            left += width;
        }

        return -1;
    }

    // --- Input ------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (e.Y < this.TabStripHeight)
        {
            var tab = this.HitTestTab(e.X);
            if (tab >= 0)
                this.SelectedIndex = tab;

            return;
        }

        var groupIndex = this.HitTestGroup(e.X, e.Y);
        if (groupIndex < 0 || this.SelectedTab is not { } selected)
            return;

        var group = selected.Groups[groupIndex];
        if (group.IsCollapsed)
        {
            this.OpenGroupDropDown(group);
            return;
        }

        var itemIndex = this.HitTestItem(group, e.X, e.Y);
        if (itemIndex < 0 || group.Items[itemIndex] is RibbonHostItem || !group.Items[itemIndex].Enabled)
            return;

        _pressedGroup = groupIndex;
        _pressedItem = itemIndex;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var pressedGroup = _pressedGroup;
        var pressedItem = _pressedItem;
        if (pressedGroup < 0 || pressedItem < 0)
            return;

        _pressedGroup = _pressedItem = -1;
        this.Invalidate();
        if (e.Button != MouseButtons.Left || this.SelectedTab is not { } selected)
            return;

        var group = selected.Groups[pressedGroup];
        if (this.HitTestGroup(e.X, e.Y) == pressedGroup && this.HitTestItem(group, e.X, e.Y) == pressedItem)
            group.Items[pressedItem].PerformClick();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var tab = e.Y < this.TabStripHeight ? this.HitTestTab(e.X) : -1;
        var groupIndex = this.HitTestGroup(e.X, e.Y);
        var itemIndex = groupIndex >= 0 && this.SelectedTab is { } selected
            ? this.HitTestItem(selected.Groups[groupIndex], e.X, e.Y)
            : -1;

        if (tab == _hotTab && groupIndex == _hotGroup && itemIndex == _hotItem)
            return;

        _hotTab = tab;
        _hotGroup = groupIndex;
        _hotItem = itemIndex;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hotTab < 0 && _hotGroup < 0 && _hotItem < 0 && _pressedGroup < 0)
            return;

        _hotTab = _hotGroup = _hotItem = _pressedGroup = _pressedItem = -1;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var count = this.Tabs.Count;
        if (count == 0)
            return;

        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Tab when e.Control:
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

    /// <summary>Opens a collapsed group's items as a popup menu under its button.</summary>
    private void OpenGroupDropDown(RibbonGroup group)
    {
        if (this.Backend is null || group.Items.Count == 0)
            return;

        this.Engine.Open(group.Items, this.PointToScreen(new(group.Bounds.X, group.Bounds.Bottom)));
    }

    // --- Painting ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, width, this.Height));

        var stripHeight = this.TabStripHeight;
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, width, stripHeight));
        g.DrawLine(theme.Border, 0, stripHeight - 1, width - 1, stripHeight - 1);
        this.PaintTabStrip(g, theme, stripHeight);

        if (this.Minimized || this.SelectedTab is not { } tab)
            return;

        // Paint reruns the arrangement rather than trusting a stale one: the theme metrics the
        // layout pass used may predate realization, and this is the first place they are certainly
        // final. It is idempotent and allocates nothing, so running it here costs only arithmetic.
        this.ArrangeGroups();

        var contentHeight = this.GroupContentHeight();
        var captionHeight = this.CaptionStripHeight();
        for (var i = 0; i < tab.Groups.Count; ++i)
            this.PaintGroup(g, theme, tab.Groups[i], i, contentHeight, captionHeight);
    }

    /// <summary>Paints the tab captions, the accent underline and the hover feedback.</summary>
    private void PaintTabStrip(IGraphics g, ITheme theme, int stripHeight)
    {
        // The strip paints before the groups are arranged, so this is the first measuring call of the
        // frame — it has to be the one that notices a font change.
        var font = this.MeasurementFont();
        var x = 0;
        for (var i = 0; i < this.Tabs.Count; ++i)
        {
            var tab = this.Tabs[i];
            var width = tab.TextWidth(this.Backend, font) + (2 * _TabPadding);
            var active = i == _selectedIndex;
            if (active || i == _hotTab)
                g.FillRectangle(theme.ControlBackground, new Rectangle(x, 0, width, stripHeight));

            if (active)
                g.FillRectangle(theme.Accent, new Rectangle(x, stripHeight - _UnderlineThickness, width, _UnderlineThickness));

            g.DrawText(
                tab.Text,
                font,
                active ? theme.ControlText : theme.HeaderText,
                new Rectangle(x + _TabPadding, 0, Math.Max(0, width - (2 * _TabPadding)), stripHeight),
                ContentAlignment.MiddleLeft);

            x += width;
        }
    }

    /// <summary>Paints one group: its frame, its items (or its collapsed button) and its caption strip.</summary>
    private void PaintGroup(IGraphics g, ITheme theme, RibbonGroup group, int groupIndex, int contentHeight, int captionHeight)
    {
        var bounds = group.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        g.DrawRectangle(theme.Border, new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));

        var captionRect = new Rectangle(bounds.X + 1, bounds.Bottom - captionHeight - 1, bounds.Width - 2, captionHeight);
        g.FillRectangle(theme.HeaderBackground, captionRect);
        g.DrawText(group.Text, theme.DefaultFont, theme.HeaderText, captionRect, ContentAlignment.MiddleCenter);

        if (group.IsCollapsed)
        {
            this.PaintCollapsedGroup(g, theme, group, bounds, contentHeight);
            return;
        }

        Span<int> slots = stackalloc int[_SmallRowsPerColumn];
        var rowHeight = contentHeight / _SmallRowsPerColumn;
        var cursor = 0;
        var x = bounds.X + _GroupPadding;
        var top = bounds.Y + _GroupPadding;
        while (cursor < group.Items.Count)
        {
            var width = this.ScanColumn(group, ref cursor, slots, out var count, out var isLarge);
            if (count == 0)
                break;

            for (var j = 0; j < count; ++j)
            {
                if (group.Items[slots[j]] is not RibbonItem item || item is RibbonHostItem)
                    continue;

                var rect = isLarge
                    ? new Rectangle(x, top, width, contentHeight)
                    : new Rectangle(x, top + (j * rowHeight), width, rowHeight);

                this.PaintItem(g, theme, item, groupIndex, slots[j], rect, isLarge);
            }

            x += width;
        }
    }

    /// <summary>Paints a group that has folded into its drop-down button: icon, caption and chevron.</summary>
    private void PaintCollapsedGroup(IGraphics g, ITheme theme, RibbonGroup group, Rectangle bounds, int contentHeight)
    {
        var face = new Rectangle(bounds.X + _GroupPadding, bounds.Y + _GroupPadding, bounds.Width - (2 * _GroupPadding), contentHeight);
        if (this.ImageList is { } images && group.ImageIndex >= 0 && group.ImageIndex < images.Count && this.Backend is { } backend)
        {
            var size = images.ImageSize;
            g.DrawImage(
                images.GetImage(group.ImageIndex, backend),
                new Rectangle(face.X + ((face.Width - _LargeIconSize) / 2), face.Y + 2, _LargeIconSize, _LargeIconSize));
        }

        Glyphs.PaintTriangle(
            g,
            theme.ControlText,
            new Rectangle(face.X + ((face.Width - 8) / 2), face.Bottom - 8, 8, 5),
            GlyphDirection.Down);
    }

    /// <summary>Paints one item in its current hover/pressed/checked state, large or small.</summary>
    private void PaintItem(IGraphics g, ITheme theme, RibbonItem item, int groupIndex, int itemIndex, Rectangle bounds, bool isLarge)
    {
        var pressed = groupIndex == _pressedGroup && itemIndex == _pressedItem;
        var hovered = groupIndex == _hotGroup && itemIndex == _hotItem && item.Enabled;
        var isChecked = item is RibbonToggleButton { Checked: true };

        if (pressed)
            GlyphRenderer.FillSelection(g, theme, bounds);
        else if (hovered || isChecked)
            g.FillRectangle(theme.HeaderBackground, bounds);

        if (isChecked)
            g.DrawRectangle(theme.Accent, new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));

        var textColor = !item.Enabled ? theme.DisabledText : pressed ? theme.SelectionText : theme.ControlText;
        var icon = item.ResolveImage(this.Backend);
        var font = theme.DefaultFont;

        if (isLarge)
        {
            if (icon is not null)
                g.DrawImage(icon, new Rectangle(bounds.X + ((bounds.Width - _LargeIconSize) / 2), bounds.Y + 2, _LargeIconSize, _LargeIconSize));

            var textTop = bounds.Y + 2 + (icon is not null ? _LargeIconSize + 2 : 0);
            g.DrawText(
                item.DisplayText,
                font,
                textColor,
                new Rectangle(bounds.X, textTop, bounds.Width, Math.Max(0, bounds.Bottom - textTop)),
                ContentAlignment.TopCenter);
            return;
        }

        var x = bounds.X + _ItemPadding;
        if (icon is not null)
        {
            g.DrawImage(icon, new Rectangle(x, bounds.Y + ((bounds.Height - _SmallIconSize) / 2), _SmallIconSize, _SmallIconSize));
            x += _SmallIconSize + _ItemPadding;
        }

        g.DrawText(
            item.DisplayText,
            font,
            textColor,
            new Rectangle(x, bounds.Y, Math.Max(0, bounds.Right - _ItemPadding - x), bounds.Height),
            ContentAlignment.MiddleLeft);
    }
}
