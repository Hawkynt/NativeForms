using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn radio button painted in the native theme (themed ring, accent dot, themed text).
/// Selecting it — by click or Space — checks it, unchecks its sibling radio buttons in the same parent
/// and raises <see cref="Control.Click"/>. Clearing <see cref="Checked"/> directly is permitted.
/// </summary>
public class RadioButton : OwnerDrawnControl
{
    private const int _CircleSize = 14;
    private const int _TextGap = 6;

    /// <summary>Whether this button is the selected one in its group.</summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (value)
                this.UncheckSiblings();

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

    /// <summary>Selects this button (checking it) and raises <see cref="Control.Click"/>.</summary>
    protected void Select()
    {
        this.Checked = true;
        this.OnClick(EventArgs.Empty);
    }

    /// <summary>Clears the checked state of the sibling radio buttons in the same parent.</summary>
    private void UncheckSiblings()
    {
        var parent = this.Parent;
        if (parent is null)
            return;

        foreach (var sibling in parent.Controls.OfType<RadioButton>())
            if (!ReferenceEquals(sibling, this))
                sibling.Checked = false;
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && new Rectangle(0, 0, this.Width, this.Height).Contains(e.Location))
            this.Select();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.Select();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        var circleTop = Math.Max(0, (this.Height - _CircleSize) / 2);
        var circle = new Rectangle(0, circleTop, _CircleSize, _CircleSize);
        g.FillEllipse(theme.FieldBackground, circle);
        g.DrawEllipse(this.Checked ? theme.Accent : theme.Border, circle);

        if (this.Checked)
        {
            var inset = _CircleSize / 4;
            var dot = new Rectangle(circle.X + inset, circle.Y + inset, _CircleSize - 2 * inset, _CircleSize - 2 * inset);
            g.FillEllipse(theme.Accent, dot);
        }

        var textRect = new Rectangle(_CircleSize + _TextGap, 0, this.Width - _CircleSize - _TextGap, this.Height);
        g.DrawText(this.Text, theme.DefaultFont, this.Enabled ? theme.ControlText : theme.DisabledText, textRect, ContentAlignment.MiddleLeft);
    }
}
