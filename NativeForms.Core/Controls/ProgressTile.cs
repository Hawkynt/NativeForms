using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An Explorer-style tile: an icon, a primary caption, an optional secondary caption and a themed
/// usage bar that turns a warning colour once <see cref="Value"/> reaches
/// <see cref="WarningThreshold"/> — the shape a file manager uses to show how full a drive is.
/// </summary>
/// <remarks>
/// <para>
/// Named for its shape rather than its most obvious use. The type knows nothing about drives: there
/// is no <c>DriveInfo</c> binding and no byte formatting, because <c>NativeForms.Core</c> stays
/// platform-agnostic and the paint path may not touch the filesystem (§4). Both captions are plain
/// strings the application sets, so a tile serves a mailbox quota or a download just as well as a
/// volume — and no per-frame formatting sneaks onto the paint path.
/// </para>
/// <para>
/// The bar reuses <see cref="GlyphRenderer.DrawProgressBar"/>, so a tile and a
/// <see cref="ProgressBar"/> render the same fill from the same code. With <see cref="Clickable"/>
/// the tile takes focus, highlights on hover and raises <see cref="Control.Click"/> on click or
/// Space; left alone it is as inert as a label.
/// </para>
/// </remarks>
public class ProgressTile : OwnerDrawnControl
{
    /// <summary>The padding between the tile's frame and its content.</summary>
    private const int _Padding = 8;

    /// <summary>The gap between the icon column and the text column.</summary>
    private const int _IconGap = 10;

    /// <summary>The height of the usage bar.</summary>
    private const int _BarHeight = 8;

    /// <summary>The gap above and below the usage bar.</summary>
    private const int _BarGap = 4;

    private int _maximum = 100;
    private int _value;
    private bool _hot;

    /// <summary>The icon shown at the leading edge, or <see langword="null"/> for a text-only tile.</summary>
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

    /// <summary>
    /// The line under the primary caption — "45.2 GB free of 128 GB" and the like. Formatted by the
    /// application and stored as-is, so painting never allocates. Empty hides the line.
    /// </summary>
    public string SecondaryText
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (string.Equals(field, value, StringComparison.Ordinal))
                return;

