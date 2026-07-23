using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An Outlook-style navigation pane (§7.9): a vertical stack of <see cref="AccordionPane"/>s, each a
/// themed header row — toggle glyph, optional <see cref="ImageList"/> icon, caption, hover/pressed
/// feedback — over a body of ordinary child controls. Headers keep their place; the open panes share
/// whatever height the headers leave. <see cref="ExpandMode"/> decides whether opening a pane closes
/// its siblings.
/// </summary>
/// <remarks>
/// A collapsed pane's peer tree is vetoed through <see cref="GetChildPeerVisible"/> rather than by
/// clearing the children's own flags, exactly like <see cref="Expander"/>: expanding restores the
/// body to precisely what it was, and a pane that lives on a tab page nobody selected stays hidden
/// because the native nesting hides it with its ancestor. Header captions are drawn straight into
/// their row, so nothing on the pointer path ever measures text.
/// </remarks>
public class Accordion : OwnerDrawnControl
{
    private const int _GlyphSize = 8;
    private const int _GlyphInset = 8;
    private const int _Gap = 6;

    private int _selectedIndex = -1;
    private int _focusedIndex;
    private int _hotIndex = -1;
    private int _pressedIndex = -1;

    /// <summary>Creates an empty accordion.</summary>
    public Accordion() => this.Panes = new(this);

    /// <summary>The panes, top to bottom. Adding a pane parents it into <see cref="Control.Controls"/>.</summary>
    public AccordionPaneCollection Panes { get; }

    /// <summary>The icons <see cref="AccordionPane.ImageIndex"/> indexes into, or <see langword="null"/>.</summary>
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
    /// Whether opening a pane closes the others (<see cref="AccordionExpandMode.Single"/>, the
    /// default) or panes toggle independently. Switching to <see cref="AccordionExpandMode.Single"/>
    /// while several panes are open collapses all but the selected one.
    /// </summary>
    public AccordionExpandMode ExpandMode
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (value != AccordionExpandMode.Single)
                return;

            // Fold back to one open pane: the selected one wins, or the topmost open one when the
            // selection is still empty.
            var keep = _selectedIndex >= 0 && _selectedIndex < this.Panes.Count && this.Panes[_selectedIndex].IsExpanded
                ? _selectedIndex
                : this.FirstExpandedIndex();

            for (var i = 0; i < this.Panes.Count; ++i)
                this.Panes[i].IsExpanded = i == keep;

