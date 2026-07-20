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
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        if (this.Text.Length == 0)
            return;

        // Right-to-left anchors the text (and its underline) at the right edge instead.
        var rtl = this.IsRightToLeft;
        var client = this.DisplayRectangle;
        var font = this.Font;
        var color = this.LinkColor(theme);
        g.DrawText(this.Text, font, color, client, rtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);

        var extent = g.MeasureText(this.Text, font);
        var underlineX = rtl ? client.Right - extent.Width : client.X;
        var underlineY = client.Y + (client.Height - extent.Height) / 2 + extent.Height - 1;
        g.DrawLine(color, underlineX, underlineY, underlineX + extent.Width, underlineY);

        if (_focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(underlineX, client.Y + (client.Height - extent.Height) / 2, extent.Width, extent.Height));
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

    /// <summary>The client-space rectangle the text occupies (anchored middle-left, or middle-right
    /// under right-to-left), for hit testing.</summary>
    private Rectangle TextExtentRectangle()
    {
        var backend = this.Backend;
        if (backend is null || this.Text.Length == 0)
            return Rectangle.Empty;

        var client = this.DisplayRectangle;
        var extent = backend.MeasureText(this.Text, this.Font);
        var x = this.IsRightToLeft ? client.Right - extent.Width : client.X;
        return new(new Point(x, client.Y + (client.Height - extent.Height) / 2), extent);
    }

    /// <summary>
    /// Updates the hover state, repainting only on an actual change and switching the pointer to
    /// the hand cursor while it rests on the link text (back to the ambient cursor off it).
    /// </summary>
    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
            return;

        _hovered = hovered;
        if (hovered)
            this.Cursor = Cursors.Hand;
        else
            this.ResetCursor();

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
