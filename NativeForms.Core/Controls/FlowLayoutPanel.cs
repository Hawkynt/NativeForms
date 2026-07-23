using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A <see cref="Panel"/> that positions its own children: they flow in <see cref="Control.Controls"/>
/// order along <see cref="FlowDirection"/>, each keeping its own <see cref="Control.Size"/> and offset
/// by its <see cref="Control.Margin"/>, wrapping into a new row or column at the client edge while
/// <see cref="WrapContents"/> is on. Layout runs in logical space, so <see cref="Panel.AutoScroll"/>
/// sees the flowed extent and scrolls the overflow by moving peers.
/// </summary>
public class FlowLayoutPanel : Panel
{
    /// <summary>The edge children flow from and the axis they advance along.</summary>
    public FlowDirection FlowDirection
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
        }
    } = FlowDirection.LeftToRight;

    /// <summary>Whether the flow wraps at the client edge; off keeps a single row or column.</summary>
    public bool WrapContents
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PerformLayout();
        }
    } = true;

    /// <summary>
    /// Repositions every child along the flow in a single pass. Runs automatically whenever the
    /// panel resizes, a child joins, leaves or resizes, or a flow property changes. The flow runs
    /// inside <see cref="Control.DisplayRectangle"/>, so <see cref="Control.Padding"/> insets it on
    /// every side. The flow owns every child's position, so
    /// <see cref="Control.Anchor"/>/<see cref="Control.Dock"/> are ignored — the Windows Forms
    /// flow-panel contract.
    /// </summary>
    /// <summary>The flow positions every child itself, so a new one — anchored or not — re-flows the
    /// panel; the base only re-flows for a docked child, which a flow child never is.</summary>
    private protected override void OnChildAdded(Control child) => this.PerformLayout();

    private protected override void OnLayout()
    {
        var direction = this.FlowDirection;
        var horizontal = direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;
        var display = this.DisplayRectangle;
        var lineExtent = horizontal ? display.Width : display.Height;
        var main = 0;
        var cross = 0;
        var lineSize = 0;
        for (var i = 0; i < this.Controls.Count; ++i)
        {
            var child = this.Controls[i];
            var margin = child.Margin;
            var size = child.Size;
            var mainConsumed = horizontal ? margin.Horizontal + size.Width : margin.Vertical + size.Height;
            var crossConsumed = horizontal ? margin.Vertical + size.Height : margin.Horizontal + size.Width;
            if (this.WrapContents && main > 0 && main + mainConsumed > lineExtent)
            {
                main = 0;
                cross += lineSize;
                lineSize = 0;
            }

            var location = direction switch
            {
                FlowDirection.LeftToRight => new Point(display.X + main + margin.Left, display.Y + cross + margin.Top),
                FlowDirection.RightToLeft => new Point(display.Right - main - margin.Right - size.Width, display.Y + cross + margin.Top),
                FlowDirection.TopDown => new Point(display.X + cross + margin.Left, display.Y + main + margin.Top),
                _ => new Point(display.X + cross + margin.Left, display.Bottom - main - margin.Bottom - size.Height),
            };
            child.Bounds = new(location, size);
            main += mainConsumed;
            lineSize = Math.Max(lineSize, crossConsumed);
        }

        this.Invalidate();
    }
}
