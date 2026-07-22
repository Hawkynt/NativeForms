using System.Collections.Generic;
using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A Visual-Studio-style docking manager (§7.2): a container that hosts movable <see cref="DockContent"/>
/// panes around a central document tab well. It owns the layout tree — nested splitter regions whose
/// leaves are tab groups — and paints every caption bar, tab strip, splitter and auto-hide edge strip
/// itself in the native theme, so the whole arrangement is drawn and hit-tested in one place. Panes can
/// be docked to an edge, tabbed together, split apart with draggable splitters, torn off into floating
/// windows, or collapsed to auto-hide strips that fly out on hover; dragging a caption raises docking
/// overlay guides with a translucent landing preview. The arrangement round-trips through
/// <see cref="SaveLayout"/>/<see cref="LoadLayout(string,System.Func{string,DockContent})"/>.
/// </summary>
/// <remarks>
/// Only the panes actually on screen (docked or in the document well) live in the layout tree; floating,
/// auto-hidden and hidden panes live in side lists, so hidden panes cost nothing and the paint path
/// never measures or allocates in steady state. The drag overlay is a child surface created only while a
/// drag is in flight and torn down on drop, so it allocates nothing at rest.
/// </remarks>
public partial class DockPanel : OwnerDrawnControl
{
    internal const int SplitterThickness = 5;
    private const int _MinRegion = 60;
    private const double _EdgeFraction = 0.25;

    private DockNode? _root;
    private DockTabGroupNode? _documentGroup;
    private readonly DockTabGroupNode?[] _edgeGroups = new DockTabGroupNode?[4];

    private List<DockContent>? _contents;
    private List<DockContent>? _autoHide;
    private List<DockFloatWindow>? _floatWindows;

    private DockContent? _active;
    private DockContent? _flyout;

    /// <summary>Creates an empty docking manager.</summary>
    public DockPanel() { }

    /// <summary>The icons <see cref="DockContent.ImageIndex"/> indexes into, or <see langword="null"/>.</summary>
    public ImageList? ImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>Every pane the manager owns, in the order they were added.</summary>
    public IReadOnlyList<DockContent> Contents => (IReadOnlyList<DockContent>?)_contents ?? [];

    /// <summary>The pane with the active caption, or <see langword="null"/> when the panel is empty.</summary>
    public DockContent? ActiveContent => _active;

    /// <summary>Raised after <see cref="ActiveContent"/> changes.</summary>
    public event EventHandler? ActiveContentChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    private int CaptionHeight => this.Theme.RowHeight;
    private int TabStripHeight => this.Theme.RowHeight;
    private int AutoHideThickness => this.Theme.RowHeight;

    // --- Public API -------------------------------------------------------------------------------

    /// <summary>
    /// Adds a pane to the manager in the given state. A <see cref="DockState.Docked"/> or
    /// <see cref="DockState.AutoHide"/> pane clings to <paramref name="edge"/>; a
    /// <see cref="DockState.Document"/> pane joins the central well.
    /// </summary>
    public void Add(DockContent content, DockState state = DockState.Document, DockEdge edge = DockEdge.Left)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (ReferenceEquals(content.DockPanel, this))
        {
            content.SetEdgeInternal(edge);
            this.SetContentState(content, state);
            return;
        }

        content.DockPanel = this;
        content.SetEdgeInternal(edge);
        (_contents ??= []).Add(content);
        if (!ReferenceEquals(content.Parent, this))
            this.Controls.Add(content);

