using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn status bar: a row of <see cref="ToolStripStatusLabel"/> panels (text + icon),
/// embedded <see cref="ToolStripProgressBarItem"/> gauges and an optional size grip in the
/// bottom-right corner. Fixed panels take their measured width; panels marked
/// <see cref="ToolStripStatusLabel.Spring"/> share whatever width is left over.
/// </summary>
public class StatusStrip : OwnerDrawnControl
{
    /// <summary>The horizontal padding inside a panel.</summary>
    internal const int PanelPadding = 4;

    /// <summary>The edge length of a panel icon.</summary>
    internal const int IconSize = 16;

    /// <summary>The square the size grip occupies in the bottom-right corner.</summary>
    internal const int GripSize = 14;

    /// <summary>Creates an empty status bar with the size grip shown.</summary>
    public StatusStrip()
    {
        this.Items = new();
        this.Items.Changed += (_, _) => this.Invalidate();
    }

    /// <summary>The panels. Mutating the collection (or any item in it) repaints the bar.</summary>
    public ToolStripItemCollection Items { get; }

    /// <summary>Whether the resize grip is painted in the bottom-right corner.</summary>
    public bool SizingGrip
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = true;

    /// <summary>The pixel width panel <paramref name="index"/> currently occupies — fixed panels
    /// their measured width, springs their equal share of the leftover.</summary>
    public int GetItemWidth(int index)
    {
        this.MeasureSprings(out var springWidth, out var springRemainder, out _);
        var springsSeen = 0;
        for (var i = 0; i < index; ++i)
            if (this.Items[i] is ToolStripStatusLabel { Spring: true } && this.Items[i].Visible)
                ++springsSeen;

        var item = this.Items[index];
        return item is ToolStripStatusLabel { Spring: true }
            ? springWidth + (springsSeen < springRemainder ? 1 : 0)
            : this.FixedWidth(item);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var height = this.Height;
        g.FillRectangle(theme.ControlBackground, new(0, 0, this.Width, height));
        g.DrawLine(theme.Border, 0, 0, this.Width - 1, 0);

        this.MeasureSprings(out var springWidth, out var springRemainder, out _);
        var springsSeen = 0;
        var x = 0;
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            int width;
            if (item is ToolStripStatusLabel { Spring: true })
            {
                width = springWidth + (springsSeen < springRemainder ? 1 : 0);
                ++springsSeen;
            }
            else
                width = this.FixedWidth(item);

            this.PaintPanel(g, item, new(x, 0, width, height));
            x += width;
        }

        if (this.SizingGrip)
            PaintGrip(g, theme, this.Width, height);
    }

    /// <summary>Paints one panel: icon, caption, or the embedded progress gauge.</summary>
    private void PaintPanel(IGraphics g, ToolStripItem item, Rectangle bounds)
    {
        var theme = this.Theme;
        if (item is ToolStripProgressBarItem progress)
        {
            progress.Paint(g, theme, new(bounds.X + 2, bounds.Y + 3, bounds.Width - 4, bounds.Height - 6));
            return;
        }

        var x = bounds.X + PanelPadding;
        var icon = item.ResolveImage(this.Backend);
        if (icon is not null)
        {
            g.DrawImage(icon, new(x, bounds.Y + ((bounds.Height - IconSize) / 2), IconSize, IconSize));
            x += IconSize + (item.DisplayText.Length > 0 ? PanelPadding : 0);
        }

        if (item.DisplayText.Length == 0)
            return;

        var textColor = item.Enabled ? theme.ControlText : theme.DisabledText;
        g.DrawText(item.DisplayText, theme.DefaultFont, textColor, new(x, bounds.Y, bounds.Right - x, bounds.Height), ContentAlignment.MiddleLeft);
    }

    /// <summary>Paints the diagonal dot grip in the bottom-right corner.</summary>
    private static void PaintGrip(IGraphics g, ITheme theme, int width, int height)
    {
        // Three diagonal rows of 2×2 dots, shortest row innermost — the classic resize affordance.
        for (var row = 0; row < 3; ++row)
            for (var dot = 0; dot <= row; ++dot)
            {
                var x = width - 3 - (row * 4) + (dot * 4);
                var y = height - 3 - (dot * 4);
                g.FillRectangle(theme.DisabledText, new(x, y, 2, 2));
            }
    }

    /// <summary>The measured width of a fixed (non-spring) panel.</summary>
    private int FixedWidth(ToolStripItem item)
    {
        if (item is ToolStripProgressBarItem progress)
            return progress.Width;

        var width = 2 * PanelPadding;
        if (item.HasIcon)
            width += IconSize + (item.DisplayText.Length > 0 ? PanelPadding : 0);

        if (item.DisplayText.Length > 0)
            width += this.Backend?.MeasureText(item.DisplayText, this.Theme.DefaultFont).Width ?? 0;

        return width;
    }

    /// <summary>
    /// Splits the bar's width into the fixed part and the spring share: each spring gets
    /// <paramref name="springWidth"/> pixels and the first <paramref name="springRemainder"/> springs
    /// one extra, so the panels always tile the full width exactly.
    /// </summary>
    private void MeasureSprings(out int springWidth, out int springRemainder, out int springCount)
    {
        var fixedTotal = this.SizingGrip ? GripSize : 0;
        springCount = 0;
        for (var i = 0; i < this.Items.Count; ++i)
        {
            var item = this.Items[i];
            if (!item.Visible)
                continue;

            if (item is ToolStripStatusLabel { Spring: true })
                ++springCount;
            else
                fixedTotal += this.FixedWidth(item);
        }

        var leftover = Math.Max(0, this.Width - fixedTotal);
        springWidth = springCount > 0 ? leftover / springCount : 0;
        springRemainder = springCount > 0 ? leftover % springCount : 0;
    }
}
