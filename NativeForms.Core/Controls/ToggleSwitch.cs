using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn on/off switch — the modern <see cref="CheckBox"/> alternative: a pill-shaped track
/// (accent-filled while on, border-grey while off) with a themed thumb that sits left for off and
/// right for on, and an optional caption beside it. Toggles on click or Space and raises
/// <see cref="CheckedChanged"/>. The thumb snaps to its new side; there is no slide animation.
/// </summary>
public class ToggleSwitch : OwnerDrawnControl
{
    /// <summary>The pixel width of the track pill.</summary>
    internal const int TrackWidth = 36;

    /// <summary>The pixel height of the track pill — also the diameter of its rounded ends.</summary>
    internal const int TrackHeight = 16;

    /// <summary>The inset of the thumb circle within the track.</summary>
    private const int _ThumbMargin = 2;

    /// <summary>The gap between the track and the caption.</summary>
    private const int _TextGap = 6;

    /// <summary>Whether the switch is on.</summary>
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

    /// <summary>Toggles the switch and raises <see cref="Control.Click"/>.</summary>
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

        // The track: one rounded rectangle whose corner radius is half its height — a pill. The
        // accent only shows on an enabled, on switch; a disabled one keeps the grey track and
        // reports its state through the thumb side alone. Right-to-left mirrors the face: track at
        // the right edge, caption to its left, and the thumb's on-side flipped to the left end.
        var rtl = this.IsRightToLeft;
        var client = this.DisplayRectangle;
        var top = client.Y + Math.Max(0, (client.Height - TrackHeight) / 2);
        var track = new Rectangle(client.X, top, TrackWidth, TrackHeight);
        if (rtl)
            track = RtlLayout.Mirror(track, this.Width);
        var trackColor = this.Enabled && this.Checked ? theme.Accent : theme.Border;
        g.FillRoundedRectangle(trackColor, track, TrackHeight / 2);

        // The thumb: a field-colored circle hugging the off or on end of the track (sides swap in RTL).
        var diameter = TrackHeight - (2 * _ThumbMargin);
        var thumbAtFarEnd = this.Checked != rtl;
        var thumbX = track.X + (thumbAtFarEnd ? TrackWidth - diameter - _ThumbMargin : _ThumbMargin);
        var thumb = new Rectangle(thumbX, top + _ThumbMargin, diameter, diameter);
        g.FillEllipse(theme.FieldBackground, thumb);
        g.DrawEllipse(theme.Border, thumb);

        if (this.Text.Length == 0)
            return;

        var content = new Rectangle(client.X + TrackWidth + _TextGap, client.Y, client.Right - client.X - TrackWidth - _TextGap, client.Height);
        var alignment = ContentAlignment.MiddleLeft;
        if (rtl)
        {
            content = RtlLayout.Mirror(content, this.Width);
            alignment = RtlLayout.Mirror(alignment);
        }

        ContentLayout.Arrange(
            content,
            Size.Empty,
            g.MeasureText(this.Text, font),
            TextImageRelation.ImageBeforeText,
            alignment,
            out _,
            out var textRect);
        g.DrawText(this.Text, font, this.Enabled ? this.ForeColor : theme.DisabledText, textRect, alignment);
    }
}