        // Adopt from the pane's currently-recorded state into the requested one.
        content.SetStateInternal(DockState.Hidden);
        this.SetContentState(content, state);
    }

    /// <summary>Docks a pane to one of the four edges, joining any existing group on that edge.</summary>
    public void DockToEdge(DockContent content, DockEdge edge)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.SetEdgeInternal(edge);
        this.SetContentState(content, DockState.Docked);
    }

    /// <summary>Adds a pane to the central document well.</summary>
    public void AddDocument(DockContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.SetContentState(content, DockState.Document);
    }

    /// <summary>Re-docks a docked or auto-hidden pane to a different edge.</summary>
    internal void ReDockToEdge(DockContent content, DockEdge edge)
    {
        content.SetEdgeInternal(edge);
        var state = content.DockState;
        if (state == DockState.Docked)
        {
            this.DetachFromCurrent(content);
            this.DockToEdgeInternal(content);
            this.CommitLayout();
        }
        else if (state == DockState.AutoHide)
            this.CommitLayout();
    }

    // --- State machine ----------------------------------------------------------------------------

    /// <summary>Moves a pane into a new <see cref="DockState"/>, the single writer of pane placement.</summary>
    internal void SetContentState(DockContent content, DockState state)
    {
        if (_contents is null || !_contents.Contains(content))
        {
            // Unowned pane: adopt it, which routes back here once parented.
            this.Add(content, state, content.DockEdge);
            return;
        }

        if (content.DockState == state && state != DockState.Docked)
            return;

        this.DetachFromCurrent(content);

        switch (state)
        {
            case DockState.Document:
                this.EnsureChildOfPanel(content);
                this.AddToGroup(this.EnsureDocumentGroup(), content);
                break;
            case DockState.Docked:
                this.EnsureChildOfPanel(content);
                this.DockToEdgeInternal(content);
                break;
            case DockState.AutoHide:
                this.EnsureChildOfPanel(content);
                this.RaiseToTop(content);
                (_autoHide ??= []).Add(content);
                break;
            case DockState.Floating:
                this.MoveToFloat(content);
                break;
            case DockState.Hidden:
            default:
                this.EnsureChildOfPanel(content);
                break;
        }

        content.SetStateInternal(state);
        if (state is DockState.Document or DockState.Docked)
            this.SetActive(content);
        else if (ReferenceEquals(_active, content))
            this.SetActive(this.FirstTreeContent());

        this.CommitLayout();
    }

    /// <summary>Closes a pane after its vetoable <see cref="DockContent.CloseRequested"/> pipeline.</summary>
    internal void CloseContent(DockContent content)
    {
        if (_contents is null || !_contents.Contains(content))
            return;
        if (!content.RequestClose())
            return;

        this.DetachFromCurrent(content);
        _contents.Remove(content);
        content.SetStateInternal(DockState.Hidden);
        var wasActive = ReferenceEquals(_active, content);
        content.DockPanel = null;
        if (ReferenceEquals(content.Parent, this))
            this.Controls.Remove(content);

        if (wasActive)
            this.SetActive(this.FirstTreeContent());
        this.CommitLayout();
    }

    /// <summary>Brings a pane to the front of its tab group and gives it the active caption.</summary>
    internal void ActivateContent(DockContent content)
    {
        if (this.FindGroup(content) is { } group)
        {
            var index = group.Contents.IndexOf(content);
            if (index >= 0)
                group.ActiveIndex = index;
        }
        else if (content.DockState == DockState.AutoHide)
            this.ShowFlyout(content);

        this.SetActive(content);
        this.CommitLayout();
    }

    private void SetActive(DockContent? content)
    {
        if (ReferenceEquals(_active, content))
            return;

        _active = content;
        this.ActiveContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Detach / attach --------------------------------------------------------------------------

    private void DetachFromCurrent(DockContent content)
    {
        switch (content.DockState)
        {
            case DockState.Document:
            case DockState.Docked:
                this.RemoveFromTree(content);
                break;
            case DockState.AutoHide:
                _autoHide?.Remove(content);
                if (ReferenceEquals(_flyout, content))
                    _flyout = null;
                break;
            case DockState.Floating:
                this.MoveFromFloat(content);
                break;
        }
    }

    private void EnsureChildOfPanel(DockContent content)
    {
        if (!ReferenceEquals(content.Parent, this))
            this.Controls.Add(content);
    }

    /// <summary>Re-adds the pane so its peer sits last in z-order — needed so an auto-hide fly-out or a
    /// re-docked pane paints over its neighbours. A rare, state-transition-only cost.</summary>
    private void RaiseToTop(DockContent content)
    {
        if (!ReferenceEquals(content.Parent, this))
            return;
        if (this.Controls.Count > 0 && ReferenceEquals(this.Controls[this.Controls.Count - 1], content))
            return;

        this.Controls.Remove(content);
        this.Controls.Add(content);
    }

    // --- Tree operations --------------------------------------------------------------------------

    private DockTabGroupNode EnsureDocumentGroup()
    {
        if (_documentGroup is { } existing && this.NodeInTree(existing))
            return existing;

        var group = new DockTabGroupNode { IsDocument = true };
        _documentGroup = group;
        if (_root is null)
            _root = group;
        else
            _root = new DockSplitNode(Orientation.Vertical, 1 - _EdgeFraction, _root, group);

        return group;
    }

    private void DockToEdgeInternal(DockContent content)
    {
        var edge = content.DockEdge;
        var slot = (int)edge;
        if (_edgeGroups[slot] is { } group && this.NodeInTree(group))
        {
            this.AddToGroup(group, content);
            return;
        }

        var fresh = new DockTabGroupNode();
        fresh.Contents.Add(content);
        _edgeGroups[slot] = fresh;

        if (_root is null)
        {
            _root = fresh;
            return;
        }

        _root = edge switch
        {
            DockEdge.Left => new DockSplitNode(Orientation.Vertical, _EdgeFraction, fresh, _root),
            DockEdge.Right => new DockSplitNode(Orientation.Vertical, 1 - _EdgeFraction, _root, fresh),
            DockEdge.Top => new DockSplitNode(Orientation.Horizontal, _EdgeFraction, fresh, _root),
            _ => new DockSplitNode(Orientation.Horizontal, 1 - _EdgeFraction, _root, fresh),
        };
    }

    private void AddToGroup(DockTabGroupNode group, DockContent content)
    {
        if (!group.Contents.Contains(content))
            group.Contents.Add(content);
        group.ActiveIndex = group.Contents.IndexOf(content);
    }

    private void RemoveFromTree(DockContent content)
    {
        if (this.FindGroup(content) is not { } group)
            return;

        group.Contents.Remove(content);
        group.ClampActive();
        if (group.Contents.Count > 0)
            return;

        // Empty group: drop it and collapse its parent split into the surviving sibling.
        for (var i = 0; i < _edgeGroups.Length; ++i)
            if (ReferenceEquals(_edgeGroups[i], group))
                _edgeGroups[i] = null;
        if (ReferenceEquals(_documentGroup, group))
            _documentGroup = null;

        if (this.ParentOf(group) is { } parent)
        {
            var sibling = ReferenceEquals(parent.First, group) ? parent.Second : parent.First;
            this.ReplaceNode(parent, sibling);
        }
        else if (ReferenceEquals(_root, group))
            _root = null;
    }

    /// <summary>Splits <paramref name="target"/> to place <paramref name="content"/> on the given side,
    /// or tabs it into <paramref name="target"/> for <see cref="DockGuide.Center"/>. Used by the drag
    /// drop and by programmatic relative docking.</summary>
    private void DockRelative(DockContent content, DockTabGroupNode target, DockGuide guide)
    {
        if (guide == DockGuide.Center)
        {
            this.AddToGroup(target, content);
            content.SetStateInternal(target.IsDocument ? DockState.Document : DockState.Docked);
            return;
        }

        var fresh = new DockTabGroupNode { IsDocument = target.IsDocument && guide is DockGuide.Left or DockGuide.Right or DockGuide.Top or DockGuide.Bottom ? false : target.IsDocument };
        fresh.Contents.Add(content);

        var (orientation, firstIsNew, ratio) = guide switch
        {
            DockGuide.Left => (Orientation.Vertical, true, _EdgeFraction),
            DockGuide.Right => (Orientation.Vertical, false, 1 - _EdgeFraction),
            DockGuide.Top => (Orientation.Horizontal, true, _EdgeFraction),
            _ => (Orientation.Horizontal, false, 1 - _EdgeFraction),
        };

        var split = firstIsNew
            ? new DockSplitNode(orientation, ratio, fresh, target)
            : new DockSplitNode(orientation, ratio, target, fresh);
        this.ReplaceNode(target, split);
        content.SetStateInternal(DockState.Docked);
    }

    private DockTabGroupNode? FindGroup(DockContent content) => FindGroup(_root, content);

    private static DockTabGroupNode? FindGroup(DockNode? node, DockContent content)
    {
        switch (node)
        {
            case DockTabGroupNode group:
                return group.Contents.Contains(content) ? group : null;
            case DockSplitNode split:
                return FindGroup(split.First, content) ?? FindGroup(split.Second, content);
            default:
                return null;
        }
    }

    private DockSplitNode? ParentOf(DockNode child) => ParentOf(_root, child);

    private static DockSplitNode? ParentOf(DockNode? node, DockNode child)
    {
        if (node is not DockSplitNode split)
            return null;
        if (ReferenceEquals(split.First, child) || ReferenceEquals(split.Second, child))
            return split;

        return ParentOf(split.First, child) ?? ParentOf(split.Second, child);
    }

    private void ReplaceNode(DockNode oldNode, DockNode newNode)
    {
        if (this.ParentOf(oldNode) is { } parent)
        {
            if (ReferenceEquals(parent.First, oldNode))
                parent.First = newNode;
            else
                parent.Second = newNode;
        }
        else if (ReferenceEquals(_root, oldNode))
            _root = newNode;
    }

    private bool NodeInTree(DockNode node) => ContainsNode(_root, node);

    private static bool ContainsNode(DockNode? node, DockNode target)
        => node is not null && (ReferenceEquals(node, target)
            || (node is DockSplitNode split && (ContainsNode(split.First, target) || ContainsNode(split.Second, target))));

    private DockContent? FirstTreeContent()
    {
        DockContent? found = null;
        WalkGroups(_root, g =>
        {
            if (found is null && g.Active is { } active)
                found = active;
        });
        return found;
    }

    private static void WalkGroups(DockNode? node, Action<DockTabGroupNode> visit)
    {
        switch (node)
        {
            case DockTabGroupNode group:
                visit(group);
                break;
            case DockSplitNode split:
                WalkGroups(split.First, visit);
                WalkGroups(split.Second, visit);
                break;
        }
    }

    // --- Floating ---------------------------------------------------------------------------------

    private void MoveToFloat(DockContent content)
    {
        var origin = this.PeerReady ? this.SafeScreenBounds(content) : new Rectangle(80, 80, 320, 240);
        var window = new DockFloatWindow(this, content) { Bounds = origin };
        (_floatWindows ??= []).Add(window);
        window.ShowFloating();
    }

    private void MoveFromFloat(DockContent content)
    {
        if (_floatWindows is null)
            return;

        for (var i = 0; i < _floatWindows.Count; ++i)
        {
            if (!ReferenceEquals(_floatWindows[i].Content, content))
                continue;

            var window = _floatWindows[i];
            _floatWindows.RemoveAt(i);
            window.ReclaimContent();
            window.CloseFloating();
            break;
        }
    }

    /// <summary>Called by a float window when the user closes it: re-dock the pane to the well.</summary>
    internal void OnFloatWindowClosed(DockFloatWindow window)
    {
        _floatWindows?.Remove(window);
        var content = window.Content;
        if (_contents is null || !_contents.Contains(content) || content.DockState != DockState.Floating)
            return;

        window.ReclaimContent();
        content.SetStateInternal(DockState.Hidden);
        this.SetContentState(content, DockState.Document);
    }

    /// <summary>The number of floating windows the manager currently owns (for tests and the demo).</summary>
    internal int FloatingWindowCount => _floatWindows?.Count ?? 0;

    private bool PeerReady => this.Peer is not null;

    /// <summary>Whether a pane's content peer is currently shown (active tab or flown out) — for tests.</summary>
    internal bool IsContentShown(DockContent content) => this.GetChildPeerVisible(content);

    /// <summary>The pane currently flown out of its auto-hide strip, or <see langword="null"/> — for tests.</summary>
    internal DockContent? FlyoutContent => _flyout;

    /// <summary>The group a pane lives in, or <see langword="null"/> — for tests.</summary>
    internal DockTabGroupNode? GroupOf(DockContent content) => this.FindGroup(content);

    private Rectangle SafeScreenBounds(DockContent content)
    {
        try
        {
            var origin = this.PointToScreen(new Point(Math.Max(0, this.Width / 4), Math.Max(0, this.Height / 4)));
            var size = content.Bounds.Size;
            if (size.Width < 200 || size.Height < 150)
                size = new Size(320, 240);
            return new Rectangle(origin, size);
        }
        catch (InvalidOperationException)
        {
            return new Rectangle(80, 80, 320, 240);
        }
    }

    // --- Layout -----------------------------------------------------------------------------------

    /// <summary>Requests a chrome repaint (caption/tabs/splitters/edge strips).</summary>
    internal void InvalidateChrome() => this.Invalidate();

    private void CommitLayout()
    {
        this.PerformLayout();
        this.PushPeerVisibleTree();
        this.Invalidate();
    }

    /// <inheritdoc/>
    private protected override void OnLayout()
    {
        var inner = this.InnerArea();
        if (_root is not null)
            this.LayoutNode(_root, inner);

        if (_flyout is { } fly)
            fly.Bounds = this.FlyoutBounds(fly, inner);

        this.Invalidate();
    }

    /// <summary>The client area with the auto-hide edge strips reserved out.</summary>
    private Rectangle InnerArea()
    {
        var rect = new Rectangle(0, 0, this.Width, this.Height);
        if (_autoHide is null || _autoHide.Count == 0)
            return rect;

        var t = this.AutoHideThickness;
        if (this.HasAutoHide(DockEdge.Left)) { rect.X += t; rect.Width -= t; }
        if (this.HasAutoHide(DockEdge.Right)) rect.Width -= t;
        if (this.HasAutoHide(DockEdge.Top)) { rect.Y += t; rect.Height -= t; }
        if (this.HasAutoHide(DockEdge.Bottom)) rect.Height -= t;
        rect.Width = Math.Max(0, rect.Width);
        rect.Height = Math.Max(0, rect.Height);
        return rect;
    }

    private bool HasAutoHide(DockEdge edge)
    {
        if (_autoHide is null)
            return false;
        for (var i = 0; i < _autoHide.Count; ++i)
            if (_autoHide[i].DockEdge == edge)
                return true;
        return false;
    }

    private void LayoutNode(DockNode node, Rectangle bounds)
    {
        node.Bounds = bounds;
        switch (node)
        {
            case DockSplitNode split:
                this.LayoutSplit(split, bounds);
                break;
            case DockTabGroupNode group:
                this.LayoutGroup(group, bounds);
                break;
        }
    }

    private void LayoutSplit(DockSplitNode split, Rectangle bounds)
    {
        if (split.Orientation == Orientation.Vertical)
        {
            var avail = Math.Max(0, bounds.Width - SplitterThickness);
            var first = Math.Clamp((int)Math.Round(avail * split.Ratio), 0, avail);
            split.First.Bounds = new Rectangle(bounds.X, bounds.Y, first, bounds.Height);
            split.Splitter = new Rectangle(bounds.X + first, bounds.Y, SplitterThickness, bounds.Height);
            var secondX = bounds.X + first + SplitterThickness;
            this.LayoutNode(split.First, split.First.Bounds);
            this.LayoutNode(split.Second, new Rectangle(secondX, bounds.Y, Math.Max(0, bounds.Right - secondX), bounds.Height));
        }
        else
        {
            var avail = Math.Max(0, bounds.Height - SplitterThickness);
            var first = Math.Clamp((int)Math.Round(avail * split.Ratio), 0, avail);
            split.First.Bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, first);
            split.Splitter = new Rectangle(bounds.X, bounds.Y + first, bounds.Width, SplitterThickness);
            var secondY = bounds.Y + first + SplitterThickness;
            this.LayoutNode(split.First, split.First.Bounds);
            this.LayoutNode(split.Second, new Rectangle(bounds.X, secondY, bounds.Width, Math.Max(0, bounds.Bottom - secondY)));
        }
    }

    private void LayoutGroup(DockTabGroupNode group, Rectangle bounds)
    {
        var caption = this.CaptionHeight;
        group.CaptionBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Min(caption, bounds.Height));
        var tabStrip = group.Contents.Count > 1 ? Math.Min(this.TabStripHeight, Math.Max(0, bounds.Height - caption)) : 0;
        group.TabStripBounds = tabStrip > 0
            ? new Rectangle(bounds.X, bounds.Bottom - tabStrip, bounds.Width, tabStrip)
            : Rectangle.Empty;
        group.ContentBounds = new Rectangle(
            bounds.X,
            bounds.Y + group.CaptionBounds.Height,
            bounds.Width,
            Math.Max(0, bounds.Height - group.CaptionBounds.Height - tabStrip));

        if (group.Active is { } active)
            active.Bounds = group.ContentBounds;
    }

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        // The theme metrics are known only now, so redo the layout the fallback metrics produced.
        this.PerformLayout();
    }

    /// <inheritdoc/>
    private protected override bool GetChildPeerVisible(Control child)
    {
        if (child is not DockContent content)
            return child.IsVisibleLocal;
        if (!content.IsVisibleLocal)
            return false;

        return content.DockState switch
        {
            DockState.Document or DockState.Docked => this.FindGroup(content) is { } g && ReferenceEquals(g.Active, content),
            DockState.AutoHide => ReferenceEquals(_flyout, content),
            _ => false,
        };
    }

    /// <inheritdoc/>
    private protected override void OnChildAdded(Control child)
    {
        // Panes join through Add(); a stray control dropped straight into Controls is adopted hidden so
        // it never floats loose over the chrome.
        if (child is DockContent content && (_contents is null || !_contents.Contains(content)))
        {
            content.DockPanel = this;
            (_contents ??= []).Add(content);
            content.SetStateInternal(DockState.Hidden);
        }

        base.OnChildAdded(child);
    }

    // --- Persistence ------------------------------------------------------------------------------

    /// <summary>Serialises the whole arrangement — the split tree, every pane's state, edge, active
    /// flag and floating-window bounds — to a compact, reflection-free string.</summary>
    public string SaveLayout() => DockLayoutSerializer.Save(this);

    /// <summary>Restores an arrangement previously produced by <see cref="SaveLayout"/>.
    /// <paramref name="resolve"/> maps a pane's persistence key back to the live
    /// <see cref="DockContent"/>; a key it cannot resolve is skipped.</summary>
    public void LoadLayout(string layout, Func<string, DockContent?> resolve)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(resolve);
        DockLayoutSerializer.Load(this, layout, resolve);
    }

    // --- Internal accessors for the serializer ----------------------------------------------------

    internal DockNode? RootNode => _root;
    internal IReadOnlyList<DockContent> AllContents => (IReadOnlyList<DockContent>?)_contents ?? [];
    internal IReadOnlyList<DockContent> AutoHideContents => (IReadOnlyList<DockContent>?)_autoHide ?? [];

    /// <summary>Clears every pane and tree node so a layout can be rebuilt from scratch by the loader.</summary>
    internal void ResetForLoad()
    {
        if (_floatWindows is not null)
            foreach (var window in _floatWindows.ToArray())
                this.MoveFromFloat(window.Content);

        _root = null;
        _documentGroup = null;
        for (var i = 0; i < _edgeGroups.Length; ++i)
            _edgeGroups[i] = null;
        _autoHide?.Clear();
        _flyout = null;
        _active = null;
    }

    /// <summary>The floating panes and their window bounds, for the serializer.</summary>
    internal IReadOnlyList<(DockContent Content, Rectangle Bounds)> FloatingEntries()
    {
        if (_floatWindows is null || _floatWindows.Count == 0)
            return [];
        var list = new List<(DockContent, Rectangle)>(_floatWindows.Count);
        for (var i = 0; i < _floatWindows.Count; ++i)
            list.Add((_floatWindows[i].Content, _floatWindows[i].Bounds));
        return list;
    }

    /// <summary>Ensures the loader's resolved pane is owned by this panel and parked hidden before it is
    /// woven into the restored tree.</summary>
    internal void EnsureOwnedForLoad(DockContent content)
    {
        if (_contents is null || !_contents.Contains(content))
        {
            content.DockPanel = this;
            (_contents ??= []).Add(content);
        }

        if (!ReferenceEquals(content.Parent, this))
            this.Controls.Add(content);

        content.SetStateInternal(DockState.Hidden);
    }

    /// <summary>Adopts a tree the serializer rebuilt directly, deriving each pane's docked/document
    /// state from its group.</summary>
    internal void ApplyLoadedTree(DockNode? root)
    {
        _root = root;
        _documentGroup = null;
        for (var i = 0; i < _edgeGroups.Length; ++i)
            _edgeGroups[i] = null;

        WalkGroups(root, group =>
        {
            group.ClampActive();
            if (group.IsDocument)
                _documentGroup ??= group;

            for (var i = 0; i < group.Contents.Count; ++i)
                group.Contents[i].SetStateInternal(group.IsDocument ? DockState.Document : DockState.Docked);
        });
    }

    /// <summary>Adds a pane to the auto-hide strip during a load.</summary>
    internal void AddAutoHideLoaded(DockContent content, DockEdge edge)
    {
        content.SetEdgeInternal(edge);
        this.RaiseToTop(content);
        (_autoHide ??= []).Add(content);
        content.SetStateInternal(DockState.AutoHide);
    }

    /// <summary>Floats a pane at the given window bounds during a load.</summary>
    internal void AddFloatingLoaded(DockContent content, Rectangle bounds)
    {
        var window = new DockFloatWindow(this, content) { Bounds = bounds };
        (_floatWindows ??= []).Add(window);
        window.ShowFloating();
        content.SetStateInternal(DockState.Floating);
    }

    internal void FinishLoad()
    {
        if (_active is null)
            this.SetActive(this.FirstTreeContent());
        this.CommitLayout();
    }
}
