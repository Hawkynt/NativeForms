using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The owner-drawn drop-down engine every menu-shaped surface shares: <see cref="MenuStrip"/>
/// drop-downs, <see cref="ContextMenuStrip"/>, tool-strip drop-down/split buttons and the overflow
/// chevron all open their items through one instance of this class, so a menu looks and behaves
/// identically wherever it pops up. Each cascade level is one <see cref="IPopupPeer"/> painting rows
/// with an icon/check column, mnemonic-underlined text, right-aligned shortcut text, a submenu arrow
/// and separator lines. Submenus cascade as child popups anchored right of their parent item; light
/// dismissal of any level, a committing click or Escape at the root closes the whole cascade.
/// </summary>
/// <remarks>
/// Menus are owner-drawn on every backend for now — a native <c>HMENU</c>/<c>GtkMenuBar</c>/<c>NSMenu</c>
/// mapping is tracked in <c>docs/PRD.md</c> §7.6. Multi-level light dismiss leans on the backend's
/// grab behavior: opening a child level briefly suppresses the parent's dismissal so the grab handoff
/// does not read as a click-outside.
/// </remarks>
internal sealed class MenuDropDown
{
    /// <summary>The width of the leading column carrying the icon or check/radio mark.</summary>
    internal const int IconColumnWidth = 24;

    /// <summary>The width of the trailing column carrying the submenu arrow.</summary>
    internal const int ArrowColumnWidth = 16;

    /// <summary>The pixel height of a separator row.</summary>
    internal const int SeparatorHeight = 5;

    /// <summary>The minimum gap between an item's text and its shortcut text.</summary>
    internal const int ShortcutGap = 16;

    private readonly IPlatformBackend _backend;
    private readonly ITheme _theme;
    private readonly List<Level> _levels = [];
    private bool _suppressDismiss;

    /// <summary>One open cascade level: its popup surface, items and hover state.</summary>
    private sealed class Level
    {
        public required IPopupPeer Popup { get; init; }
        public required IReadOnlyList<ToolStripItem> Items { get; init; }
        public Point Location;
        public Size Size;
        public int HoverIndex = -1;

        /// <summary>Whether this level carries a search field, which only a root level ever does.</summary>
        public bool Searchable;

        /// <summary>What has been typed into that field; empty means every item shows.</summary>
        public string Filter = string.Empty;
    }

    /// <summary>Creates an engine bound to the backend whose popups and text metrics it uses.</summary>
    public MenuDropDown(IPlatformBackend backend, ITheme theme)
    {
        _backend = backend;
        _theme = theme;
    }

    /// <summary>Whether at least one cascade level is open.</summary>
    public bool IsOpen => _levels.Count > 0;

    /// <summary>
    /// The window every level of the cascade belongs to. Set it before <see cref="Open"/>: the engine
    /// outlives any single opening and the owning control may not have been realized when the engine
    /// was built, so the owner is read afresh each time rather than captured in the constructor.
    /// </summary>
    public IWindowPeer? Owner { get; set; }

    /// <summary>Raised once when the cascade fully closes, whatever caused it.</summary>
    public event EventHandler? Closed;

    /// <summary>Opens the root level at a screen position, closing any cascade already open.</summary>
    public void Open(IReadOnlyList<ToolStripItem> items, Point screenLocation)
        => this.Open(items, screenLocation, searchable: false);

    /// <summary>
    /// Opens the root level, optionally with a search field as its first row: typing narrows the rows
    /// below it instead of running the mnemonics (PRD §14).
    /// </summary>
    public void Open(IReadOnlyList<ToolStripItem> items, Point screenLocation, bool searchable)
    {
        this.CloseAll();
        this.OpenLevel(items, screenLocation, searchable);
    }

