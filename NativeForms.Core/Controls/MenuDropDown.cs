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
    }

    /// <summary>Creates an engine bound to the backend whose popups and text metrics it uses.</summary>
    public MenuDropDown(IPlatformBackend backend, ITheme theme)
    {
        _backend = backend;
        _theme = theme;
    }

    /// <summary>Whether at least one cascade level is open.</summary>
    public bool IsOpen => _levels.Count > 0;

    /// <summary>Raised once when the cascade fully closes, whatever caused it.</summary>
    public event EventHandler? Closed;

    /// <summary>Opens the root level at a screen position, closing any cascade already open.</summary>
    public void Open(IReadOnlyList<ToolStripItem> items, Point screenLocation)
    {
        this.CloseAll();
        this.OpenLevel(items, screenLocation);
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

    /// <summary>
    /// Computes the popup size a set of items needs: an icon column, the widest text, the widest
    /// shortcut (when any item declares one), an arrow column and a 1-pixel border all around.
    /// </summary>
    internal Size ComputeSize(IReadOnlyList<ToolStripItem> items)
    {
        var font = _theme.DefaultFont;
        var maxText = 0;
        var maxShortcut = 0;
        var height = 2;
        for (var i = 0; i < items.Count; ++i)
        {
            var item = items[i];
            if (!item.Visible)
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
                this.CloseDeepest();
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
    private void OpenLevel(IReadOnlyList<ToolStripItem> items, Point screenLocation)
    {
        var popup = _backend.CreatePopup();
        var level = new Level { Popup = popup, Items = items, Location = screenLocation, Size = this.ComputeSize(items) };
        popup.Paint += (_, e) => this.PaintLevel(level, e.Graphics);
        popup.MouseMove += (_, e) => this.OnLevelMouseMove(level, e);
        popup.MouseDown += (_, e) => this.OnLevelMouseDown(level, e);
        popup.KeyDown += (_, e) => e.Handled = this.HandleKeyDown(e); // backends with a keyboard grab route keys here
        popup.KeyPress += (_, e) => e.Handled = this.HandleKeyPress(e.KeyChar);
        popup.Dismissed += (_, _) =>
        {
            if (!_suppressDismiss)
                this.CloseAll();
        };

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
        for (var i = _levels.Count - 1; i > index; --i)
        {
            this.TearDownLevel(_levels[i]);
            _levels.RemoveAt(i);
        }
    }

    /// <summary>Enter/mnemonic on the hovered row: descend into a submenu or commit the item.</summary>
    private void ActivateHover(Level level)
    {
        var index = level.HoverIndex;
        if (index < 0 || index >= level.Items.Count)
            return;

        var item = level.Items[index];
        if (item is ToolStripSeparator || !item.Visible)
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
            if (item is ToolStripSeparator || !item.Visible)
                continue;

            level.HoverIndex = i;
            level.Popup.InvalidateAll();
            return;
        }
    }

    /// <summary>Hover tracking: highlights the row under the pointer and cascades into submenus.</summary>
    private void OnLevelMouseMove(Level level, MouseEventArgs e)
    {
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

    /// <summary>The index of the visible row at client-space <paramref name="y"/>, or -1.</summary>
    private int ItemAt(Level level, int y)
    {
        var top = 1;
        for (var i = 0; i < level.Items.Count; ++i)
        {
            var item = level.Items[i];
            if (!item.Visible)
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
        var top = 1;
        for (var i = 0; i < index; ++i)
        {
            var item = level.Items[i];
            if (item.Visible)
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
        for (var i = 0; i < level.Items.Count; ++i)
        {
            var item = level.Items[i];
            if (!item.Visible)
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
