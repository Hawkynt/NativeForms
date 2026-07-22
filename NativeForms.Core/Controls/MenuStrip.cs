using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The application menu bar: a horizontal, owner-drawn row of top-level items whose drop-downs open
/// through the shared <see cref="MenuDropDown"/> engine — icons, check marks, shortcut text, cascading
/// submenus and light dismiss included. Clicking or Enter opens a drop-down, Left/Right walk the
/// top-level items (switching an open menu live), Up/Down move inside the drop-down and Escape walks
/// the cascade closed. Registered shortcut chords (<see cref="ToolStripMenuItem.ShortcutKeys"/>) are
/// dispatched through <see cref="ProcessShortcut"/>.
/// </summary>
/// <remarks>
/// The bar is owner-drawn on every backend for now; the native menu bar mapping (Win32 <c>HMENU</c>,
/// <c>GtkMenuBar</c>, <c>NSMenu</c>) is tracked in <c>docs/PRD.md</c> §7.6. Shortcuts and
/// Alt+mnemonics are dispatched form-wide through the owning form's dialog-key chain, which every
/// focused owner-drawn surface feeds — the form finds its menu strips and routes chords into
/// <see cref="ProcessShortcut"/> and Alt+letter into <see cref="OpenMnemonic"/>. Keys held inside
/// native widgets (text boxes) cannot be previewed yet, so shortcuts don't fire from there.
/// </remarks>
public class MenuStrip : OwnerDrawnControl
{
    /// <summary>The horizontal padding on each side of a top-level item's caption.</summary>
    internal const int ItemPadding = 8;

    private MenuDropDown? _dropDown;
    private int _hoverIndex = -1;
    private int _openIndex = -1;

    /// <summary>Cached per-item pixel widths, index-aligned with <see cref="Items"/>; 0 marks an
    /// unmeasured slot. Invalidated by the Items.Changed hook, by (un)realization and by a theme
    /// font swap, so per-event hit-testing stops re-measuring text natively on every mouse move.</summary>
    private int[]? _itemWidths;

    /// <summary>The theme font the cache was measured with; a different snapshot voids it.</summary>
    private Font _measuredFont;

    /// <summary>Creates an empty menu bar.</summary>
    public MenuStrip()
    {
        this.Items = new();
        this.Items.Changed += (_, _) =>
        {
            _itemWidths = null;
            this.Invalidate();
        };
    }

    /// <summary>The top-level items. Mutating the collection (or any item in it) repaints the bar.</summary>
    public ToolStripItemCollection Items { get; }

    /// <summary>The index of the top-level item whose drop-down is open, or -1.</summary>
    public int OpenIndex => _openIndex;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The bar is reached with Alt, not Tab — matching Windows Forms.</summary>
    private protected override bool DefaultTabStop => false;

    /// <summary>
    /// Claims the keys an open menu (or a keyboard-hovered bar) consumes, keeping the form's
    /// dialog-key handling — Enter → AcceptButton, Escape → CancelButton, Tab navigation — out of a
    /// running menu interaction.
    /// </summary>
    protected override bool IsInputKey(Keys keyData)
        => _dropDown is { IsOpen: true }
            ? keyData is Keys.Enter or Keys.Escape or Keys.Tab
            : keyData == Keys.Enter && _hoverIndex >= 0;

    /// <summary>
    /// Dispatches a shortcut chord (for example <c>Keys.Control | Keys.S</c>) to the first enabled,
    /// visible menu item registered for it, searching the whole item tree. Returns whether one fired.
    /// </summary>
    internal bool ProcessShortcut(Keys keyData)
        => keyData != Keys.None && (keyData & Keys.KeyCode) != Keys.None && DispatchShortcut(this.Items, keyData);

    /// <summary>
    /// Opens the top-level menu whose mnemonic matches <paramref name="mnemonic"/> (uppercased) —
    /// the form-wide Alt+letter activation. Focuses the bar first so the open menu keeps receiving
    /// keys. Returns whether a menu matched.
    /// </summary>
    internal bool OpenMnemonic(char mnemonic)
    {
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible || item.MnemonicIndex < 0 || char.ToUpperInvariant(item.DisplayText[item.MnemonicIndex]) != mnemonic)
                continue;

