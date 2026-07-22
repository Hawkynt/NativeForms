using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

public partial class DockPanel
{
    private const int _DragThreshold = 4;
    private const int _GuideSize = 30;
    private const int _GuideGap = 5;
    private const int _PanelEdgeBand = 18;

    private enum Mode
    {
        None,
        Splitter,
        MaybeDrag,
        Dragging,
    }

    private Mode _mode;
    private DockSplitNode? _dragSplit;
    private int _splitGrab;

    private DockContent? _dragContent;
    private Point _dragStart;
    private DockDragOverlay? _overlay;
    private DockTabGroupNode? _dragGroup;
    private DockGuide _dropGuide;
    private DockTabGroupNode? _dropTarget;
    private Rectangle _previewRect;

    // Hover state (all value/ref, mutated only on real transitions so the pointer path stays quiet).
    private bool _sizingCursor;
    private DockTabGroupNode? _hotButtonGroup;
    private CaptionButton _hotButtonKind;
    private DockTabGroupNode? _hotTabGroup;
    private int _hotTabIndex = -1;
    private DockContent? _hotAutoHide;

    // Hover predicates the painter reads.
    private bool HotButton(DockTabGroupNode g, CaptionButton k)
        => k != CaptionButton.None && ReferenceEquals(_hotButtonGroup, g) && _hotButtonKind == k;

    private bool HotTab(DockTabGroupNode g, int i) => ReferenceEquals(_hotTabGroup, g) && _hotTabIndex == i;

    private bool HotAutoHide(DockContent p) => ReferenceEquals(_hotAutoHide, p);

    // --- Mouse ------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var pt = e.Location;

        // A click on the chrome outside the open fly-out dismisses it (its own tab toggles instead).
        if (_flyout is { } && !ReferenceEquals(this.AutoHideTabAt(pt), _flyout))
            this.HideFlyout();

        if (this.AutoHideTabAt(pt) is { } strip)
        {
            // Clicking (or hovering) an auto-hide tab flies its pane out and keeps it out; the pane
            // collapses again on a click outside it or on Escape — the Visual-Studio behaviour.
            this.ShowFlyout(strip);
            return;
        }

        if (this.SplitterAt(pt) is { } split)
        {
            _mode = Mode.Splitter;
            _dragSplit = split;
            _splitGrab = split.Orientation == Orientation.Vertical ? pt.X - split.Splitter.X : pt.Y - split.Splitter.Y;
            return;
        }