            _selectedIndex = keep;
            this.ApplyPaneState();
        }
    }

    /// <summary>
    /// The index of the current pane — the open one under <see cref="AccordionExpandMode.Single"/>,
    /// the most recently opened one otherwise; -1 while none is open. Assigning expands that pane
    /// through the ordinary path, so <see cref="PaneExpanding"/> can still veto it.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= this.Panes.Count)
                return;

            this.SetPaneExpanded(this.Panes[value], expanded: true);
        }
    }

    /// <summary>The current pane, or <see langword="null"/> while none is open.</summary>
    public AccordionPane? SelectedPane
    {
        get => _selectedIndex >= 0 && _selectedIndex < this.Panes.Count ? this.Panes[_selectedIndex] : null;
        set
        {
            if (value is not null)
                this.SetPaneExpanded(value, expanded: true);
        }
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised before a pane opens; cancelling leaves the whole stack untouched.</summary>
    public event EventHandler<AccordionPaneCancelEventArgs>? PaneExpanding;

    /// <summary>Raised after a pane has opened and the stack has been laid out again.</summary>
    public event EventHandler<AccordionPaneEventArgs>? PaneExpanded;

    /// <summary>Raised after a pane has closed.</summary>
    public event EventHandler<AccordionPaneEventArgs>? PaneCollapsed;

    /// <summary>The pixel height of one header row.</summary>
    public int HeaderHeight => this.Theme.RowHeight;

    /// <summary>The index of the header the keyboard is on.</summary>
    internal int FocusedIndex => _focusedIndex;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="PaneExpanding"/>.</summary>
    protected virtual void OnPaneExpanding(AccordionPaneCancelEventArgs e) => this.PaneExpanding?.Invoke(this, e);

    /// <summary>Raises <see cref="PaneExpanded"/>.</summary>
    protected virtual void OnPaneExpanded(AccordionPaneEventArgs e) => this.PaneExpanded?.Invoke(this, e);

    /// <summary>Raises <see cref="PaneCollapsed"/>.</summary>
    protected virtual void OnPaneCollapsed(AccordionPaneEventArgs e) => this.PaneCollapsed?.Invoke(this, e);

    // --- Expansion --------------------------------------------------------------------------------

    /// <summary>The index of the topmost open pane, or -1 when every pane is closed.</summary>
    private int FirstExpandedIndex()
    {
        for (var i = 0; i < this.Panes.Count; ++i)
            if (this.Panes[i].IsExpanded)
                return i;

        return -1;
    }

    /// <summary>
    /// The single writer of pane expansion: applies <see cref="ExpandMode"/>, raises the cancelable
    /// <see cref="PaneExpanding"/> before anything moves, then re-lays the stack out and re-pushes
    /// the pane peer trees.
    /// </summary>
    internal void SetPaneExpanded(AccordionPane pane, bool expanded)
    {
        var index = this.Panes.IndexOf(pane);
        if (index < 0)
        {
            pane.IsExpanded = expanded;
            return;
        }

        if (pane.IsExpanded == expanded)
        {
            // Re-selecting an already-open pane still moves the selection, which is what assigning
            // SelectedIndex to the open pane in Multiple mode is asking for.
            if (expanded && _selectedIndex != index)
            {
                _selectedIndex = index;
                this.Invalidate();
                this.OnSelectedIndexChanged(EventArgs.Empty);
            }

            return;
        }

        if (expanded)
        {
            var args = new AccordionPaneCancelEventArgs(pane, index);
            this.OnPaneExpanding(args);
            if (args.Cancel)
                return;

            if (this.ExpandMode == AccordionExpandMode.Single)
                for (var i = 0; i < this.Panes.Count; ++i)
                    if (i != index && this.Panes[i].IsExpanded)
                    {
                        this.Panes[i].IsExpanded = false;
                        this.OnPaneCollapsed(new(this.Panes[i], i));
                    }
        }

        pane.IsExpanded = expanded;

        var selectionChanged = false;
        if (expanded)
        {
            selectionChanged = _selectedIndex != index;
            _selectedIndex = index;
        }
        else if (_selectedIndex == index)
        {
            var next = this.FirstExpandedIndex();
            selectionChanged = _selectedIndex != next;
            _selectedIndex = next;
        }

        this.ApplyPaneState();

        if (expanded)
            this.OnPaneExpanded(new(pane, index));
        else
            this.OnPaneCollapsed(new(pane, index));

        if (selectionChanged)
            this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Re-lays the stack out and re-pushes every pane's peer visibility. Bounds first, visibility
    /// second, so an opening pane's children are already in their places before they come back —
    /// the same ordering <see cref="Expander"/> relies on to avoid a flash at the collapsed geometry.
    /// </summary>
    private void ApplyPaneState()
    {
        this.PerformLayout();

        // The whole subtree per pane, not just the pane: the veto is asked per level, so a grandchild
        // inside a closed pane has an answer of its own that nothing else recomputes.
        for (var i = 0; i < this.Panes.Count; ++i)
            this.Panes[i].PushPeerVisibleTree();

        this.Invalidate();
    }

    // --- Geometry ---------------------------------------------------------------------------------

    /// <summary>The height every open pane body shares between them, in total.</summary>
    private int BodyHeight => Math.Max(0, this.Height - (this.Panes.Count * this.HeaderHeight));

    /// <summary>The number of open panes.</summary>
    private int ExpandedCount()
    {
        var count = 0;
        for (var i = 0; i < this.Panes.Count; ++i)
            if (this.Panes[i].IsExpanded)
                ++count;

        return count;
    }

    /// <summary>
    /// The client rectangle of the header row of the pane at <paramref name="index"/>, or
    /// <see cref="Rectangle.Empty"/> for an index outside the stack. Public because aiming a click at
    /// a header is otherwise guesswork for callers — tests and UI automation both need it.
    /// </summary>
    public Rectangle GetHeaderBounds(int index)
    {
        if (index < 0 || index >= this.Panes.Count)
            return Rectangle.Empty;

        var headerHeight = this.HeaderHeight;
        var remaining = this.BodyHeight;
        var left = this.ExpandedCount();
        var y = 0;
        for (var i = 0; i < index; ++i)
        {
            y += headerHeight;
            if (!this.Panes[i].IsExpanded)
                continue;

            var share = left > 0 ? remaining / left : 0;
            remaining -= share;
            --left;
            y += share;
        }

        return new(0, y, this.Width, headerHeight);
    }

    /// <summary>The index of the header row under client y-coordinate <paramref name="y"/>, or -1.</summary>
    private int HitTestHeader(int y)
    {
        var headerHeight = this.HeaderHeight;
        var remaining = this.BodyHeight;
        var left = this.ExpandedCount();
        var top = 0;
        for (var i = 0; i < this.Panes.Count; ++i)
        {
            if (y >= top && y < top + headerHeight)
                return i;

            top += headerHeight;
            if (!this.Panes[i].IsExpanded)
                continue;

            var share = left > 0 ? remaining / left : 0;
            remaining -= share;
            --left;
            top += share;
        }

        return -1;
    }

    /// <inheritdoc/>
    private protected override void OnLayout()
    {
        var headerHeight = this.HeaderHeight;
        var remaining = this.BodyHeight;
        var left = this.ExpandedCount();
        var width = this.Width;
        var y = 0;
        for (var i = 0; i < this.Panes.Count; ++i)
        {
            var pane = this.Panes[i];
            y += headerHeight;
            var share = 0;
            if (pane.IsExpanded)
            {
                share = left > 0 ? remaining / left : 0;
                remaining -= share;
                --left;
            }

            pane.Bounds = new(0, y, width, share);
            y += share;
        }

        this.Invalidate();
    }

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);

        // The theme — and with it the header height — is only known now, so the pane bounds computed
        // from the fallback metrics have to be redone before the panes themselves realize.
        this.PerformLayout();
    }

    /// <summary>
    /// A closed pane hides its body wholesale. Combined with the child's <em>own</em> flag, never with
    /// the effective <see cref="Control.Visible"/>: that getter walks the ancestor chain, so folding
    /// it in here would let a hidden ancestor latch a pane's peer off and leave it off once the
    /// ancestor came back.
    /// </summary>
    private protected override bool GetChildPeerVisible(Control child)
        => child is AccordionPane pane ? pane.IsExpanded && child.IsVisibleLocal : child.IsVisibleLocal;

    // --- Collection plumbing ----------------------------------------------------------------------

    /// <summary>Called by <see cref="AccordionPaneCollection.Add"/> after the pane joined the list.</summary>
    internal void OnPaneAdded(AccordionPane pane)
    {
        this.OnPaneAdopted(pane);
        this.Controls.Add(pane);
    }

    /// <summary>Bookkeeping for a pane that just joined <see cref="Panes"/>, shared by the collection
    /// and the designer-style <c>Controls.Add(pane)</c> route.</summary>
    private void OnPaneAdopted(AccordionPane pane)
    {
        // The first pane opens by itself, so a freshly built accordion is never a stack of shut
        // drawers; later panes join closed under Single mode and keep their authored flag otherwise.
        if (_selectedIndex < 0 && this.ExpandMode == AccordionExpandMode.Single)
        {
            pane.IsExpanded = true;
            _selectedIndex = this.Panes.Count - 1;
        }
        else if (this.ExpandMode == AccordionExpandMode.Single && pane.IsExpanded)
            pane.IsExpanded = false;
        else if (pane.IsExpanded && _selectedIndex < 0)
            _selectedIndex = this.Panes.Count - 1;

        // While unrealized, skip the per-pane layout: OnRealized lays every pane out once, so a bulk
        // build stays linear instead of re-flowing all panes on each add.
        if (this.IsRealized)
            this.PerformLayout();

        this.Invalidate();
    }

    /// <summary>
    /// Routes designer-style <c>Controls.Add(pane)</c> into <see cref="Panes"/>; anything that is not
    /// an <see cref="AccordionPane"/> is rejected — an accordion hosts panes only.
    /// </summary>
    /// <exception cref="InvalidOperationException">The child is not an <see cref="AccordionPane"/>.</exception>
    private protected override void OnChildAdded(Control child)
    {
        if (child is not AccordionPane pane)
        {
            this.Controls.Remove(child);
            throw new InvalidOperationException(
                "Only AccordionPane instances can be added to an Accordion — add panes through Panes or Controls.Add(pane), and put other controls into a pane.");
        }

        if (this.Panes.IndexOf(pane) < 0)
        {
            this.Panes.Adopt(pane);
            this.OnPaneAdopted(pane);
        }

        base.OnChildAdded(child);
    }

    /// <summary>Called by <see cref="AccordionPaneCollection"/> after the pane left the list.</summary>
    internal void OnPaneRemoved(AccordionPane pane, int index)
    {
        this.Controls.Remove(pane);
        pane.IsExpanded = true; // restore the pane's stand-alone default

        if (this.Panes.Count == 0)
            _selectedIndex = -1;
        else if (index < _selectedIndex)
            --_selectedIndex;
        else if (index == _selectedIndex)
            _selectedIndex = this.FirstExpandedIndex();

        _focusedIndex = Math.Clamp(_focusedIndex, 0, Math.Max(0, this.Panes.Count - 1));
        this.PerformLayout();
        this.Invalidate();
    }

    // --- Input ------------------------------------------------------------------------------------

    /// <summary>Toggles a header the way a click on it does: under
    /// <see cref="AccordionExpandMode.Single"/> the open pane stays open, because closing the last
    /// drawer is not what an Outlook-style stack does.</summary>
    private void ToggleHeader(int index)
    {
        if (index < 0 || index >= this.Panes.Count)
            return;

        var pane = this.Panes[index];
        if (pane.IsExpanded && this.ExpandMode == AccordionExpandMode.Single)
            return;

        this.SetPaneExpanded(pane, !pane.IsExpanded);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var hit = this.HitTestHeader(e.Y);
        if (hit < 0)
            return;

        _pressedIndex = hit;
        _focusedIndex = hit;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var pressed = _pressedIndex;
        if (pressed < 0)
            return;

        _pressedIndex = -1;
        this.Invalidate();
        if (e.Button == MouseButtons.Left && this.HitTestHeader(e.Y) == pressed)
            this.ToggleHeader(pressed);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = this.HitTestHeader(e.Y);
        if (hit == _hotIndex)
            return;

        _hotIndex = hit;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hotIndex < 0 && _pressedIndex < 0)
            return;

        _hotIndex = -1;
        _pressedIndex = -1;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Enter or Keys.Space;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var count = this.Panes.Count;
        if (count == 0)
            return;

        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Up:
                _focusedIndex = Math.Max(0, _focusedIndex - 1);
                this.Invalidate();
                break;
            case Keys.Down:
                _focusedIndex = Math.Min(count - 1, _focusedIndex + 1);
                this.Invalidate();
                break;
            case Keys.Home:
                _focusedIndex = 0;
                this.Invalidate();
                break;
            case Keys.End:
                _focusedIndex = count - 1;
                this.Invalidate();
                break;
            case Keys.Enter:
            case Keys.Space:
                this.ToggleHeader(_focusedIndex);
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    // --- Painting ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, width, this.Height));

        var headerHeight = this.HeaderHeight;
        var remaining = this.BodyHeight;
        var left = this.ExpandedCount();
        var focused = this.Focused;
        var y = 0;
        for (var i = 0; i < this.Panes.Count; ++i)
        {
            var pane = this.Panes[i];
            this.PaintHeader(g, theme, i, pane, new Rectangle(0, y, width, headerHeight), focused);
            y += headerHeight;
            if (!pane.IsExpanded)
                continue;

            var share = left > 0 ? remaining / left : 0;
            remaining -= share;
            --left;

            // Frame the open body so the stack reads as drawers rather than as loose rows.
            if (share > 1)
                g.DrawRectangle(theme.Border, new Rectangle(0, y, width - 1, share - 1));

            y += share;
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, this.Height - 1));
    }

    /// <summary>Paints one header row in its current hover/pressed/selected state.</summary>
    private void PaintHeader(IGraphics g, ITheme theme, int index, AccordionPane pane, Rectangle bounds, bool focused)
    {
        var pressed = index == _pressedIndex;
        var selected = index == _selectedIndex && pane.IsExpanded;
        if (pressed)
            GlyphRenderer.FillSelection(g, theme, bounds);
        else if (index == _hotIndex || selected)
            g.FillRectangle(theme.HeaderBackground, bounds);
        else
            g.FillRectangle(theme.ControlBackground, bounds);

        g.DrawLine(theme.Border, bounds.X, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);

        // The open pane is marked with an accent bar down its header's leading edge, which is what
        // makes an Outlook stack readable at a glance without colouring the whole row.
        if (selected)
            g.FillRectangle(theme.Accent, new Rectangle(bounds.X, bounds.Y, 3, bounds.Height));

        var textColor = pressed ? theme.SelectionText : theme.ControlText;
        var glyphTop = bounds.Y + ((bounds.Height - _GlyphSize) / 2);
        Glyphs.PaintTriangle(
            g,
            textColor,
            new Rectangle(bounds.X + _GlyphInset, glyphTop, _GlyphSize, _GlyphSize),
            pane.IsExpanded ? GlyphDirection.Down : GlyphDirection.Right);

        var textLeft = bounds.X + _GlyphInset + _GlyphSize + _Gap;
        var paneImage = ImageList.ResolveIndex(this.ImageList, pane.ImageIndex, pane.ImageKey);
        if (this.ImageList is { } images && paneImage >= 0 && paneImage < images.Count && this.Backend is { } backend)
        {
            var size = images.ImageSize;
            var iconTop = bounds.Y + ((bounds.Height - size.Height) / 2);
            g.DrawImage(images.GetImage(paneImage, backend), new Rectangle(textLeft, iconTop, size.Width, size.Height));
            textLeft += size.Width + _Gap;
        }

        var textRect = new Rectangle(textLeft, bounds.Y, Math.Max(0, bounds.Right - _Gap - textLeft), bounds.Height);
        g.DrawText(pane.Text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);

        if (focused && index == _focusedIndex)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3));
    }
}