            this.Focus();
            this.OpenDropDown(i);
            return true;
        }

        return false;
    }

    /// <summary>Opens the drop-down of the item at <paramref name="index"/> below the bar.</summary>
    public void OpenDropDown(int index)
    {
        if (index < 0 || index >= this.Items.Count || this.Backend is null)
            return;

        if (this.Items[index] is not ToolStripDropDownItem { HasDropDownItems: true } item || !item.Enabled)
            return;

        // Open first: switching menus re-enters Closed (which resets the open index) via CloseAll.
        this.Engine.Open(item.DropDownItems, this.PointToScreen(new(this.ItemLeft(index), this.Height)));
        _openIndex = index;
        this.Invalidate();
    }

    /// <summary>Closes the open drop-down cascade, if any.</summary>
    public void CloseDropDown() => _dropDown?.CloseAll();

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
        _openIndex = -1;
        _itemWidths = null;
    }

    /// <summary>The lazily created drop-down engine, wired to reset the bar state on close.</summary>
    private MenuDropDown Engine
    {
        get
        {
            var engine = _dropDown;
            if (engine is null)
            {
                _dropDown = engine = new(this.Backend!, this.Theme);
                engine.Closed += (_, _) =>
                {
                    _openIndex = -1;
                    this.Invalidate();
                };
            }

            // Refreshed on every access rather than captured once: the bar may have been realized
            // — or reparented onto another form — after the engine was built.
            engine.Owner = this.OwnerWindowPeer;
            return engine;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new(0, 0, this.Width, this.Height));

        var x = 0;
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            var width = this.ItemWidth(i, item);
            var rect = new Rectangle(x, 0, width, this.Height);
            var active = i == _openIndex || (i == _hoverIndex && item.Enabled);
            if (active)
                GlyphRenderer.FillSelection(g, theme, rect);

            var textColor = !item.Enabled ? theme.DisabledText : active ? theme.SelectionText : theme.ControlText;
            var textRect = new Rectangle(x + ItemPadding, 0, width - (2 * ItemPadding), this.Height);
            ToolStripRenderer.PaintMnemonicText(g, theme.DefaultFont, textColor, item, textRect);
            x += width;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var index = this.ItemAt(e.X);
        if (index < 0)
            return;

        if (index == _openIndex)
            this.CloseDropDown();
        else
            this.OpenDropDown(index);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = this.ItemAt(e.X);
        if (index == _hoverIndex)
            return;

        _hoverIndex = index;
        this.Invalidate();

        // The classic menu gesture: while a menu is open, sliding along the bar switches it live.
        if (_openIndex >= 0 && index >= 0 && index != _openIndex)
            this.OpenDropDown(index);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex < 0)
            return;

        _hoverIndex = -1;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var engine = _dropDown;
        if (engine is { IsOpen: true })
        {
            if (engine.HandleKeyDown(e))
            {
                e.Handled = true;
                return;
            }

            // Left/Right fell through the cascade: walk the top-level items with the menu open.
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                this.OpenDropDown(this.NextItem(_openIndex, e.KeyCode == Keys.Right ? +1 : -1));
                e.Handled = true;
            }

            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Right:
                this.MoveHover(+1);
                e.Handled = true;
                return;

            case Keys.Left:
                this.MoveHover(-1);
                e.Handled = true;
                return;

            case Keys.Enter:
            case Keys.Down:
                if (_hoverIndex >= 0)
                {
                    this.OpenDropDown(_hoverIndex);
                    e.Handled = true;
                }

                return;

            default:
                if (this.ProcessShortcut(e.KeyData))
                    e.Handled = true;

                return;
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (char.IsControl(e.KeyChar))
            return;

        var engine = _dropDown;
        if (engine is { IsOpen: true })
        {
            e.Handled = engine.HandleKeyPress(e.KeyChar);
            return;
        }

        // A bare mnemonic letter opens the matching top-level menu while the bar has focus.
        var upper = char.ToUpperInvariant(e.KeyChar);
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible || item.MnemonicIndex < 0 || char.ToUpperInvariant(item.DisplayText[item.MnemonicIndex]) != upper)
                continue;

            this.OpenDropDown(i);
            e.Handled = true;
            return;
        }
    }

    /// <summary>Moves the keyboard hover to the next visible item in <paramref name="direction"/>.</summary>
    private void MoveHover(int direction)
    {
        var next = this.NextItem(_hoverIndex, direction);
        if (next == _hoverIndex)
            return;

        _hoverIndex = next;
        this.Invalidate();
    }

    /// <summary>The index of the next visible item after <paramref name="start"/>, wrapping around.</summary>
    private int NextItem(int start, int direction)
    {
        var count = this.Items.Count;
        if (count == 0)
            return -1;

        var index = start;
        for (var step = 0; step < count; ++step)
        {
            index = ((index + direction) % count + count) % count;
            if (this.Items[index].Visible)
                return index;
        }

        return start;
    }

    /// <summary>The pixel width of the top-level item at <paramref name="index"/>, from the cache
    /// when it is warm, measured (and cached) otherwise.</summary>
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

    /// <summary>Measures one top-level item: padded caption plus an optional icon.</summary>
    private int MeasureItemWidth(ToolStripItem item)
    {
        var width = (2 * ItemPadding) + this.MeasureItemText(item);
        if (item.HasIcon)
            width += 20;

        return width;
    }

    /// <summary>The x-offset of the visible item at <paramref name="index"/>.</summary>
    private int ItemLeft(int index)
    {
        var x = 0;
        for (var i = 0; i < index; ++i)
        {
            var item = this.Items[i];
            if (item.Visible)
                x += this.ItemWidth(i, item);
        }

        return x;
    }

    /// <summary>The index of the visible item under x-coordinate <paramref name="x"/>, or -1.</summary>
    private int ItemAt(int x)
    {
        var left = 0;
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            var width = this.ItemWidth(i, item);
            if (x >= left && x < left + width)
                return i;

            left += width;
        }

        return -1;
    }

    /// <summary>Measures an item's caption with the backend's text engine (falls back to zero before realization).</summary>
    private int MeasureItemText(ToolStripItem item)
        => this.Backend?.MeasureText(item.DisplayText, this.Theme.DefaultFont).Width ?? 0;

    /// <summary>Depth-first shortcut dispatch over an item tree.</summary>
    private static bool DispatchShortcut(ToolStripItemCollection items, Keys keyData)
    {
        for (var i = 0; i < items.Count; ++i)
        {
            if (items[i] is not ToolStripMenuItem item || !item.Visible)
                continue;

            if (item.ShortcutKeys == keyData && item.Enabled)
            {
                item.PerformClick();
                return true;
            }

            if (item.HasDropDownItems && DispatchShortcut(item.DropDownItems, keyData))
                return true;
        }

        return false;
    }
}