        if (this.GroupAt(pt) is { Active: { } active } group)
        {
            var button = this.CaptionButtonAt(group, active, pt);
            if (button != CaptionButton.None)
            {
                this.PerformCaptionButton(active, button);
                return;
            }

            if (group.CaptionBounds.Contains(pt))
            {
                this.ActivateContent(active);
                this.BeginMaybeDrag(active, pt);
                return;
            }

            var tab = this.TabIndexAt(group, pt);
            if (tab >= 0)
            {
                var pane = group.Contents[tab];
                this.ActivateContent(pane);
                this.BeginMaybeDrag(pane, pt);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pt = e.Location;
        switch (_mode)
        {
            case Mode.Splitter:
                this.DragSplitter(pt);
                return;
            case Mode.MaybeDrag:
                if (Math.Abs(pt.X - _dragStart.X) > _DragThreshold || Math.Abs(pt.Y - _dragStart.Y) > _DragThreshold)
                    this.BeginPaneDrag();
                else
                    return;
                this.UpdatePaneDrag(pt);
                return;
            case Mode.Dragging:
                this.UpdatePaneDrag(pt);
                return;
            default:
                this.UpdateHover(pt);
                return;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Splitter:
                _mode = Mode.None;
                _dragSplit = null;
                break;
            case Mode.Dragging:
                this.CompletePaneDrag();
                break;
        }

        _mode = Mode.None;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_mode != Mode.None)
            return;

        this.ClearSplitterCursor();
        if (_hotButtonGroup is null && _hotTabGroup is null && _hotAutoHide is null)
            return;

        _hotButtonGroup = null;
        _hotButtonKind = CaptionButton.None;
        _hotTabGroup = null;
        _hotTabIndex = -1;
        _hotAutoHide = null;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Escape || (keyData & Keys.KeyCode) == Keys.Tab && (keyData & Keys.Control) != 0;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape when _mode == Mode.Dragging:
                _dropGuide = DockGuide.None;
                this.CompletePaneDrag();
                e.Handled = true;
                break;
            case Keys.Escape when _flyout is not null:
                this.HideFlyout();
                e.Handled = true;
                break;
            case Keys.Tab when e.Control:
                this.CycleDocuments(!e.Shift);
                e.Handled = true;
                break;
        }
    }

    // --- Caption buttons --------------------------------------------------------------------------

    private void PerformCaptionButton(DockContent active, CaptionButton button)
    {
        switch (button)
        {
            case CaptionButton.Close:
                active.Close();
                break;
            case CaptionButton.Float:
                active.Float();
                break;
            case CaptionButton.Pin:
                active.ToggleAutoHide();
                break;
        }
    }

    // --- Hover ------------------------------------------------------------------------------------

    private void UpdateHover(Point pt)
    {
        this.UpdateSplitterCursor(pt);

        DockTabGroupNode? buttonGroup = null;
        var buttonKind = CaptionButton.None;
        DockTabGroupNode? tabGroup = null;
        var tabIndex = -1;

        if (this.GroupAt(pt) is { Active: { } active } group)
        {
            buttonKind = this.CaptionButtonAt(group, active, pt);
            if (buttonKind != CaptionButton.None)
                buttonGroup = group;
            else
            {
                tabIndex = this.TabIndexAt(group, pt);
                if (tabIndex >= 0)
                    tabGroup = group;
            }
        }

        var autoHide = this.AutoHideTabAt(pt);

        if (!ReferenceEquals(buttonGroup, _hotButtonGroup) || buttonKind != _hotButtonKind
            || !ReferenceEquals(tabGroup, _hotTabGroup) || tabIndex != _hotTabIndex
            || !ReferenceEquals(autoHide, _hotAutoHide))
        {
            _hotButtonGroup = buttonGroup;
            _hotButtonKind = buttonKind;
            _hotTabGroup = tabGroup;
            _hotTabIndex = tabIndex;
            _hotAutoHide = autoHide;
            this.Invalidate();
        }

        // Hovering an auto-hide tab flies its pane out.
        if (autoHide is { } pane && !ReferenceEquals(_flyout, pane))
            this.ShowFlyout(pane);
    }

    private void UpdateSplitterCursor(Point pt)
    {
        var split = this.SplitterAt(pt);
        var over = split is not null;
        if (_sizingCursor == over)
            return;

        _sizingCursor = over;
        this.SetRegionCursor(over
            ? split!.Orientation == Orientation.Horizontal ? Cursors.SizeNS : Cursors.SizeWE
            : null);
    }

    private void ClearSplitterCursor()
    {
        if (!_sizingCursor)
            return;

        _sizingCursor = false;
        this.SetRegionCursor(null);
    }

    // --- Splitter drag ----------------------------------------------------------------------------

    private void DragSplitter(Point pt)
    {
        if (_dragSplit is not { } split)
            return;

        var bounds = split.Bounds;
        if (split.Orientation == Orientation.Vertical)
        {
            var avail = Math.Max(1, bounds.Width - SplitterThickness);
            var first = pt.X - _splitGrab - bounds.X;
            split.Ratio = ClampRatio((double)first / avail, avail);
        }
        else
        {
            var avail = Math.Max(1, bounds.Height - SplitterThickness);
            var first = pt.Y - _splitGrab - bounds.Y;
            split.Ratio = ClampRatio((double)first / avail, avail);
        }

        this.PerformLayout();
        this.PushPeerVisibleTree();
        this.Invalidate();
    }

    private static double ClampRatio(double ratio, int avail)
    {
        var min = avail > 0 ? (double)_MinRegion / avail : 0.1;
        min = Math.Min(min, 0.45);
        return Math.Clamp(ratio, min, 1 - min);
    }

    // --- Pane drag + overlay ----------------------------------------------------------------------

    private void BeginMaybeDrag(DockContent content, Point start)
    {
        _mode = Mode.MaybeDrag;
        _dragContent = content;
        _dragStart = start;
    }

    private void BeginPaneDrag()
    {
        _mode = Mode.Dragging;
        _dropGuide = DockGuide.None;
        _dropTarget = null;
        _previewRect = Rectangle.Empty;

        // The overlay surface is created only for the duration of the drag, so nothing is allocated at
        // rest; it is the last child, so it composites on top of every pane.
        _overlay = new DockDragOverlay(this) { Bounds = new Rectangle(0, 0, this.Width, this.Height) };
        this.Controls.Add(_overlay);
    }

    private void UpdatePaneDrag(Point pt)
    {
        this.ComputeDropTarget(pt);
        _overlay?.Invalidate();
    }

    private void ComputeDropTarget(Point pt)
    {
        _dragGroup = this.GroupAt(pt);
        var panel = new Rectangle(0, 0, this.Width, this.Height);

        // Outer guides: docking against the whole panel's edge takes priority near the border.
        if (_root is not null)
        {
            if (pt.X <= _PanelEdgeBand) { this.SetDrop(DockGuide.PanelLeft, null, new Rectangle(0, 0, this.Width / 4, this.Height)); return; }
            if (pt.X >= this.Width - _PanelEdgeBand) { this.SetDrop(DockGuide.PanelRight, null, new Rectangle(this.Width - this.Width / 4, 0, this.Width / 4, this.Height)); return; }
            if (pt.Y <= _PanelEdgeBand) { this.SetDrop(DockGuide.PanelTop, null, new Rectangle(0, 0, this.Width, this.Height / 4)); return; }
            if (pt.Y >= this.Height - _PanelEdgeBand) { this.SetDrop(DockGuide.PanelBottom, null, new Rectangle(0, this.Height - this.Height / 4, this.Width, this.Height / 4)); return; }
        }

        if (_dragGroup is not { } group)
        {
            // Empty panel: any drop becomes a document.
            this.SetDrop(DockGuide.Center, null, panel);
            return;
        }

        var b = group.Bounds;
        var bandX = b.Width / 3;
        var bandY = b.Height / 3;
        DockGuide guide;
        Rectangle preview;
        if (pt.X < b.X + bandX && pt.X - b.X <= b.Bottom - pt.Y && pt.X - b.X <= pt.Y - b.Y)
        { guide = DockGuide.Left; preview = new Rectangle(b.X, b.Y, b.Width / 2, b.Height); }
        else if (pt.X > b.Right - bandX && b.Right - pt.X <= b.Bottom - pt.Y && b.Right - pt.X <= pt.Y - b.Y)
        { guide = DockGuide.Right; preview = new Rectangle(b.X + b.Width / 2, b.Y, b.Width - b.Width / 2, b.Height); }
        else if (pt.Y < b.Y + bandY)
        { guide = DockGuide.Top; preview = new Rectangle(b.X, b.Y, b.Width, b.Height / 2); }
        else if (pt.Y > b.Bottom - bandY)
        { guide = DockGuide.Bottom; preview = new Rectangle(b.X, b.Y + b.Height / 2, b.Width, b.Height - b.Height / 2); }
        else
        { guide = DockGuide.Center; preview = b; }

        this.SetDrop(guide, group, preview);
    }

    private void SetDrop(DockGuide guide, DockTabGroupNode? target, Rectangle preview)
    {
        _dropGuide = guide;
        _dropTarget = target;
        _previewRect = preview;
    }

    private void CompletePaneDrag()
    {
        var content = _dragContent;
        var guide = _dropGuide;
        var target = _dropTarget;

        if (_overlay is { } overlay)
        {
            this.Controls.Remove(overlay);
            _overlay = null;
        }

        _mode = Mode.None;
        _dragContent = null;
        _dragGroup = null;
        _previewRect = Rectangle.Empty;

        if (content is null || guide == DockGuide.None)
        {
            this.Invalidate();
            return;
        }

        this.PerformDrop(content, guide, target);
    }

    private void PerformDrop(DockContent content, DockGuide guide, DockTabGroupNode? target)
    {
        this.DetachFromCurrent(content);
        if (target is not null && !this.NodeInTree(target))
            target = null;

        switch (guide)
        {
            case DockGuide.PanelLeft:
            case DockGuide.PanelTop:
            case DockGuide.PanelRight:
            case DockGuide.PanelBottom:
                content.SetEdgeInternal(guide switch
                {
                    DockGuide.PanelLeft => DockEdge.Left,
                    DockGuide.PanelTop => DockEdge.Top,
                    DockGuide.PanelRight => DockEdge.Right,
                    _ => DockEdge.Bottom,
                });
                this.EnsureChildOfPanel(content);
                this.DockToEdgeInternal(content);
                content.SetStateInternal(DockState.Docked);
                break;
            case DockGuide.Center when target is null:
                this.EnsureChildOfPanel(content);
                this.AddToGroup(this.EnsureDocumentGroup(), content);
                content.SetStateInternal(DockState.Document);
                break;
            default:
                this.EnsureChildOfPanel(content);
                if (target is null)
                {
                    this.AddToGroup(this.EnsureDocumentGroup(), content);
                    content.SetStateInternal(DockState.Document);
                }
                else
                    this.DockRelative(content, target, guide);
                break;
        }

        this.SetActive(content);
        this.CommitLayout();
    }

    // --- Fly-out ----------------------------------------------------------------------------------

    private void ShowFlyout(DockContent pane)
    {
        _flyout = pane;
        this.RaiseToTop(pane);
        this.SetActive(pane);
        this.CommitLayout();
    }

    private void HideFlyout()
    {
        if (_flyout is null)
            return;

        // Collapsing hands the active caption back to the docked tree, so nothing keeps reporting the
        // now-hidden fly-out pane as active.
        var wasActive = ReferenceEquals(_active, _flyout);
        _flyout = null;
        if (wasActive)
            this.SetActive(this.FirstTreeContent());
        this.CommitLayout();
    }

    private Rectangle FlyoutBounds(DockContent pane, Rectangle inner)
    {
        var t = this.AutoHideThickness;
        return pane.DockEdge switch
        {
            DockEdge.Left => new Rectangle(t, 0, Math.Min(300, Math.Max(_MinRegion, this.Width - t)), this.Height),
            DockEdge.Right => new Rectangle(this.Width - t - Math.Min(300, this.Width - t), 0, Math.Min(300, this.Width - t), this.Height),
            DockEdge.Top => new Rectangle(0, t, this.Width, Math.Min(300, Math.Max(_MinRegion, this.Height - t))),
            _ => new Rectangle(0, this.Height - t - Math.Min(300, this.Height - t), this.Width, Math.Min(300, this.Height - t)),
        };
    }

    // --- Ctrl+Tab ---------------------------------------------------------------------------------

    private void CycleDocuments(bool forward)
    {
        if (_documentGroup is not { } group || group.Contents.Count <= 1 || !this.NodeInTree(group))
            return;

        var count = group.Contents.Count;
        group.ActiveIndex = ((group.ActiveIndex + (forward ? 1 : -1)) % count + count) % count;
        this.SetActive(group.Active);
        this.CommitLayout();
    }

    // --- Overlay paint (delegated here so all drag visuals live in one place) ----------------------

    internal void PaintOverlay(IGraphics g, ITheme theme)
    {
        if (_mode != Mode.Dragging)
            return;

        // A gentle scrim, then the translucent landing preview, then the guide diamond on top.
        g.FillRectangle(Color.FromArgb(44, 0, 0, 0), new Rectangle(0, 0, this.Width, this.Height));

        if (!_previewRect.IsEmpty)
        {
            g.FillRectangle(Color.FromArgb(90, theme.Accent), _previewRect);
            g.DrawRectangle(theme.Accent, new Rectangle(_previewRect.X, _previewRect.Y, _previewRect.Width - 1, _previewRect.Height - 1), 2);
        }

        this.PaintGuideDiamond(g, theme);
        this.PaintPanelGuides(g, theme);
    }

    private void PaintGuideDiamond(IGraphics g, ITheme theme)
    {
        var center = _dragGroup is { } group
            ? new Point(group.Bounds.X + group.Bounds.Width / 2, group.Bounds.Y + group.Bounds.Height / 2)
            : new Point(this.Width / 2, this.Height / 2);

        var step = _GuideSize + _GuideGap;
        this.PaintGuideButton(g, theme, new Point(center.X, center.Y), DockGuide.Center, GlyphDirection.Down);
        this.PaintGuideButton(g, theme, new Point(center.X - step, center.Y), DockGuide.Left, GlyphDirection.Left);
        this.PaintGuideButton(g, theme, new Point(center.X + step, center.Y), DockGuide.Right, GlyphDirection.Right);
        this.PaintGuideButton(g, theme, new Point(center.X, center.Y - step), DockGuide.Top, GlyphDirection.Up);
        this.PaintGuideButton(g, theme, new Point(center.X, center.Y + step), DockGuide.Bottom, GlyphDirection.Down);
    }

    private void PaintPanelGuides(IGraphics g, ITheme theme)
    {
        var h = _GuideSize / 2;
        this.PaintGuideButton(g, theme, new Point(_PanelEdgeBand + h, this.Height / 2), DockGuide.PanelLeft, GlyphDirection.Left);
        this.PaintGuideButton(g, theme, new Point(this.Width - _PanelEdgeBand - h, this.Height / 2), DockGuide.PanelRight, GlyphDirection.Right);
        this.PaintGuideButton(g, theme, new Point(this.Width / 2, _PanelEdgeBand + h), DockGuide.PanelTop, GlyphDirection.Up);
        this.PaintGuideButton(g, theme, new Point(this.Width / 2, this.Height - _PanelEdgeBand - h), DockGuide.PanelBottom, GlyphDirection.Down);
    }

    private void PaintGuideButton(IGraphics g, ITheme theme, Point center, DockGuide guide, GlyphDirection arrow)
    {
        var rect = new Rectangle(center.X - _GuideSize / 2, center.Y - _GuideSize / 2, _GuideSize, _GuideSize);
        var active = _dropGuide == guide;
        g.FillRoundedRectangle(active ? theme.Accent : theme.ControlBackground, rect, 4);
        g.DrawRoundedRectangle(theme.Border, rect, 4);
        var glyphColor = active ? theme.SelectionText : theme.ControlText;
        var inset = _GuideSize / 3;
        var glyph = new Rectangle(rect.X + inset, rect.Y + inset, rect.Width - 2 * inset, rect.Height - 2 * inset);
        if (guide is DockGuide.Center)
            g.DrawRectangle(glyphColor, glyph);
        else
            Glyphs.PaintTriangle(g, glyphColor, glyph, arrow);
    }
}