/// <summary>
/// The ordered set of panes owned by an <see cref="Accordion"/>. Adding a pane parents it into the
/// accordion (realizing it immediately when the control is live); removing it tears its peer tree
/// down and hands the selection to a neighbouring pane.
/// </summary>
public sealed class AccordionPaneCollection : IReadOnlyList<AccordionPane>
{
    private readonly Accordion _owner;
    private readonly List<AccordionPane> _panes = [];

    internal AccordionPaneCollection(Accordion owner) => _owner = owner;

    /// <summary>The number of panes.</summary>
    public int Count => _panes.Count;

    /// <summary>The pane at the given index.</summary>
    public AccordionPane this[int index] => _panes[index];

    /// <summary>The index of the pane, or -1 when it is not part of this accordion.</summary>
    public int IndexOf(AccordionPane pane) => _panes.IndexOf(pane);

    /// <summary>Appends a pane; the first pane added opens by itself.</summary>
    public void Add(AccordionPane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        _panes.Add(pane);
        _owner.OnPaneAdded(pane);
    }

    /// <summary>Registers a pane that arrived through <c>Controls.Add(pane)</c> — it is already a
    /// child control, so only the pane list itself needs the entry.</summary>
    internal void Adopt(AccordionPane pane) => _panes.Add(pane);

    /// <summary>Appends several panes in order.</summary>
    public void AddRange(params AccordionPane[] panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        foreach (var pane in panes)
            this.Add(pane);
    }

    /// <summary>Removes a pane, disposing its peer tree. Returns whether it was present.</summary>
    public bool Remove(AccordionPane pane)
    {
        var index = _panes.IndexOf(pane);
        if (index < 0)
            return false;

        _panes.RemoveAt(index);
        _owner.OnPaneRemoved(pane, index);
        return true;
    }

    /// <summary>Removes every pane.</summary>
    public void Clear()
    {
        while (_panes.Count > 0)
            this.Remove(_panes[^1]);
    }

    /// <inheritdoc/>
    public IEnumerator<AccordionPane> GetEnumerator() => _panes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
