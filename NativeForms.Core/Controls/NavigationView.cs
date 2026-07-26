using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A left navigation rail — the modern app-shell side bar: a vertical list of icon + caption items with a
/// hamburger button that collapses the rail to icons-only, an accent stripe on the selected item, and
/// <see cref="SelectedIndexChanged"/> so the host swaps the content region beside it. Owner-drawn and
/// native-themed; icons come from an <see cref="ImageList"/>.
/// </summary>
public class NavigationView : OwnerDrawnControl
{
    private const int _RowHeight = 34;
    private const int _IconZone = 40;
    private const int _CollapsedWidth = 44;
    private const int _Stripe = 3;

    private readonly List<(string Text, int ImageIndex)> _items = [];

    /// <summary>The icon source for the item images.</summary>
    public ImageList? ImageList
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    }

    /// <summary>Adds a navigation item; returns its index.</summary>
    public int AddItem(string text, int imageIndex = -1)
    {
        _items.Add((text ?? string.Empty, imageIndex));
        if (_selectedIndex < 0)
            _selectedIndex = 0;

        this.Invalidate();
        return _items.Count - 1;
    }

    /// <summary>The item captions, top to bottom.</summary>
    public IReadOnlyList<string> Items => _items.ConvertAll(i => i.Text);

    /// <summary>The selected item, or <c>-1</c> when empty.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    private int _selectedIndex = -1;

    private int _expandedWidth;

    /// <summary>Whether the rail is collapsed to an icons-only strip. Toggled by the hamburger button.
    /// Collapsing narrows the rail to <see cref="_CollapsedWidth"/> and expanding restores the width it
    /// had before, so the content region beside it reflows automatically.</summary>
    public bool Collapsed
    {
        get => field;
        set
        {
            if (field == value)
                return;

            if (value)
                _expandedWidth = this.Width;

            field = value;
            this.Width = value ? _CollapsedWidth : Math.Max(_CollapsedWidth, _expandedWidth);
            this.Invalidate();
            this.OnCollapsedChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when <see cref="Collapsed"/> changes — the host resizes the rail in response.</summary>
    public event EventHandler? CollapsedChanged;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="CollapsedChanged"/>.</summary>
    protected virtual void OnCollapsedChanged(EventArgs e) => this.CollapsedChanged?.Invoke(this, e);

    /// <summary>The width the rail wants: a fixed strip while collapsed, otherwise its current width.</summary>
    public int PreferredWidth => this.Collapsed ? _CollapsedWidth : Math.Max(_CollapsedWidth, this.Width);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    private Rectangle HamburgerRect => new(0, 0, this.Width, _RowHeight);
    private int ItemTop(int i) => _RowHeight + (i * _RowHeight);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var backend = this.Backend;
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, this.Width, this.Height));

        // The hamburger toggle at the top.
        var h = this.HamburgerRect;
        for (var i = 0; i < 3; ++i)
            g.DrawLine(theme.ControlText, 14, h.Y + 12 + (i * 5), 26, h.Y + 12 + (i * 5), 2);

        for (var i = 0; i < _items.Count; ++i)
        {
            var row = new Rectangle(0, this.ItemTop(i), this.Width, _RowHeight);
            var selected = i == this.SelectedIndex;
            if (selected)
            {
                g.FillRectangle(Blend(theme.Accent, theme.HeaderBackground, 0.18), row);
                g.FillRectangle(theme.Accent, new Rectangle(0, row.Y, _Stripe, row.Height));
            }

            var item = _items[i];
            if (this.ImageList is { } images && item.ImageIndex >= 0 && item.ImageIndex < images.Count && backend is { })
            {
                var size = images.ImageSize;
                g.DrawImage(images.GetImage(item.ImageIndex, backend), new Rectangle(((_IconZone - size.Width) / 2) + _Stripe, row.Y + ((_RowHeight - size.Height) / 2), size.Width, size.Height));
            }

            if (!this.Collapsed)
                g.DrawText(item.Text, this.Font, selected ? theme.ControlText : theme.HeaderText,
                    new Rectangle(_IconZone, row.Y, this.Width - _IconZone - 6, _RowHeight), ContentAlignment.MiddleLeft);
        }

        g.DrawLine(theme.Border, this.Width - 1, 0, this.Width - 1, this.Height); // divider against the content
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.HamburgerRect.Contains(e.Location))
        {
            this.Collapsed = !this.Collapsed;
            return;
        }

        var index = (e.Y - _RowHeight) / _RowHeight;
        if (index >= 0 && index < _items.Count)
            this.SelectedIndex = index;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up when this.SelectedIndex > 0:
                this.SelectedIndex--;
                e.Handled = true;
                break;
            case Keys.Down when this.SelectedIndex < _items.Count - 1:
                this.SelectedIndex++;
                e.Handled = true;
                break;
        }
    }

    private static Color Blend(Color a, Color b, double t)
        => Color.FromArgb(255,
            (int)Math.Round((a.R * t) + (b.R * (1 - t))),
            (int)Math.Round((a.G * t) + (b.G * (1 - t))),
            (int)Math.Round((a.B * t) + (b.B * (1 - t))));
}
