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
        if (e.Button == MouseButtons.Left && new Rectangle(0, 0, this.Width, this.Height).Contains(e.Location))
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

        var boxTop = Math.Max(0, (this.Height - CheckGlyph.BoxSize) / 2);
        CheckGlyph.Draw(g, theme, 0, boxTop, this.Checked);

        var textRect = new Rectangle(CheckGlyph.BoxSize + _TextGap, 0, this.Width - CheckGlyph.BoxSize - _TextGap, this.Height);
        g.DrawText(this.Text, theme.DefaultFont, this.Enabled ? theme.ControlText : theme.DisabledText, textRect, ContentAlignment.MiddleLeft);
    }
}
