using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn check box painted in the native theme (themed box, accent checkmark, themed text).
/// Toggles on click or Space and raises <see cref="CheckedChanged"/>.
/// </summary>
public class CheckBox : OwnerDrawnControl
{
    private const int _TextGap = 6;

    /// <summary>Whether the box is checked.</summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
            this.OnCheckedChanged(EventArgs.Empty);
        }
    }

    /// <summary>
    /// An optional icon rendered between the check square and the caption through the shared content
    /// layout; the text shifts right to make room.
    /// </summary>
    public IImage? Image
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

    /// <summary>Raised when <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="CheckedChanged"/>.</summary>
    protected virtual void OnCheckedChanged(EventArgs e) => this.CheckedChanged?.Invoke(this, e);

    /// <summary>Toggles the checked state and raises <see cref="Control.Click"/>.</summary>
    protected void Toggle() => this.OnClick(EventArgs.Empty);

    /// <summary>Toggles <see cref="Checked"/>, then raises <see cref="Control.Click"/> — the
    /// Windows Forms order (<see cref="CheckedChanged"/> first), shared by mouse, Space and
    /// <see cref="Control.PerformClick"/>.</summary>
    protected override void OnClick(EventArgs e)
    {
        this.Checked = !this.Checked;
        base.OnClick(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest.ClientContains(this, e.Location))
            this.OnClick(EventArgs.Empty);
    }

    /// <summary>Space toggles on the key <em>release</em>, like the Windows Forms button base — a
    /// held key must not auto-repeat the toggle.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.OnClick(EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var font = this.Font;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        // Right-to-left mirrors the whole face: check square at the right edge, content anchored
        // toward it, image on the text's trailing (right) side.
        var rtl = this.IsRightToLeft;
        var client = this.DisplayRectangle;
        var boxTop = client.Y + Math.Max(0, (client.Height - GlyphRenderer.CheckBoxSize) / 2);
        var box = new Rectangle(client.X, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
        var content = new Rectangle(box.Right + _TextGap, client.Y, client.Right - box.Right - _TextGap, client.Height);
        var alignment = ContentAlignment.MiddleLeft;
        if (rtl)
        {
            box = RtlLayout.Mirror(box, this.Width);
            content = RtlLayout.Mirror(content, this.Width);
            alignment = RtlLayout.Mirror(alignment);
        }

        GlyphRenderer.DrawCheckBox(g, theme, box, this.Checked);

        var textColor = this.Enabled ? this.ForeColor : theme.DisabledText;
        if (this.Image is { } image)
        {
            ContentLayout.Arrange(
                content,
                new Size(image.Width, image.Height),
                g.MeasureText(this.Text, font),
                rtl ? TextImageRelation.TextBeforeImage : TextImageRelation.ImageBeforeText,
                alignment,
                out var imageRect,
                out var textRect);
            g.DrawImage(image, imageRect);
            g.DrawText(this.Text, font, textColor, textRect, alignment);
        }
        else
            g.DrawText(this.Text, font, textColor, content, alignment);
    }
}