    /// <summary>Closes every level, deepest first, and raises <see cref="Closed"/> once.</summary>
    public void CloseAll()
    {
        if (_levels.Count == 0)
            return;

        for (var i = _levels.Count - 1; i >= 0; --i)
            this.TearDownLevel(_levels[i]);

        _levels.Clear();
        this.Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The height a level's search field occupies, zero when it has none.</summary>
    private int SearchHeight(Level level) => level.Searchable ? _theme.RowHeight : 0;

    /// <summary>
    /// Whether a row shows: an item hidden by the application never does, and while a filter is
    /// active only the items whose text contains it do. Separators go while filtering — a divider
    /// between groups that are no longer both there separates nothing.
    /// </summary>
    private static bool RowVisible(Level level, ToolStripItem item)
    {
        if (!item.Visible)
            return false;

        if (level.Filter.Length == 0)
            return true;

        return item is not ToolStripSeparator
            && item.DisplayText.Contains(level.Filter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>The size a level needs as it currently stands, filter and search field included.</summary>
    private Size ComputeLevelSize(Level level)
    {
        var size = this.ComputeSize(level.Items, level);
        return new(Math.Max(size.Width, _MinSearchableWidth * (level.Searchable ? 1 : 0)), size.Height);
    }

    /// <summary>Wide enough for a search field to be worth typing into, however short the items are.</summary>
    private const int _MinSearchableWidth = 160;

    /// <summary>
    /// Computes the popup size a set of items needs: an icon column, the widest text, the widest
    /// shortcut (when any item declares one), an arrow column and a 1-pixel border all around.
    /// </summary>
    internal Size ComputeSize(IReadOnlyList<ToolStripItem> items) => this.ComputeSize(items, level: null);

    private Size ComputeSize(IReadOnlyList<ToolStripItem> items, Level? level)
    {
        var font = _theme.DefaultFont;
        var maxText = 0;
        var maxShortcut = 0;
        var height = 2 + (level is null ? 0 : this.SearchHeight(level));
        for (var i = 0; i < items.Count; ++i)
        {
            var item = items[i];
            if (level is null ? !item.Visible : !RowVisible(level, item))
                continue;

            if (item is ToolStripSeparator)
            {
                height += SeparatorHeight;
                continue;
            }

            height += _theme.RowHeight;
            maxText = Math.Max(maxText, _backend.MeasureText(item.DisplayText, font).Width);
            if (item is ToolStripMenuItem { ShortcutText.Length: > 0 } menuItem)
                maxShortcut = Math.Max(maxShortcut, _backend.MeasureText(menuItem.ShortcutText, font).Width);
        }

        var width = 2 + IconColumnWidth + maxText + (maxShortcut > 0 ? ShortcutGap + maxShortcut : 0) + ArrowColumnWidth;
        return new(width, height);
    }

    /// <summary>Routes a key while the cascade is open. Returns whether the key was consumed; an
    /// unconsumed Left/Right lets the owning menu bar move its top-level selection instead.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (_levels.Count == 0)
            return false;

        var level = _levels[^1];
        switch (e.KeyCode)
        {
            case Keys.Down:
                this.MoveHover(level, +1);
                return true;

            case Keys.Up:
                this.MoveHover(level, -1);
                return true;

            case Keys.Enter:
                this.ActivateHover(level);
                return true;

            case Keys.Escape:
                // Escape backs out one step at a time: first whatever was typed, then the level.
                if (level.Searchable && level.Filter.Length > 0)
                {
                    this.SetFilter(level, string.Empty);
                    return true;
                }

                this.CloseDeepest();
                return true;

            case Keys.Back:
                if (!level.Searchable || level.Filter.Length == 0)
                    return false;

                this.SetFilter(level, level.Filter[..^1]);
                return true;

            case Keys.Right:
                if (level.HoverIndex >= 0 && level.Items[level.HoverIndex] is ToolStripDropDownItem { HasDropDownItems: true } parent && parent.Enabled)
                {
                    this.OpenSubmenu(level, level.HoverIndex, parent);
                    return true;
                }

                return false;

            case Keys.Left:
                if (_levels.Count > 1)
                {
                    this.CloseDeepest();
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Activates the first item in the deepest level whose mnemonic matches
    /// <paramref name="c"/>; returns whether one did.</summary>
    public bool HandleKeyPress(char c)
    {
        if (_levels.Count == 0)
            return false;

        var level = _levels[^1];

        // A searchable level spends its keystrokes on the filter. Mnemonics and type-to-filter are the
        // same keys, so one level cannot offer both — and a menu that was opened to be searched has
        // said which it wants.
        if (level.Searchable)
        {
            if (char.IsControl(c))
                return false;

            this.SetFilter(level, level.Filter + c);
            return true;
        }

        var upper = char.ToUpperInvariant(c);
        for (var i = 0; i < level.Items.Count; ++i)
        {
            var item = level.Items[i];
            if (!item.Visible || item.MnemonicIndex < 0 || char.ToUpperInvariant(item.DisplayText[item.MnemonicIndex]) != upper)
                continue;

            level.HoverIndex = i;
            this.ActivateHover(level);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces a level's filter and re-fits its popup to what is left. The surface is resized in
    /// place rather than re-shown, so the light-dismiss grab it holds is never handed round mid-typing.
    /// </summary>
    private void SetFilter(Level level, string filter)
    {
        if (level.Filter == filter)
            return;

        level.Filter = filter;

        // A submenu whose parent row may have just been filtered away has nothing left to hang from.
        this.CloseBelow(level);

        // The highlighted row keeps its highlight only if it survived the narrowing.
        if (level.HoverIndex >= 0 && (level.HoverIndex >= level.Items.Count || !RowVisible(level, level.Items[level.HoverIndex])))
            level.HoverIndex = -1;

        level.Size = this.ComputeLevelSize(level);
        level.Popup.Resize(level.Size);
        level.Popup.InvalidateAll();
    }

    /// <summary>Closes only the deepest level; closing the root closes the cascade.</summary>
    private void CloseDeepest()
    {
        if (_levels.Count == 0)
            return;

        if (_levels.Count == 1)
        {
            this.CloseAll();
            return;
        }

        var level = _levels[^1];
        _levels.RemoveAt(_levels.Count - 1);
        this.TearDownLevel(level);
    }

    /// <summary>Hides and disposes one level's popup without touching the level list.</summary>
    private void TearDownLevel(Level level)
    {
        _suppressDismiss = true;
        try
        {
            level.Popup.Hide();
            level.Popup.Dispose();
        }
        finally
        {
            _suppressDismiss = false;
        }
    }

    /// <summary>Creates, wires and shows one cascade level.</summary>
    private void OpenLevel(IReadOnlyList<ToolStripItem> items, Point screenLocation, bool searchable = false)
    {
        var popup = _backend.CreatePopup(this.Owner);
        var level = new Level { Popup = popup, Items = items, Location = screenLocation, Searchable = searchable };
        level.Size = this.ComputeLevelSize(level);
        popup.Paint += (_, e) => this.PaintLevel(level, e.Graphics);
        popup.MouseMove += (_, e) => this.OnLevelMouseMove(level, e);
        popup.MouseDown += (_, e) => this.OnLevelMouseDown(level, e);
        popup.KeyDown += (_, e) => e.Handled = this.HandleKeyDown(e); // backends with a keyboard grab route keys here
        popup.KeyPress += (_, e) => e.Handled = this.HandleKeyPress(e.KeyChar);
        popup.OutsidePress = this.RouteOutsidePress; // a click on a shallower level is not an outside dismissal
        popup.OutsidePointerMove = this.RouteOutsideMove; // motion the grab redirected here belongs to the level under it
        popup.Dismissed += (_, _) =>
        {
            if (_suppressDismiss)
                return;

            // A level that is no longer the deepest one lost its grab to a child it just opened — the
            // grab handoff, which some backends report asynchronously, after the synchronous
            // _suppressDismiss window has closed. Only the current (deepest) level's dismissal is a real
            // light-dismiss, so a parent's grab-broken never tears the open submenu cascade down.
            if (_levels.Count == 0 || !ReferenceEquals(_levels[^1], level))
                return;

            this.CloseAll();
        };

        // Showing this level takes the light-dismiss grab from the current deepest one, which fires a
        // grab-broken on it asynchronously; tell it that break is an expected handoff so it stays open
        // instead of tearing the cascade down. A nested level also anchors to the one that opened it, so
        // a stacked-popup server maps it as a child of the top-most popup rather than of the root window.
        if (_levels.Count > 0)
        {
            _levels[^1].Popup.ExpectGrabHandoff();
            popup.SetParentPopup(_levels[^1].Popup);
        }

        _levels.Add(level);

        // The grab moving to the new popup must not read as the previous level being dismissed.
        _suppressDismiss = true;
        try
        {
            popup.ShowAt(screenLocation, level.Size);
        }
        finally
        {
            _suppressDismiss = false;
        }
    }

    /// <summary>Opens <paramref name="parent"/>'s children as a child level anchored right of its row.</summary>
    private void OpenSubmenu(Level level, int index, ToolStripDropDownItem parent)
    {
        this.CloseBelow(level);
        var itemTop = this.ItemTop(level, index);
        this.OpenLevel(parent.DropDownItems, new(level.Location.X + level.Size.Width, level.Location.Y + itemTop));
    }

    /// <summary>Closes every level deeper than <paramref name="level"/>.</summary>
    private void CloseBelow(Level level)
    {
        var index = _levels.IndexOf(level);
        var closedAny = false;
        for (var i = _levels.Count - 1; i > index; --i)
        {
            this.TearDownLevel(_levels[i]);
            _levels.RemoveAt(i);
            closedAny = true;
        }

        // A child that held the grab is gone, so the level that is deepest again re-takes it to keep
        // catching outside clicks and Escape.
        if (closedAny && _levels.Count > 0)
            _levels[^1].Popup.Regrab();
    }

    /// <summary>Enter/mnemonic on the hovered row: descend into a submenu or commit the item.</summary>
    private void ActivateHover(Level level)
    {
        var index = level.HoverIndex;
        if (index < 0 || index >= level.Items.Count)
            return;

        var item = level.Items[index];
        if (item is ToolStripSeparator || !RowVisible(level, item))
            return;

        if (item is ToolStripDropDownItem { HasDropDownItems: true } parent)
        {
            if (parent.Enabled)
            {
                this.OpenSubmenu(level, index, parent);
                this.MoveHover(_levels[^1], +1); // land on the first selectable child row
            }

            return;
        }

        if (!item.Enabled)
            return;

        this.CloseAll();
        item.PerformClick();
    }

    /// <summary>Moves the hover row by steps of <paramref name="direction"/>, skipping separators
    /// and invisible items, without wrapping.</summary>
    private void MoveHover(Level level, int direction)
    {
        var index = level.HoverIndex;
        for (var i = index + direction; i >= 0 && i < level.Items.Count; i += direction)
        {
            var item = level.Items[i];
            if (item is ToolStripSeparator || !RowVisible(level, item))
                continue;

            level.HoverIndex = i;
            level.Popup.InvalidateAll();
            return;
        }
    }

    /// <summary>
    /// A pointer motion the deepest level's grab redirected here, in screen coordinates (the backend whose
    /// grab reports out-of-surface motion). Delivers it to the level actually under the pointer via the same
    /// dispatch the canvas path uses, so the parent re-highlights and can cascade a sibling submenu.
    /// </summary>
    private void RouteOutsideMove(Point screen)
    {
        if (_levels.Count == 0)
            return;

        var deepest = _levels[^1];
        this.OnLevelMouseMove(deepest, new MouseEventArgs(MouseButtons.None, screen.X - deepest.Location.X, screen.Y - deepest.Location.Y, 0));
    }

    /// <summary>Hover tracking: highlights the row under the pointer and cascades into submenus.</summary>
    private void OnLevelMouseMove(Level level, MouseEventArgs e)
    {
        // The deepest level holds the grab, so the display server delivers motion that is really over a
        // shallower level to this one instead. Reconstruct the screen point and dispatch to the level the
        // pointer is actually over, so moving back onto the parent updates its highlight and can open a
        // sibling submenu — without this, an open submenu freezes hover tracking on every level above it.
        var screen = new Point(level.Location.X + e.X, level.Location.Y + e.Y);
        for (var i = _levels.Count - 1; i >= 0; --i)
        {
            var over = _levels[i];
            if (!new Rectangle(over.Location, over.Size).Contains(screen))
                continue;

            if (!ReferenceEquals(over, level))
            {
                this.OnLevelMouseMove(over, new MouseEventArgs(e.Button, screen.X - over.Location.X, screen.Y - over.Location.Y, e.Delta));
                return;
            }

            break;
        }

        var index = this.ItemAt(level, e.Y);
        if (index == level.HoverIndex)
            return;

        level.HoverIndex = index;
        level.Popup.InvalidateAll();

        if (index >= 0 && level.Items[index] is ToolStripDropDownItem { HasDropDownItems: true } parent && parent.Enabled)
            this.OpenSubmenu(level, index, parent);
        else
            this.CloseBelow(level);
    }

    /// <summary>A left click on a row: opens its submenu or commits it and closes the cascade.</summary>
    private void OnLevelMouseDown(Level level, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var index = this.ItemAt(level, e.Y);
        if (index < 0)
            return;

        level.HoverIndex = index;
        this.ActivateHover(level);
    }

    /// <summary>
    /// A press outside the deepest (grab-holding) level, in screen coordinates. The grab that catches a
    /// genuine outside click also redirects clicks on shallower levels of this same cascade — so a click on
    /// an open parent menu would otherwise read as an outside dismissal. If the point lies on a shallower
    /// level, close the levels below it and deliver the press there (switching submenu or committing the
    /// row); only a point on no level at all is a real dismissal, left for the popup to perform.
    /// </summary>
    private bool RouteOutsidePress(Point screen)
    {
        for (var i = _levels.Count - 1; i >= 0; --i)
        {
            var level = _levels[i];
            if (!new Rectangle(level.Location, level.Size).Contains(screen))
                continue;

            if (ReferenceEquals(level, _levels[^1]))
                return false; // the deepest level itself — inside it, so not our concern

            this.CloseBelow(level);
            this.OnLevelMouseDown(level, new MouseEventArgs(MouseButtons.Left, screen.X - level.Location.X, screen.Y - level.Location.Y, 0));
            return true;
        }

        return false;
    }

    /// <summary>The index of the visible row at client-space <paramref name="y"/>, or -1.</summary>
    private int ItemAt(Level level, int y)
    {
        var top = 1 + this.SearchHeight(level);
        for (var i = 0; i < level.Items.Count; ++i)
        {
            var item = level.Items[i];
            if (!RowVisible(level, item))
                continue;

            var height = item is ToolStripSeparator ? SeparatorHeight : _theme.RowHeight;
            if (y >= top && y < top + height)
                return item is ToolStripSeparator ? -1 : i;

            top += height;
        }

        return -1;
    }

    /// <summary>The y-offset of the row at <paramref name="index"/> within its popup.</summary>
    private int ItemTop(Level level, int index)
    {
        var top = 1 + this.SearchHeight(level);
        for (var i = 0; i < index; ++i)
        {
            var item = level.Items[i];
            if (RowVisible(level, item))
                top += item is ToolStripSeparator ? SeparatorHeight : _theme.RowHeight;
        }

        return top;
    }

    /// <summary>Paints one level: background, rows (mark/icon, text, shortcut, arrow), border.</summary>
    private void PaintLevel(Level level, IGraphics g)
    {
        var theme = _theme;
        var size = level.Size;
        g.FillRectangle(theme.ControlBackground, new(0, 0, size.Width, size.Height));

        var top = 1;
        if (level.Searchable)
        {
            var field = new Rectangle(1, top, size.Width - 2, _theme.RowHeight);
            GlyphRenderer.DrawSearchField(g, theme, field, enabled: true, showClear: level.Filter.Length > 0);

            var textRect = new Rectangle(
                field.X + GlyphRenderer.SearchGlyphZoneWidth,
                field.Y,
                Math.Max(0, field.Width - GlyphRenderer.SearchGlyphZoneWidth - GlyphRenderer.SearchClearZoneWidth),
                field.Height);

            var typed = level.Filter.Length > 0;
            g.DrawText(
                typed ? level.Filter : Strings.SearchPlaceholder,
                theme.DefaultFont,
                typed ? theme.ControlText : theme.DisabledText,
                textRect,
                ContentAlignment.MiddleLeft);

            top += _theme.RowHeight;
        }

        for (var i = 0; i < level.Items.Count; ++i)
        {
            var item = level.Items[i];
            if (!RowVisible(level, item))
                continue;

            if (item is ToolStripSeparator)
            {
                var mid = top + (SeparatorHeight / 2);
                g.DrawLine(theme.Border, 1 + IconColumnWidth, mid, size.Width - 2, mid);
                top += SeparatorHeight;
                continue;
            }

            var rowHeight = theme.RowHeight;
            var row = new Rectangle(1, top, size.Width - 2, rowHeight);
            var hovered = i == level.HoverIndex && item.Enabled;
            if (hovered)
                GlyphRenderer.FillSelection(g, theme, row);

            var textColor = !item.Enabled ? theme.DisabledText : hovered ? theme.SelectionText : theme.ControlText;

            // The leading column: an icon when the item has one, else its check/radio mark.
            var icon = item.ResolveImage(_backend);
            if (icon is not null)
            {
                var edge = rowHeight - 6;
                g.DrawImage(icon, new(row.X + ((IconColumnWidth - edge) / 2), row.Y + 3, edge, edge));
            }
            else if (item is ToolStripMenuItem { Checked: true } checkedItem)
                if (checkedItem.CheckedGroup is not null)
                    ToolStripRenderer.PaintRadioMark(g, textColor, row.X, row.Y, IconColumnWidth, rowHeight);
                else
                    ToolStripRenderer.PaintCheckMark(g, textColor, row.X, row.Y, IconColumnWidth, rowHeight);

            var textRect = new Rectangle(row.X + IconColumnWidth, row.Y, row.Width - IconColumnWidth - ArrowColumnWidth, rowHeight);
            ToolStripRenderer.PaintMnemonicText(g, theme.DefaultFont, textColor, item, textRect);

            if (item is ToolStripMenuItem { ShortcutText.Length: > 0 } withShortcut)
                g.DrawText(withShortcut.ShortcutText, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleRight);

            if (item is ToolStripDropDownItem { HasDropDownItems: true })
                Glyphs.PaintTriangle(g, textColor, new(row.Right - ArrowColumnWidth + 5, row.Y + ((rowHeight - 7) / 2), 4, 7), GlyphDirection.Right);

            top += rowHeight;
        }

        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
    }
}
