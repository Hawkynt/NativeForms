using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn hyperlink label: its whole text paints in the theme's accent color with an
/// underline, shifts subtly while hovered, and raises <see cref="LinkClicked"/> on a click inside the
/// text or on Space when focused. <see cref="Visited"/> blends the color toward the theme's grey.
/// Per-character link ranges (WinForms <c>LinkArea</c>) are not modeled yet — the entire text is the
/// link.
/// </summary>
public class LinkLabel : OwnerDrawnControl
{
    private bool _hovered;
    private bool _focused;

    /// <summary>Whether the link has been followed; shifts the paint color toward the theme's grey.</summary>
    public bool Visited
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>Raised when the link is activated (click inside the text, or Space while focused).</summary>
    public event EventHandler? LinkClicked;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="LinkClicked"/>.</summary>
    protected virtual void OnLinkClicked(EventArgs e) => this.LinkClicked?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && this.TextExtentRectangle().Contains(e.Location))
            this.OnLinkClicked(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
        => this.SetHovered(this.TextExtentRectangle().Contains(e.Location));

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e) => this.SetHovered(false);

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.OnLinkClicked(EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var full = new Rectangle(0, 0, this.Width, this.Height);
        g.FillRectangle(theme.ControlBackground, full);

        if (this.Text.Length == 0)
            return;

        var font = theme.DefaultFont;
        var color = this.LinkColor(theme);
        g.DrawText(this.Text, font, color, full, ContentAlignment.MiddleLeft);

        var extent = g.MeasureText(this.Text, font);
        var underlineY = (this.Height - extent.Height) / 2 + extent.Height - 1;
        g.DrawLine(color, 0, underlineY, extent.Width, underlineY);

        if (_focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(0, (this.Height - extent.Height) / 2, extent.Width, extent.Height));
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(EventArgs e) => this.SetFocused(true);

    /// <inheritdoc/>
    protected override void OnLostFocus(EventArgs e) => this.SetFocused(false);

    /// <summary>Updates the focus state, repainting only on an actual change.</summary>
    private void SetFocused(bool focused)
    {
        if (_focused == focused)
            return;

        _focused = focused;
        this.Invalidate();
    }

    /// <summary>The client-space rectangle the text occupies (middle-left aligned), for hit testing.</summary>
    private Rectangle TextExtentRectangle()
    {
        var backend = this.Backend;
        if (backend is null || this.Text.Length == 0)
            return Rectangle.Empty;

        var extent = backend.MeasureText(this.Text, this.Theme.DefaultFont);
        return new(new Point(0, (this.Height - extent.Height) / 2), extent);
    }

    /// <summary>Updates the hover state, repainting only on an actual change.</summary>
    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
            return;

        _hovered = hovered;
        this.Invalidate();
    }

    /// <summary>The current link color: theme accent, greyed when visited, shifted while hovered.</summary>
    private Color LinkColor(ITheme theme)
    {
        var color = this.Visited ? Blend(theme.Accent, theme.DisabledText, 50) : theme.Accent;
        return _hovered ? Blend(color, theme.ControlText, 30) : color;
    }

    /// <summary>Linearly blends <paramref name="from"/> toward <paramref name="to"/> by <paramref name="percent"/>.</summary>
    private static Color Blend(Color from, Color to, int percent)
        => Color.FromArgb(
            0xFF,
            from.R + (to.R - from.R) * percent / 100,
            from.G + (to.G - from.G) * percent / 100,
            from.B + (to.B - from.B) * percent / 100);
}