            field = value;
            this.Invalidate();
        }
    } = string.Empty;

    /// <summary>The highest value the usage bar can represent.</summary>
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
                return;

            _maximum = value;
            if (_maximum < 0)
                _maximum = 0;

            this.Value = _value;
            this.Invalidate();
        }
    }

    /// <summary>The amount used, clamped to [0, <see cref="Maximum"/>].</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, _maximum);
            if (_value == clamped)
                return;

            _value = clamped;
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>
    /// The value at which the bar switches to <see cref="WarningColor"/>, in <see cref="Value"/>'s
    /// own units. 0 — the default — leaves the warning off and the bar always accent-coloured.
    /// </summary>
    public int WarningThreshold
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

    /// <summary>The fill used past <see cref="WarningThreshold"/>. Defaults to the alert red
    /// Explorer paints a nearly-full drive in.</summary>
    public Color WarningColor
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = GlyphRenderer.Warning;

    /// <summary>
    /// Whether the tile behaves as a button: focusable, hover-highlighted, and raising
    /// <see cref="Control.Click"/> on a click or Space. Defaults to <see langword="false"/>.
    /// </summary>
    public bool Clickable
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

    /// <summary>Whether the tile paints as the selected one of a set.</summary>
    public bool Selected
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

    /// <summary>
    /// Whether to use the short, one-row layout: the icon on the left with the caption stacked directly
    /// over the usage bar on its right, the two together sized to the icon's height for a tight visual
    /// fit. The <see cref="SecondaryText"/> line is not shown in this mode. Defaults to
    /// <see langword="false"/> (the full three-line tile).
    /// </summary>
    public bool Compact
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

    /// <summary>Whether the bar is currently past <see cref="WarningThreshold"/>.</summary>
    public bool IsWarning => this.WarningThreshold > 0 && _value >= this.WarningThreshold;

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => this.Clickable;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;

        // Face: selection wins over hover, both only where they mean something.
        var face = this.Selected ? theme.SelectionBackground
            : _hot && this.Clickable ? theme.HeaderBackground
            : theme.ControlBackground;
        g.FillRectangle(face, new Rectangle(0, 0, width, height));

        var textColor = !this.Enabled ? theme.DisabledText
            : this.Selected ? theme.SelectionText
            : this.ForeColor;

        var content = new Rectangle(_Padding, _Padding, Math.Max(0, width - (2 * _Padding)), Math.Max(0, height - (2 * _Padding)));
        var font = this.Font;
        var lineHeight = g.MeasureText(this.Text, font).Height;
        if (this.Compact)
            this.PaintCompactContent(g, theme, content, textColor, font, lineHeight);
        else
            this.PaintStackContent(g, theme, content, textColor, font, lineHeight);

        if (this.Clickable && this.Focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(2, 2, width - 5, height - 5));
    }

    /// <summary>The full tile: icon on the left, then caption, usage bar and secondary caption stacked
    /// from the top, dropping bottom-up whatever no longer fits.</summary>
    private void PaintStackContent(IGraphics g, ITheme theme, Rectangle content, Color textColor, Font font, int lineHeight)
    {
        if (this.Image is { } image)
        {
            var iconTop = content.Y + Math.Max(0, (content.Height - image.Height) / 2);
            g.DrawImage(image, new Rectangle(content.X, iconTop, image.Width, image.Height));
            var shift = image.Width + _IconGap;
            content = new(content.X + shift, content.Y, Math.Max(0, content.Width - shift), content.Height);
        }

        g.PushClip(content);

        var y = content.Y;
        if (y < content.Bottom)
            g.DrawText(
                this.Text,
                font,
                textColor,
                new Rectangle(content.X, y, content.Width, Math.Min(lineHeight, content.Bottom - y)),
                ContentAlignment.MiddleLeft);

        y += lineHeight + _BarGap;
        if (y + _BarHeight <= content.Bottom)
        {
            var bar = new Rectangle(content.X, y, content.Width, _BarHeight);
            GlyphRenderer.DrawProgressBar(g, theme, bar, _value, 0, _maximum, this.IsWarning ? this.WarningColor : theme.Accent);
            y = bar.Bottom + _BarGap;

            if (this.SecondaryText.Length > 0 && y + lineHeight <= content.Bottom)
                g.DrawText(
                    this.SecondaryText,
                    font,
                    this.Enabled ? (this.Selected ? theme.SelectionText : theme.DisabledText) : theme.DisabledText,
                    new Rectangle(content.X, y, content.Width, lineHeight),
                    ContentAlignment.MiddleLeft);
        }

        g.PopClip();
    }

    /// <summary>The compact tile: the caption stacked over the usage bar as one block, centred down the
    /// content height, with the icon centred vertically beside it — so a tile taller than the caption
    /// plus bar sits balanced rather than stranding the caption at the top.</summary>
    private void PaintCompactContent(IGraphics g, ITheme theme, Rectangle content, Color textColor, Font font, int lineHeight)
    {
        var iconWidth = 0;
        if (this.Image is { } image)
        {
            var iconTop = content.Y + Math.Max(0, (content.Height - image.Height) / 2);
            g.DrawImage(image, new Rectangle(content.X, iconTop, image.Width, image.Height));
            iconWidth = image.Width + _IconGap;
        }

        var rightX = content.X + iconWidth;
        var rightW = Math.Max(0, content.Right - rightX);
        if (rightW <= 0)
            return;

        // Measure the caption and bar together as one block, then centre it down the content height —
        // the same band the icon is centred in — so the two line up whatever the tile's height.
        g.PushClip(new Rectangle(rightX, content.Y, rightW, content.Height));

        var blockHeight = lineHeight + _BarGap + _BarHeight;
        var blockTop = content.Y + Math.Max(0, (content.Height - blockHeight) / 2);

        g.DrawText(this.Text, font, textColor, new Rectangle(rightX, blockTop, rightW, lineHeight), ContentAlignment.TopLeft);
        GlyphRenderer.DrawProgressBar(g, theme, new Rectangle(rightX, blockTop + lineHeight + _BarGap, rightW, _BarHeight), _value, 0, _maximum, this.IsWarning ? this.WarningColor : theme.Accent);

        g.PopClip();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (this.Clickable && e.Button == MouseButtons.Left && HitTest.ClientContains(this, e.Location))
            this.OnClick(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!this.Clickable || _hot)
            return;

        _hot = true;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_hot)
            return;

        _hot = false;
        this.Invalidate();
    }

    /// <summary>Space activates on the key release, like every other button face in the toolkit.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (!this.Clickable || e.KeyCode is not Keys.Space)
            return;

        this.OnClick(EventArgs.Empty);
        e.Handled = true;
    }
}
