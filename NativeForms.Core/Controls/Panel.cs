using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The border a control paints around itself.</summary>
public enum BorderStyle
{
    /// <summary>No border.</summary>
    None,

    /// <summary>A single flat line.</summary>
    FixedSingle,

    /// <summary>A sunken 3-D edge.</summary>
    Fixed3D,
}

/// <summary>
/// A simple owner-drawn container that fills itself with the theme's control background and optionally
/// draws a border. A grouping surface for other controls. With <see cref="AutoScroll"/> enabled it
/// paints themed scrollbars whenever children overflow the client area and scrolls by physically
/// moving the child peers — each child's logical <see cref="Control.Bounds"/> stays untouched.
/// </summary>
public class Panel : OwnerDrawnControl
{
    private const int _NoThumbDrag = 0;
    private const int _VerticalThumbDrag = 1;
    private const int _HorizontalThumbDrag = 2;

    private Point _scroll;
    private int _thumbDrag;
    private int _thumbDragPixel;
    private int _thumbDragOrigin;

    /// <summary>The border drawn around the panel.</summary>
    public BorderStyle BorderStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = BorderStyle.None;

    /// <summary>
    /// Whether the panel scrolls children that overflow its client area. Turning it off snaps the
    /// scroll offset back to zero.
    /// </summary>
    public bool AutoScroll
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value)
                this.ScrollTo(0, 0);

            this.Invalidate();
        }
    }

    /// <summary>
    /// The scroll offset, negated exactly like its Windows Forms namesake: a panel scrolled 10px down
    /// reports (0, -10). Assigning accepts either sign and scrolls to the absolute offset.
    /// </summary>
    public Point AutoScrollPosition
    {
        get => new(-_scroll.X, -_scroll.Y);
        set => this.ScrollTo(Math.Abs(value.X), Math.Abs(value.Y));
    }

    /// <inheritdoc/>
    private protected override Rectangle GetChildPeerBounds(Control child)
    {
        var bounds = child.Bounds;
        return this.AutoScroll
            ? new(bounds.X - _scroll.X, bounds.Y - _scroll.Y, bounds.Width, bounds.Height)
            : bounds;
    }

    /// <summary>The union bottom-right corner of all children — the size of the scrollable content.</summary>
    private Size GetContentExtent()
    {
        var width = 0;
        var height = 0;
        for (var i = 0; i < this.Controls.Count; ++i)
        {
            var bounds = this.Controls[i].Bounds;
            width = Math.Max(width, bounds.Right);
            height = Math.Max(height, bounds.Bottom);
        }

        return new(width, height);
    }

    /// <summary>Determines which scrollbars are needed; each bar steals room from the other's axis.</summary>
    private void GetScrollState(out Size extent, out bool verticalBar, out bool horizontalBar, out Size viewport)
    {
        extent = this.GetContentExtent();
        if (!this.AutoScroll)
        {
            verticalBar = horizontalBar = false;
            viewport = this.Size;
            return;
        }

        var barSize = this.Theme.ScrollBarSize;
        verticalBar = extent.Height > this.Height;
        horizontalBar = extent.Width > this.Width - (verticalBar ? barSize : 0);
        verticalBar = extent.Height > this.Height - (horizontalBar ? barSize : 0);
        viewport = new(
            this.Width - (verticalBar ? barSize : 0),
            this.Height - (horizontalBar ? barSize : 0));
    }

    private Rectangle GetVerticalTrack(bool horizontalBar)
    {
        var barSize = this.Theme.ScrollBarSize;
        return new(this.Width - barSize, 0, barSize, this.Height - (horizontalBar ? barSize : 0));
    }

    private Rectangle GetHorizontalTrack(bool verticalBar)
    {
        var barSize = this.Theme.ScrollBarSize;
        return new(0, this.Height - barSize, this.Width - (verticalBar ? barSize : 0), barSize);
    }

    /// <summary>Scrolls to the given (positive) offset, clamped to the content, and moves the child peers.</summary>
    private void ScrollTo(int x, int y)
    {
        this.GetScrollState(out var extent, out _, out _, out var viewport);
        var clamped = new Point(
            Math.Clamp(x, 0, Math.Max(0, extent.Width - viewport.Width)),
            Math.Clamp(y, 0, Math.Max(0, extent.Height - viewport.Height)));
        if (clamped == _scroll)
            return;

        _scroll = clamped;
        for (var i = 0; i < this.Controls.Count; ++i)
            this.Controls[i].PushPeerBounds();

        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!this.AutoScroll)
            return;

        this.ScrollTo(_scroll.X, _scroll.Y - (Math.Sign(e.Delta) * 3 * this.Theme.RowHeight));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!this.AutoScroll || e.Button != MouseButtons.Left)
            return;

        this.GetScrollState(out var extent, out var verticalBar, out var horizontalBar, out var viewport);
        if (verticalBar)
        {
            var verticalTrack = this.GetVerticalTrack(horizontalBar);
            if (verticalTrack.Contains(e.Location))
            {
                var thumb = Drawing.ScrollBarRenderer.GetThumb(verticalTrack, vertical: true, extent.Height, viewport.Height, _scroll.Y);
                if (thumb.Contains(e.Location))
                {
                    _thumbDrag = _VerticalThumbDrag;
                    _thumbDragPixel = e.Y;
                    _thumbDragOrigin = _scroll.Y;
                }
                else
                    this.ScrollTo(_scroll.X, _scroll.Y + (e.Y < thumb.Y ? -viewport.Height : viewport.Height));

                return;
            }
        }

        if (!horizontalBar)
            return;

        var horizontalTrack = this.GetHorizontalTrack(verticalBar);
        if (!horizontalTrack.Contains(e.Location))
            return;

        var horizontalThumb = Drawing.ScrollBarRenderer.GetThumb(horizontalTrack, vertical: false, extent.Width, viewport.Width, _scroll.X);
        if (horizontalThumb.Contains(e.Location))
        {
            _thumbDrag = _HorizontalThumbDrag;
            _thumbDragPixel = e.X;
            _thumbDragOrigin = _scroll.X;
        }
        else
            this.ScrollTo(_scroll.X + (e.X < horizontalThumb.X ? -viewport.Width : viewport.Width), _scroll.Y);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_thumbDrag == _NoThumbDrag)
            return;

        this.GetScrollState(out var extent, out var verticalBar, out var horizontalBar, out var viewport);
        if (_thumbDrag == _VerticalThumbDrag)
        {
            var position = Drawing.ScrollBarRenderer.PositionFromThumbDelta(
                this.GetVerticalTrack(horizontalBar), vertical: true, extent.Height, viewport.Height, _thumbDragOrigin, e.Y - _thumbDragPixel);
            this.ScrollTo(_scroll.X, position);
        }
        else
        {
            var position = Drawing.ScrollBarRenderer.PositionFromThumbDelta(
                this.GetHorizontalTrack(verticalBar), vertical: false, extent.Width, viewport.Width, _thumbDragOrigin, e.X - _thumbDragPixel);
            this.ScrollTo(position, _scroll.Y);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _thumbDrag = _NoThumbDrag;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var full = new Rectangle(0, 0, this.Width, this.Height);
        g.FillRectangle(this.Theme.ControlBackground, full);

        switch (this.BorderStyle)
        {
            case BorderStyle.FixedSingle:
                g.DrawRectangle(this.Theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
                break;
            case BorderStyle.Fixed3D:
                g.DrawLine(this.Theme.Border, 0, 0, this.Width - 1, 0);
                g.DrawLine(this.Theme.Border, 0, 0, 0, this.Height - 1);
                break;
            case BorderStyle.None:
            default:
                break;
        }

        if (!this.AutoScroll)
            return;

        this.GetScrollState(out var extent, out var verticalBar, out var horizontalBar, out var viewport);
        if (verticalBar)
            Drawing.ScrollBarRenderer.Paint(g, this.Theme, this.GetVerticalTrack(horizontalBar), vertical: true, extent.Height, viewport.Height, _scroll.Y);

        if (horizontalBar)
            Drawing.ScrollBarRenderer.Paint(g, this.Theme, this.GetHorizontalTrack(verticalBar), vertical: false, extent.Width, viewport.Width, _scroll.X);

        if (verticalBar && horizontalBar)
            g.FillRectangle(this.Theme.ControlBackground, new Rectangle(viewport.Width, viewport.Height, this.Width - viewport.Width, this.Height - viewport.Height));
    }
}
