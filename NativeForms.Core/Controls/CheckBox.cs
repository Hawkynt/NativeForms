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
    protected void Toggle()
    {
        this.Checked = !this.Checked;
        this.OnClick(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest.ClientContains(this, e.Location))
            this.Toggle();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.Toggle();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        var boxTop = Math.Max(0, (this.Height - GlyphRenderer.CheckBoxSize) / 2);
        var box = new Rectangle(0, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
        GlyphRenderer.DrawCheckBox(g, theme, box, this.Checked);

        var content = new Rectangle(GlyphRenderer.CheckBoxSize + _TextGap, 0, this.Width - GlyphRenderer.CheckBoxSize - _TextGap, this.Height);
        var textColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        if (this.Image is { } image)
        {
            ContentLayout.Arrange(
                content,
                new Size(image.Width, image.Height),
                g.MeasureText(this.Text, theme.DefaultFont),
                TextImageRelation.ImageBeforeText,
                ContentAlignment.MiddleLeft,
                out var imageRect,
                out var textRect);
            g.DrawImage(image, imageRect);
            g.DrawText(this.Text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleLeft);
        }
        else
            g.DrawText(this.Text, theme.DefaultFont, textColor, content, ContentAlignment.MiddleLeft);
    }
}
