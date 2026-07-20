using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn container split into two <see cref="Panel"/>s by a draggable bar. The panels are
/// permanent nested children; the splitter is the only strip of the control's own surface left
/// exposed, so mouse input there drags it — with live relayout while the button is held and a single
/// <see cref="SplitterMoved"/> once it is released. Arrow keys nudge the splitter when it has focus.
/// </summary>
public class SplitContainer : OwnerDrawnControl
{
    private const int _KeyboardStep = 8;
    private const int _GripDotCount = 3;
    private const int _GripDotSize = 2;
    private const int _GripDotSpacing = 6;

    private int _splitterDistance = 50;
    private bool _dragging;
    private int _dragOffset;

    /// <summary>Creates a split container with its two panels already parented.</summary>
    public SplitContainer()
    {
        this.Panel1 = new();
        this.Panel2 = new();
        this.Controls.AddRange(this.Panel1, this.Panel2);
    }

    /// <summary>The left (or top) panel.</summary>
    public Panel Panel1 { get; }

    /// <summary>The right (or bottom) panel.</summary>
    public Panel Panel2 { get; }

    /// <summary>
    /// The direction of the splitter bar. The default <see cref="Orientation.Vertical"/> puts the
    /// panels side by side; <see cref="Orientation.Horizontal"/> stacks them.
    /// </summary>
    public Orientation Orientation
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.LayoutPanels();
            this.Invalidate();
        }
    } = Orientation.Vertical;

    /// <summary>
    /// The pixel size of <see cref="Panel1"/> along the split axis. Assignments clamp to
    /// <see cref="Panel1MinSize"/>/<see cref="Panel2MinSize"/> against the current control size.
    /// </summary>
    public int SplitterDistance
    {
        get => _splitterDistance;
        set => this.ApplyDistance(value);
    }

    /// <summary>The thickness of the splitter bar in pixels.</summary>
    public int SplitterWidth
    {
        get => field;
        set
        {
            value = Math.Max(1, value);
            if (field == value)
                return;

            field = value;
            this.LayoutPanels();
            this.Invalidate();
        }
    } = 4;

    /// <summary>The smallest size <see cref="Panel1"/> may be squeezed to.</summary>
    public int Panel1MinSize
    {
        get => field;
        set
        {
            field = Math.Max(0, value);
            this.ApplyDistance(_splitterDistance);
        }
    } = 25;

    /// <summary>The smallest size <see cref="Panel2"/> may be squeezed to.</summary>
    public int Panel2MinSize
    {
        get => field;
        set
        {
            field = Math.Max(0, value);
            this.ApplyDistance(_splitterDistance);
        }
    } = 25;

    /// <summary>Raised when a splitter move is committed (mouse release or keyboard nudge).</summary>
    public event EventHandler? SplitterMoved;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SplitterMoved"/>.</summary>
    protected virtual void OnSplitterMoved(EventArgs e) => this.SplitterMoved?.Invoke(this, e);

    /// <summary>Re-clamps the distance and re-applies both panels' bounds — the split container owns
    /// its panels' bounds, so the base Anchor/Dock engine is replaced wholesale. Each panel then
    /// lays out its own children per their Anchor/Dock like any plain container. A no-op while the
    /// control has no size yet, so constructing it never clamps the default distance away.</summary>
    private protected override void OnLayout()
    {
        if (this.Width == 0 && this.Height == 0)
            return;

        this.ApplyDistance(_splitterDistance);
        this.LayoutPanels();
    }

    private int ClampDistance(int value)
    {
        var available = (this.Orientation == Orientation.Vertical ? this.Width : this.Height) - this.SplitterWidth - this.Panel2MinSize;
        return Math.Max(this.Panel1MinSize, Math.Min(value, available));
    }

    /// <summary>Clamps and applies a new distance; returns whether it actually moved.</summary>
    private bool ApplyDistance(int value)
    {
        var clamped = this.ClampDistance(value);
        if (clamped == _splitterDistance)
            return false;

        _splitterDistance = clamped;
        this.LayoutPanels();
        this.Invalidate();
        return true;
    }

    /// <summary>Recomputes both panels' bounds from the current distance, orientation and size.</summary>
    private void LayoutPanels()
    {
        var distance = _splitterDistance;
        var splitterWidth = this.SplitterWidth;
        if (this.Orientation == Orientation.Vertical)
        {
            this.Panel1.Bounds = new(0, 0, distance, this.Height);
            this.Panel2.Bounds = new(distance + splitterWidth, 0, Math.Max(0, this.Width - distance - splitterWidth), this.Height);
        }
        else
        {
            this.Panel1.Bounds = new(0, 0, this.Width, distance);
            this.Panel2.Bounds = new(0, distance + splitterWidth, this.Width, Math.Max(0, this.Height - distance - splitterWidth));
        }
    }

    /// <summary>The strip of the control's own surface occupied by the splitter bar.</summary>
    private Rectangle GetSplitterBounds()
        => this.Orientation == Orientation.Vertical
            ? new(_splitterDistance, 0, this.SplitterWidth, this.Height)
            : new(0, _splitterDistance, this.Width, this.SplitterWidth);

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left || !this.GetSplitterBounds().Contains(e.Location))
            return;

        _dragging = true;
        _dragOffset = (this.Orientation == Orientation.Vertical ? e.X : e.Y) - _splitterDistance;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        this.ApplyDistance((this.Orientation == Orientation.Vertical ? e.X : e.Y) - _dragOffset);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        this.OnSplitterMoved(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var vertical = this.Orientation == Orientation.Vertical;
        var handled = true;
        var moved = false;
        switch (e.KeyCode)
        {
            case Keys.Left when vertical:
            case Keys.Up when !vertical:
                moved = this.ApplyDistance(_splitterDistance - _KeyboardStep);
                break;
            case Keys.Right when vertical:
            case Keys.Down when !vertical:
                moved = this.ApplyDistance(_splitterDistance + _KeyboardStep);
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
        if (moved)
            this.OnSplitterMoved(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        // A subtle grip: three dots centered on the bar.
        var splitter = this.GetSplitterBounds();
        var centerX = splitter.X + (splitter.Width / 2);
        var centerY = splitter.Y + (splitter.Height / 2);
        for (var i = 0; i < _GripDotCount; ++i)
        {
            var offset = (i - (_GripDotCount / 2)) * _GripDotSpacing;
            var dot = this.Orientation == Orientation.Vertical
                ? new Rectangle(centerX - (_GripDotSize / 2), centerY + offset - (_GripDotSize / 2), _GripDotSize, _GripDotSize)
                : new Rectangle(centerX + offset - (_GripDotSize / 2), centerY - (_GripDotSize / 2), _GripDotSize, _GripDotSize);
            g.FillRectangle(theme.Border, dot);
        }
    }
}
