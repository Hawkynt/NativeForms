using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for every visual element. The API mirrors <c>System.Windows.Forms.Control</c> closely
/// enough that porting is mostly a namespace swap: absolute <see cref="Bounds"/> in parent-client
/// pixels, a <see cref="Controls"/> child collection, and familiar <see cref="Click"/>/
/// <see cref="TextChanged"/> events.
/// </summary>
/// <remarks>
/// A control carries its state in managed fields until it is <em>realized</em> against a backend,
/// at which point it acquires an <see cref="IControlPeer"/>. Property writes made before realization
/// are buffered by the peer and flushed when the native widget is created; writes made afterwards
/// are forwarded to the widget immediately.
/// </remarks>
public abstract class Control
{
    /// <summary>Packed flag state, kept in one word so focus and layout cost no per-flag fields.</summary>
    [Flags]
    private enum State : ushort
    {
        /// <summary><see cref="TabStop"/> was assigned explicitly and overrides the per-kind default.</summary>
        TabStopAssigned = 1,

        /// <summary>The explicitly assigned <see cref="TabStop"/> value.</summary>
        TabStop = 2,

        /// <summary>The peer currently holds keyboard focus.</summary>
        Focused = 4,

        /// <summary><see cref="Anchor"/> was assigned explicitly and overrides the Top|Left default.</summary>
        AnchorAssigned = 8,

        /// <summary>Bits 4–7: the assigned <see cref="AnchorStyles"/> flags, shifted by <see cref="_AnchorShift"/>.</summary>
        AnchorBits = 0xF << _AnchorShift,

        /// <summary>Bits 8–10: the <see cref="DockStyle"/> value, shifted by <see cref="_DockShift"/>.</summary>
        DockBits = 0x7 << _DockShift,

        /// <summary>A layout pass is running; re-entrant <see cref="PerformLayout"/> calls return at once.</summary>
        LayoutInProgress = 1 << 11,

        /// <summary>A layout was requested while suspended and runs on the closing <see cref="ResumeLayout()"/>.</summary>
        LayoutPending = 1 << 12,

        /// <summary>A light-dismiss surface owned by this control is on screen holding the grab.</summary>
        PopupOpen = 1 << 13,
    }

    /// <summary>The bit position of the packed <see cref="AnchorStyles"/> flags inside <see cref="_state"/>.</summary>
    private const int _AnchorShift = 4;

    /// <summary>The bit position of the packed <see cref="DockStyle"/> value inside <see cref="_state"/>.</summary>
    private const int _DockShift = 8;

    private IControlPeer? _peer;
    private IPlatformBackend? _backend;
    private Rectangle _bounds;
    private int _tabIndex;
    private State _state;
    private byte _layoutSuspend;
    private bool _visible = true;
    private bool _enabled = true;
    private Rectangle _layoutBounds;
    private AppearanceState? _appearance;
    private ControlCollection? _controls;

    /// <summary>
    /// The rarely-set appearance slots, allocated on the first explicit set so the thousands of
    /// controls that never touch them pay a single null reference — the footprint rule from
    /// <c>docs/PRD.md</c> §4. A <see langword="null"/> slot means "unset": the property getters then
    /// fall back to the ambient value inherited from the <see cref="Parent"/> chain and finally to
    /// the theme, exactly like the Windows Forms ambient properties.
    /// </summary>
    private sealed class AppearanceState
    {
        /// <summary>The explicitly set font, or <see langword="null"/> for ambient.</summary>
        public Font? Font;

        /// <summary>The explicitly set text color; <see cref="Color.Empty"/> for ambient.</summary>
        public Color ForeColor;

        /// <summary>The explicitly set background color; <see cref="Color.Empty"/> for ambient.</summary>
        public Color BackColor;

        /// <summary>The interior spacing between the control's edges and its content.</summary>
        public Padding Padding;

        /// <summary>The explicitly set pointer shape, or <see langword="null"/> for ambient.</summary>
        public Cursor? Cursor;
    }

    /// <summary>The caption text: a button label, a form's title bar, a label's text.</summary>
    public virtual string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _peer?.SetText(value);
            this.OnTextChanged(EventArgs.Empty);
        }
    } = string.Empty;

    /// <summary>Position and size relative to the parent's client area, in pixels.</summary>
    public Rectangle Bounds
    {
        get => _bounds;
        set
        {
            if (_bounds == value)
                return;

            _bounds = value;
            this.PushPeerBounds();
            this.OnBoundsChanged();
            this.Parent?.OnChildLayoutChanged(this);
        }
    }

    /// <summary>The top-left corner of <see cref="Bounds"/>.</summary>
    public Point Location
    {
        get => this.Bounds.Location;
        set => this.Bounds = new(value, this.Bounds.Size);
    }

    /// <summary>The size of <see cref="Bounds"/>.</summary>
    public Size Size
    {
        get => this.Bounds.Size;
        set => this.Bounds = new(this.Bounds.Location, value);
    }

    /// <summary>The x-coordinate of the left edge.</summary>
    public int Left
    {
        get => this.Bounds.X;
        set => this.Location = new(value, this.Bounds.Y);
    }

    /// <summary>The y-coordinate of the top edge.</summary>
    public int Top
    {
        get => this.Bounds.Y;
        set => this.Location = new(this.Bounds.X, value);
    }

    /// <summary>The width in pixels.</summary>
    public int Width
    {
        get => this.Bounds.Width;
        set => this.Size = new(value, this.Bounds.Height);
    }

    /// <summary>The height in pixels.</summary>
    public int Height
    {
        get => this.Bounds.Height;
        set => this.Size = new(this.Bounds.Width, value);
    }

    /// <summary>
    /// Whether the widget is shown. The getter is <em>effective</em>, exactly like Windows Forms: a
    /// control inside a hidden ancestor reports <see langword="false"/> even while its own flag is
    /// still set. A container that hides its content wholesale without touching the children's flags
    /// — a collapsed <see cref="Expander"/>, a collapsed <see cref="SplitContainer"/> panel — counts
    /// as such an ancestor: its veto (<see cref="GetChildPeerVisible"/>) is what the peer obeys, so
    /// reporting anything else here would contradict the pixels. The setter only writes this
    /// control's own flag; the peer receives the local value — native widget nesting already hides
    /// children with their parent.
    /// </summary>
    public bool Visible
    {
        get
        {
            for (var control = this; control is not null; control = control.Parent)
            {
                if (!control._visible)
                    return false;

                // The veto is asked per level and combines with the child's *own* flag, never with
                // this effective getter — so consulting it here cannot recurse.
                if (control.Parent is { } parent && !parent.GetChildPeerVisible(control))
                    return false;
            }

            return true;
        }
        set
        {
            if (_visible == value)
                return;

            _visible = value;

            // The whole subtree, not just this peer: a container between here and a descendant may
            // veto that descendant's visibility (a collapsed Expander), and showing an ancestor has
            // to give every such container its say again.
            this.PushPeerVisibleTree();
        }
    }

    /// <summary>
    /// Whether the widget accepts user interaction. The getter is <em>effective</em>, exactly like
    /// Windows Forms: a control inside a disabled ancestor reports <see langword="false"/> even
    /// while its own flag is still set. The setter only writes this control's own flag; the peer
    /// receives the local value — native widgets grey out with their disabled parent already.
    /// </summary>
    public bool Enabled
    {
        get
        {
            for (var control = this; control is not null; control = control.Parent)
                if (!control._enabled)
                    return false;

            return true;
        }
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            _peer?.SetEnabled(value);
        }
    }

    /// <summary>This control's own <see cref="Visible"/> flag, ignoring the ancestor chain — the
    /// value peers are fed and container vetoes combine with.</summary>
    internal bool IsVisibleLocal => _visible;

    /// <summary>Arbitrary caller-owned data attached to the control, exactly like Windows Forms —
    /// the toolkit never reads it.</summary>
    public object? Tag { get; set; }

    /// <summary>The programmatic name of the control — designer-style lookup data, never rendered.
    /// Defaults to the empty string; assigning <see langword="null"/> resets to it.</summary>
    public string Name
    {
        get => field;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>
    /// Requests a full repaint. Owner-drawn controls forward to their canvas surface; controls
    /// backed by a native widget repaint themselves through the platform and treat this as a no-op —
    /// <see cref="IControlPeer"/> exposes no invalidation seam.
    /// </summary>
    public virtual void Invalidate() { }

    /// <summary>Requests a repaint of a client-space sub-region (see <see cref="Invalidate()"/>).</summary>
    public virtual void Invalidate(Rectangle region) { }

    /// <summary>
    /// Invalidates the whole control. Unlike Windows Forms the repaint is not forced synchronously —
    /// it arrives with the platform's next paint cycle, which every backend schedules promptly.
    /// </summary>
    public void Refresh() => this.Invalidate();

    /// <summary>
    /// The spacing layout containers (<see cref="FlowLayoutPanel"/>, <see cref="TableLayoutPanel"/>)
    /// keep around this control. Plain containers position children by <see cref="Bounds"/> alone
    /// and ignore it.
    /// </summary>
    public Padding Margin
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Parent?.OnChildLayoutChanged(this);
        }
    }

    /// <summary>
    /// The container edges this control is bound to, <see cref="AnchorStyles.Top"/> |
    /// <see cref="AnchorStyles.Left"/> by default. When the parent resizes, every anchored edge
    /// keeps its distance to the matching edge of the parent's <see cref="DisplayRectangle"/>:
    /// opposing anchors stretch the control, a single anchor translates it, and
    /// <see cref="AnchorStyles.None"/> drifts by half the delta. Assigning an anchor resets
    /// <see cref="Dock"/> to <see cref="DockStyle.None"/> — the two are mutually exclusive and the
    /// property assigned last wins, exactly like Windows Forms.
    /// </summary>
    public AnchorStyles Anchor
    {
        get => (_state & State.AnchorAssigned) != 0
            ? (AnchorStyles)((int)(_state & State.AnchorBits) >> _AnchorShift)
            : AnchorStyles.Top | AnchorStyles.Left;
        set
        {
            if (this.Dock == DockStyle.None && (_state & State.AnchorAssigned) != 0 && this.Anchor == value)
                return;

            _state = _state & ~State.AnchorBits & ~State.DockBits
                | State.AnchorAssigned
                | (State)(((int)value << _AnchorShift) & (int)State.AnchorBits);
            this.Parent?.PerformLayout();
        }
    }

    /// <summary>
    /// The parent edge this control glues itself to, <see cref="DockStyle.None"/> by default.
    /// Docked siblings claim their edges of the parent's <see cref="DisplayRectangle"/> in reverse
    /// <see cref="Controls"/> order — the last-added sibling docks first — each shrinking the
    /// rectangle left for the next; <see cref="DockStyle.Fill"/> takes whatever remains. Assigning
    /// a dock resets
    /// <see cref="Anchor"/> to its default — the property assigned last wins, exactly like
    /// Windows Forms.
    /// </summary>
    public DockStyle Dock
    {
        get => (DockStyle)((int)(_state & State.DockBits) >> _DockShift);
        set
        {
            if (this.Dock == value)
                return;

            var state = _state & ~State.DockBits | (State)(((int)value << _DockShift) & (int)State.DockBits);
            if (value != DockStyle.None)
                state &= ~State.AnchorBits & ~State.AnchorAssigned;

            _state = state;
            this.Parent?.PerformLayout();
        }
    }

    /// <summary>Whether <see cref="Anchor"/> was assigned explicitly (layout containers with their
    /// own placement rules, like the table's in-cell arrangement, only honor explicit anchors).</summary>
    internal bool IsAnchorAssigned => (_state & State.AnchorAssigned) != 0;

    /// <summary>
    /// The position of this control in its container's tab order. Siblings are visited in ascending
    /// <see cref="TabIndex"/> (ties keep the <see cref="Controls"/> insertion order), depth-first
    /// through nested containers — the Windows Forms traversal. Defaults to 0.
    /// </summary>
    public int TabIndex
    {
        get => _tabIndex;
        set => _tabIndex = value;
    }

    /// <summary>
    /// Whether Tab stops on this control. Until assigned it follows the per-kind default: focusable
    /// controls are tab stops, static and container kinds (labels, panels, group boxes, picture
    /// boxes, progress bars, scroll bars, strips) are not, and the menu bar opts out because Alt —
    /// not Tab — reaches it, matching Windows Forms.
    /// </summary>
    public bool TabStop
    {
        get => (_state & State.TabStopAssigned) != 0 ? (_state & State.TabStop) != 0 : this.DefaultTabStop;
        set => _state = (_state | State.TabStopAssigned) & ~State.TabStop | (value ? State.TabStop : 0);
    }

    /// <summary>Whether the peer currently holds keyboard focus, tracked from its focus events.</summary>
    public bool Focused => (_state & State.Focused) != 0;

    /// <summary>
    /// Whether <see cref="Focus"/> would succeed right now: the control kind takes focus at all, the
    /// control is visible and enabled, and a native peer exists to receive it.
    /// </summary>
    public bool CanFocus => _peer is not null && this.Focusable && this.Visible && this.Enabled;

    /// <summary>
    /// Whether this kind of control can take keyboard focus at all. Interactive native widgets
    /// (buttons, text boxes) can; static kinds (<see cref="Label"/>) and non-interactive owner-drawn
    /// surfaces override to <see langword="false"/>.
    /// </summary>
    protected virtual bool Focusable => true;

    /// <summary>Whether Tab stops here until <see cref="TabStop"/> is assigned. Follows <see cref="Focusable"/>.</summary>
    private protected virtual bool DefaultTabStop => this.Focusable;

    /// <summary>
    /// The control that actually takes the keyboard on this one's behalf. Plain controls answer with
    /// themselves; a composite that hosts a native editor inside an owner-drawn surface answers with
    /// that editor, because the editor — not the painted shell — is the widget that takes text.
    /// </summary>
    private protected virtual Control FocusTarget => this;

    /// <summary>
    /// Moves keyboard focus to this control by asking the peer (<c>SetFocus</c> on Win32,
    /// <c>gtk_widget_grab_focus</c> on GTK). A no-op while <see cref="CanFocus"/> is
    /// <see langword="false"/>; <see cref="Focused"/> flips when the platform reports the change.
    /// On a composite the focus lands on its <see cref="FocusTarget"/>.
    /// </summary>
    public void Focus()
    {
        var target = this.FocusTarget;
        if (target.CanFocus)
            target._peer!.Focus();
    }

    /// <summary>The form this control sits on — itself for a form — or <see langword="null"/> while unparented.</summary>
    public Form? FindForm()
    {
        for (Control? control = this; control is not null; control = control.Parent)
            if (control is Form form)
                return form;

        return null;
    }

    /// <summary>
    /// Whether the control claims <paramref name="keyData"/> for its own input, exempting the key
    /// from the owning form's dialog handling (Tab navigation, Enter → <see cref="Form.AcceptButton"/>,
    /// Escape → <see cref="Form.CancelButton"/>). The base claims nothing; controls that consume
    /// Enter or Escape themselves — an open drop-down, a grid edit — override this.
    /// </summary>
    protected virtual bool IsInputKey(Keys keyData) => false;

    /// <summary>Routes the form's dialog-key chain to the protected <see cref="IsInputKey"/> hook.</summary>
    internal bool WantsInputKey(Keys keyData) => this.IsInputKey(keyData);

    /// <summary>Whether this control is an ancestor of <paramref name="descendant"/>.</summary>
    internal bool IsAncestorOf(Control? descendant)
    {
        for (var control = descendant?.Parent; control is not null; control = control.Parent)
            if (ReferenceEquals(control, this))
                return true;

        return false;
    }

    /// <summary>
    /// Whether this control accepts in-process drag-and-drop: only opted-in controls are considered
    /// by the hit-test a drag performs, and only they raise <see cref="DragEnter"/>/
    /// <see cref="DragOver"/>/<see cref="DragLeave"/>/<see cref="DragDrop"/>.
    /// </summary>
    public bool AllowDrop { get; set; }

    /// <summary>
    /// The text direction, <see cref="NativeForms.RightToLeft.Inherit"/> by default: the effective
    /// value comes from the nearest ancestor with an explicit setting, falling back to
    /// <see cref="NativeForms.RightToLeft.No"/>. When it resolves to
    /// <see cref="NativeForms.RightToLeft.Yes"/> owner-drawn controls mirror their glyph/text
    /// painting and a container mirrors where its children physically sit — the logical
    /// <see cref="Bounds"/> stay left-to-right, only the peer placement flips across the client width.
    /// </summary>
    public RightToLeft RightToLeft
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyRightToLeftChanged();
        }
    } = RightToLeft.Inherit;

    /// <summary>Fans a direction change out to this control and every descendant that inherits it.</summary>
    private void NotifyRightToLeftChanged()
    {
        this.OnRightToLeftChanged(EventArgs.Empty);
        if (_controls is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
        {
            // Re-place every child under this container's (now flipped) mirroring — the child's own
            // direction only governs its descendants, not where this container puts it.
            children[i].PushPeerBounds();
            if (children[i].RightToLeft == RightToLeft.Inherit)
                children[i].NotifyRightToLeftChanged();
        }
    }

    /// <summary>The resolved text direction: walks the <see cref="Parent"/> chain past
    /// <see cref="NativeForms.RightToLeft.Inherit"/> values, defaulting to left-to-right.</summary>
    internal bool IsRightToLeft
    {
        get
        {
            for (var control = this; control is not null; control = control.Parent)
                if (control.RightToLeft != RightToLeft.Inherit)
                    return control.RightToLeft == RightToLeft.Yes;

            return false;
        }
    }

    /// <summary>
    /// The font the control's text renders in — an ambient property: unset controls inherit the
    /// nearest ancestor's font and finally the theme's default, exactly like Windows Forms.
    /// <see cref="ResetFont"/> returns an explicitly set control to the ambient value.
    /// </summary>
    public Font Font
    {
        get => this.GetAmbientFont() ?? this.AmbientTheme.DefaultFont;
        set
        {
            if (_appearance?.Font == value)
                return;

            (_appearance ??= new()).Font = value;
            this.PushAmbientFont(value);
        }
    }

    /// <summary>Clears an explicitly set <see cref="Font"/> so the control inherits again.</summary>
    public void ResetFont()
    {
        if (_appearance is not { Font: not null } state)
            return;

        state.Font = null;
        this.PushAmbientFont(this.Font);
    }

    /// <summary>
    /// The text color — ambient like <see cref="Font"/>: unset controls inherit from the parent
    /// chain, then the theme. <see cref="ResetForeColor"/> returns to the ambient value.
    /// </summary>
    public Color ForeColor
    {
        get
        {
            var color = this.GetAmbientForeColor();
            return color.IsEmpty ? this.FallbackForeColor : color;
        }
        set
        {
            if ((_appearance?.ForeColor ?? Color.Empty) == value)
                return;

            (_appearance ??= new()).ForeColor = value;
            this.PushAmbientColors();
        }
    }

    /// <summary>Clears an explicitly set <see cref="ForeColor"/> so the control inherits again.</summary>
    public void ResetForeColor()
    {
        if (_appearance is not { } state || state.ForeColor.IsEmpty)
            return;

        state.ForeColor = Color.Empty;
        this.PushAmbientColors();
    }

    /// <summary>
    /// The background color — ambient like <see cref="Font"/>: unset controls inherit from the
    /// parent chain, then the theme. <see cref="ResetBackColor"/> returns to the ambient value.
    /// </summary>
    public Color BackColor
    {
        get
        {
            var color = this.GetAmbientBackColor();
            return color.IsEmpty ? this.FallbackBackColor : color;
        }
        set
        {
            if ((_appearance?.BackColor ?? Color.Empty) == value)
                return;

            (_appearance ??= new()).BackColor = value;
            this.PushAmbientColors();
        }
    }

    /// <summary>Clears an explicitly set <see cref="BackColor"/> so the control inherits again.</summary>
    public void ResetBackColor()
    {
        if (_appearance is not { } state || state.BackColor.IsEmpty)
            return;

        state.BackColor = Color.Empty;
        this.PushAmbientColors();
    }

    /// <summary>
    /// The interior spacing between the control's edges and its content, in pixels per side. Not
    /// ambient (each control owns its padding); owner-drawn controls honor it through
    /// <see cref="DisplayRectangle"/> and their content layout.
    /// </summary>
    public Padding Padding
    {
        get => _appearance?.Padding ?? default;
        set
        {
            if ((_appearance?.Padding ?? default) == value)
                return;

            (_appearance ??= new()).Padding = value;
            this.OnAppearanceChanged();
            this.PerformLayout(); // the DisplayRectangle changed, so docked/anchored children move
        }
    }

    /// <summary>
    /// The pointer shape shown over the control — ambient like <see cref="Font"/>: unset controls
    /// inherit from the parent chain, then <see cref="Cursors.Arrow"/>.
    /// <see cref="ResetCursor"/> returns to the ambient value.
    /// </summary>
    public Cursor Cursor
    {
        get => this.GetAmbientCursor() ?? Cursors.Arrow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_appearance?.Cursor, value))
                return;

            (_appearance ??= new()).Cursor = value;
            this.PushAmbientCursor(value);
        }
    }

    /// <summary>Clears an explicitly set <see cref="Cursor"/> so the control inherits again.</summary>
    public void ResetCursor()
    {
        if (_appearance is not { Cursor: not null } state)
            return;

        state.Cursor = null;
        this.PushAmbientCursor(this.Cursor);
    }

    /// <summary>
    /// The client rectangle available to content: the full client area deflated by
    /// <see cref="Padding"/>. Containers with chrome of their own (a <see cref="GroupBox"/> frame)
    /// deflate further.
    /// </summary>
    public virtual Rectangle DisplayRectangle
    {
        get
        {
            var padding = this.Padding;
            return new(
                padding.Left,
                padding.Top,
                Math.Max(0, this.Width - padding.Horizontal),
                Math.Max(0, this.Height - padding.Vertical));
        }
    }

    /// <summary>The containing control, or <see langword="null"/> for a top-level form.</summary>
    public Control? Parent { get; internal set; }

    /// <summary>
    /// The context menu a right-click on this control opens at the cursor, or <see langword="null"/>
    /// for none. Owner-drawn controls open it from their mouse pipeline; native-widget controls open
    /// it from their peer's <see cref="Backends.IControlPeer.ContextMenuRequested"/>.
    /// </summary>
    public ContextMenuStrip? ContextMenuStrip { get; set; }

    /// <summary>The child controls hosted by this control. Created on first access, so the many leaf
    /// controls that never host children pay no collection allocation — internal traversals go
    /// through <see cref="ChildrenOrNull"/> and skip the empty case.</summary>
    public ControlCollection Controls => _controls ??= new(this);

    /// <summary>The child collection when one exists, or <see langword="null"/> for a control no
    /// child was ever added to — the allocation-free traversal seam.</summary>
    internal ControlCollection? ChildrenOrNull => _controls;

    /// <summary>Raised when the control is activated by the user.</summary>
    public event EventHandler? Click;

    /// <summary>Raised after <see cref="Text"/> changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised after the control gains keyboard focus, following <see cref="Enter"/>.</summary>
    public event EventHandler? GotFocus;

    /// <summary>Raised when the control loses keyboard focus, before <see cref="Leave"/>.</summary>
    public event EventHandler? LostFocus;

    /// <summary>
    /// Raised when focus enters the control — and, on the containers along the way, when focus
    /// enters their subtree from outside. On the focused control itself it precedes
    /// <see cref="GotFocus"/>, the Windows Forms order.
    /// </summary>
    public event EventHandler? Enter;

    /// <summary>
    /// Raised when focus leaves the control — and, on containers, when focus leaves their subtree
    /// entirely. On the control itself it follows <see cref="LostFocus"/>.
    /// </summary>
    public event EventHandler? Leave;

    /// <summary>Raised when an in-process drag first moves over this control (see <see cref="AllowDrop"/>).</summary>
    public event EventHandler<DragEventArgs>? DragEnter;

    /// <summary>Raised while an in-process drag keeps moving over this control.</summary>
    public event EventHandler<DragEventArgs>? DragOver;

    /// <summary>Raised when an in-process drag moves off this control or ends refused.</summary>
    public event EventHandler? DragLeave;

    /// <summary>Raised when an in-process drag is dropped on this control with an accepted effect.</summary>
    public event EventHandler<DragEventArgs>? DragDrop;

    /// <summary>Raises <see cref="Click"/>.</summary>
    protected virtual void OnClick(EventArgs e) => this.Click?.Invoke(this, e);

    /// <summary>Raises <see cref="TextChanged"/>.</summary>
    protected virtual void OnTextChanged(EventArgs e) => this.TextChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="GotFocus"/>.</summary>
    protected virtual void OnGotFocus(EventArgs e) => this.GotFocus?.Invoke(this, e);

    /// <summary>Raises <see cref="LostFocus"/>.</summary>
    protected virtual void OnLostFocus(EventArgs e) => this.LostFocus?.Invoke(this, e);

    /// <summary>Raises <see cref="Enter"/>.</summary>
    protected virtual void OnEnter(EventArgs e) => this.Enter?.Invoke(this, e);

    /// <summary>Raises <see cref="Leave"/>.</summary>
    protected virtual void OnLeave(EventArgs e) => this.Leave?.Invoke(this, e);

    /// <summary>Raises <see cref="Enter"/> on a container crossed by a focus change (called by the form).</summary>
    internal void RaiseEnter() => this.OnEnter(EventArgs.Empty);

    /// <summary>Raises <see cref="Leave"/> on a container abandoned by a focus change (called by the form).</summary>
    internal void RaiseLeave() => this.OnLeave(EventArgs.Empty);

    /// <summary>
    /// Adopts a focus gain the peer reports: marks the control focused, lets the owning form update
    /// <see cref="Form.ActiveControl"/> and walk Enter/Leave across the container chain, then raises
    /// <see cref="Enter"/> followed by <see cref="GotFocus"/>.
    /// </summary>
    private void OnPeerGotFocus(object? sender, EventArgs e)
    {
        if ((_state & State.Focused) != 0)
            return;

        _state |= State.Focused;
        this.FindForm()?.NotifyFocusGained(this);
        this.OnEnter(EventArgs.Empty);
        this.OnGotFocus(EventArgs.Empty);
    }

    /// <summary>
    /// Adopts a focus loss the peer reports: clears the flag and raises <see cref="LostFocus"/>
    /// followed by <see cref="Leave"/>. Deliberately not guarded by <see cref="Focused"/> — some
    /// platforms report a loss for a widget that never announced its gain, and controls (spin boxes
    /// committing an edit) rely on hearing it.
    ///
    /// It is guarded by <see cref="OwnsOpenPopup"/> though: a light-dismiss surface takes a grab, and
    /// toolkits report that grab as the owning widget losing focus even though the user never moved
    /// focus anywhere. Adopting it would have the field tear down the very surface it just opened, so
    /// while the surface is up the loss is treated as the toolkit's own bookkeeping and dropped; the
    /// grab's outside-press dismissal is what ends the interaction.
    /// </summary>
    private void OnPeerLostFocus(object? sender, EventArgs e)
    {
        if (this.OwnsOpenPopup)
            return;

        _state &= ~State.Focused;
        this.OnLostFocus(EventArgs.Empty);
        this.OnLeave(EventArgs.Empty);
    }

    /// <summary>
    /// Opens this control's <see cref="ContextMenuStrip"/> where a native peer reports a right-click
    /// or Menu-key request, and marks the request handled so the peer suppresses the widget's own
    /// default menu. Owner-drawn controls open the same menu from their canvas mouse pipeline, so this
    /// path serves only the native-widget controls whose peers raise the request.
    /// </summary>
    private void OnPeerContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (this.ContextMenuStrip is not { } menu)
            return;

        menu.Show(this, e.Location);
        e.Handled = true;
    }

    /// <summary>
    /// Gives up focus the control can no longer hold because it was hidden or disabled and nothing
    /// else took it — the form's last resort in <see cref="Form.ReconcileActiveControlVisibility"/>.
    /// Guarded by the flag so it is a no-op when the platform already reported the loss (a real
    /// backend fires focus-out as the widget unmaps), which keeps the crossing events single.
    /// </summary>
    internal void AbandonFocus()
    {
        if ((_state & State.Focused) == 0)
            return;

        _state &= ~State.Focused;
        this.OnLostFocus(EventArgs.Empty);
        this.OnLeave(EventArgs.Empty);
    }

    /// <summary>
    /// Whether this control currently owns a shown <see cref="Backends.IPopupPeer"/>. Every popup
    /// owner sets it across the surface's lifetime — from just before <c>ShowAt</c> until the surface
    /// is hidden or light-dismissed — which is what keeps the grab's spurious focus loss from closing
    /// it. Packed into the existing state bits, so it costs no per-instance footprint.
    /// </summary>
    private protected bool OwnsOpenPopup
    {
        get => (_state & State.PopupOpen) != 0;
        set => _state = value ? _state | State.PopupOpen : _state & ~State.PopupOpen;
    }

    /// <summary>Hook for subclasses when <see cref="RightToLeft"/> changes; owner-drawn controls repaint.</summary>
    protected virtual void OnRightToLeftChanged(EventArgs e) { }

    /// <summary>Raises <see cref="DragEnter"/>.</summary>
    protected virtual void OnDragEnter(DragEventArgs e) => this.DragEnter?.Invoke(this, e);

    /// <summary>Raises <see cref="DragOver"/>.</summary>
    protected virtual void OnDragOver(DragEventArgs e) => this.DragOver?.Invoke(this, e);

    /// <summary>Raises <see cref="DragLeave"/>.</summary>
    protected virtual void OnDragLeave(EventArgs e) => this.DragLeave?.Invoke(this, e);

    /// <summary>Raises <see cref="DragDrop"/>.</summary>
    protected virtual void OnDragDrop(DragEventArgs e) => this.DragDrop?.Invoke(this, e);

    /// <summary>Routes the drag engine's enter notification into <see cref="OnDragEnter"/>.</summary>
    internal void RaiseDragEnter(DragEventArgs e) => this.OnDragEnter(e);

    /// <summary>Routes the drag engine's over notification into <see cref="OnDragOver"/>.</summary>
    internal void RaiseDragOver(DragEventArgs e) => this.OnDragOver(e);

    /// <summary>Routes the drag engine's leave notification into <see cref="OnDragLeave"/>.</summary>
    internal void RaiseDragLeave() => this.OnDragLeave(EventArgs.Empty);

    /// <summary>Routes the drag engine's drop notification into <see cref="OnDragDrop"/>.</summary>
    internal void RaiseDragDrop(DragEventArgs e) => this.OnDragDrop(e);

    /// <summary>
    /// Starts an in-process drag with this control as the source. The call returns immediately —
    /// unlike WinForms there is no nested message loop — and the drag then follows the source's
    /// captured mouse stream: targets with <see cref="AllowDrop"/> receive <see cref="DragEnter"/>/
    /// <see cref="DragOver"/>/<see cref="DragLeave"/>, and releasing the button raises
    /// <see cref="DragDrop"/> on the target that accepted an effect. The source must be a realized
    /// <see cref="OwnerDrawnControl"/> (only those own a mouse stream); in-process only — OS-level
    /// drag sources and drop targets are tracked in <c>docs/PRD.md</c> §8.
    /// </summary>
    public void DoDragDrop(object data, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(data);
        DragDropSession.Begin(this, data, allowedEffects);
    }

    /// <summary>Programmatically triggers the <see cref="Click"/> event — a no-op while the control
    /// is not effectively <see cref="Enabled"/> and <see cref="Visible"/>, the Windows Forms
    /// contract a disabled dialog button relies on.</summary>
    public void PerformClick()
    {
        if (this.Enabled && this.Visible)
            this.OnClick(EventArgs.Empty);
    }

    /// <summary>
    /// Whether the caller must marshal through <see cref="Invoke"/>/<see cref="BeginInvoke"/> to
    /// touch this control: <see langword="true"/> only while a message loop is running on another
    /// thread. <see langword="false"/> outside <see cref="Application.Run(Form)"/>, matching the
    /// WinForms convention for a control without a created handle.
    /// </summary>
    public bool InvokeRequired => Application.InvokeRequired;

    /// <summary>
    /// Executes <paramref name="action"/> on the UI thread and blocks until it completed. On the UI
    /// thread (or with no loop running) it runs inline; from any other thread it is queued onto the
    /// loop and awaited. Exceptions propagate to the caller either way.
    /// </summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!this.InvokeRequired)
        {
            action();
            return;
        }

        NativeFormsSynchronizationContext.SendBlocking(this.ResolveInvokeBackend(), action);
    }

    /// <summary>
    /// Queues <paramref name="action"/> for execution on the UI thread and returns immediately.
    /// Callable from any thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No message loop is running and the control is not realized, so there is nothing to queue onto.
    /// </exception>
    public void BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        this.ResolveInvokeBackend().Post(action);
    }

    /// <summary>The backend to marshal through: the realized one, else the running application's.</summary>
    private IPlatformBackend ResolveInvokeBackend()
        => _backend ?? Application.Current ?? throw new InvalidOperationException(
            "Control.Invoke/BeginInvoke need a message loop — call them while Application.Run is active or after the control is realized.");

    /// <summary>
    /// Maps a point from this control's client space to screen coordinates. Only the native widget
    /// knows where it sits on screen, so the control must be realized first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The control has not been realized yet.</exception>
    public Point PointToScreen(Point clientPoint)
        => _peer is not null
            ? _peer.PointToScreen(clientPoint)
            : throw new InvalidOperationException("The control must be realized before client coordinates can be mapped to the screen.");

    /// <summary>
    /// Scales a logical (96-DPI) pixel length to device pixels using the backend's current DPI
    /// scale, rounding to the nearest pixel. Identity before realization — an unrealized control
    /// has no monitor to scale for.
    /// </summary>
    public int LogicalToDevice(int value)
        => _backend is { } backend ? (int)Math.Round(value * backend.GetDpiScale()) : value;

    /// <summary>Scales a logical size to device pixels, component-wise.</summary>
    public Size LogicalToDevice(Size size) => new(this.LogicalToDevice(size.Width), this.LogicalToDevice(size.Height));

    /// <summary>The realized native peer, or <see langword="null"/> before realization.</summary>
    internal IControlPeer? Peer => _peer;

    /// <summary>Whether the control has a live peer. Containers that lay their children out on add can
    /// skip that pass while unrealized — construction ends with one layout on <c>OnRealized</c>, so a
    /// per-add pass during a bulk build would only be thrown-away quadratic work.</summary>
    private protected bool IsRealized => _peer is not null;

    /// <summary>The backend this control is realized on, or <see langword="null"/> before realization.</summary>
    internal IPlatformBackend? Backend => _backend;

    /// <summary>
    /// The realized window peer of the form this control sits on, or <see langword="null"/> while it
    /// is unparented or its form is not realized yet. This is the owner every popup this control puts
    /// up belongs to — see <see cref="Backends.IPlatformBackend.CreatePopup"/>.
    /// </summary>
    internal IWindowPeer? OwnerWindowPeer => this.FindForm()?.WindowPeer;

    /// <summary>
    /// The peer's pointer events, relayed to core listeners. Lazily allocated, so a control nothing
    /// ever hovers pays a single null reference rather than two delegate slots — the same shape the
    /// appearance state uses.
    /// </summary>
    private sealed class PointerRelay
    {
        public EventHandler<MouseEventArgs>? Move;
        public EventHandler? Leave;
        public EventHandler? Enter;
        public EventHandler<MouseEventArgs>? Down;
        public EventHandler<MouseEventArgs>? Up;
        public EventHandler<MouseEventArgs>? Wheel;
        public EventHandler<MouseEventArgs>? DoubleClickArgs;
        public EventHandler? DoubleClick;
        public bool Hooked;
        public bool Inside;
        public long LastPressTicks;
        public Point LastPressLocation;
    }

    /// <summary>The double-click window in milliseconds and the pixel slop — a second left press
    /// within both of the previous one is a double-click, mirroring the classic control.</summary>
    private const int _DoubleClickMs = 500;
    private const int _DoubleClickSlop = 4;

    private PointerRelay? _pointer;

    /// <summary>Raised when the pointer enters this control's bounds (the first move after a leave).</summary>
    public event EventHandler? MouseEnter
    {
        add { (_pointer ??= new()).Enter += value; this.HookPeerPointer(); }
        remove { if (_pointer is { } relay) relay.Enter -= value; }
    }

    /// <summary>Raised while the pointer moves over this control — native widgets and owner-drawn surfaces alike.</summary>
    public event EventHandler<MouseEventArgs>? MouseMove
    {
        add { (_pointer ??= new()).Move += value; this.HookPeerPointer(); }
        remove { if (_pointer is { } relay) relay.Move -= value; }
    }

    /// <summary>Raised when the pointer leaves this control — the counterpart of <see cref="MouseEnter"/>.</summary>
    public event EventHandler? MouseLeave
    {
        add { (_pointer ??= new()).Leave += value; this.HookPeerPointer(); }
        remove { if (_pointer is { } relay) relay.Leave -= value; }
    }

    /// <summary>
    /// Raised when a mouse button goes down over this control. Delivered for owner-drawn controls
    /// (buttons, lists, custom surfaces); native widgets consume their own presses, so the button
    /// press does not surface for them — the same platform limit documented for native key preview.
    /// </summary>
    public event EventHandler<MouseEventArgs>? MouseDown
    {
        add => (_pointer ??= new()).Down += value;
        remove { if (_pointer is { } relay) relay.Down -= value; }
    }

    /// <summary>Raised when a mouse button is released over this control (owner-drawn controls).</summary>
    public event EventHandler<MouseEventArgs>? MouseUp
    {
        add => (_pointer ??= new()).Up += value;
        remove { if (_pointer is { } relay) relay.Up -= value; }
    }

    /// <summary>Raised when the mouse wheel turns over this control (owner-drawn controls).</summary>
    public event EventHandler<MouseEventArgs>? MouseWheel
    {
        add => (_pointer ??= new()).Wheel += value;
        remove { if (_pointer is { } relay) relay.Wheel -= value; }
    }

    /// <summary>Raised on a double-click over this control, carrying the pointer location (owner-drawn controls).</summary>
    public event EventHandler<MouseEventArgs>? MouseDoubleClick
    {
        add => (_pointer ??= new()).DoubleClickArgs += value;
        remove { if (_pointer is { } relay) relay.DoubleClickArgs -= value; }
    }

    /// <summary>Raised on a double-click over this control (owner-drawn controls).</summary>
    public event EventHandler? DoubleClick
    {
        add => (_pointer ??= new()).DoubleClick += value;
        remove { if (_pointer is { } relay) relay.DoubleClick -= value; }
    }

    /// <summary>
    /// Raises <see cref="MouseDown"/> from the owner-drawn input path and folds in double-click
    /// recognition — the timing state rides the lazy relay, so a control nobody subscribed to keeps
    /// its per-instance footprint. A second left press close in time and place to the first raises
    /// <see cref="MouseDoubleClick"/>/<see cref="DoubleClick"/>.
    /// </summary>
    internal void RaiseMouseDown(MouseEventArgs e)
    {
        if (_pointer is not { } relay)
            return;

        relay.Down?.Invoke(this, e);
        if (e.Button != MouseButtons.Left)
            return;

        var now = Environment.TickCount64;
        if (now - relay.LastPressTicks <= _DoubleClickMs
            && Math.Abs(e.X - relay.LastPressLocation.X) <= _DoubleClickSlop
            && Math.Abs(e.Y - relay.LastPressLocation.Y) <= _DoubleClickSlop)
        {
            relay.LastPressTicks = 0; // consumed; a third press begins a fresh pair
            this.RaiseMouseDoubleClick(e);
            return;
        }

        relay.LastPressTicks = now;
        relay.LastPressLocation = e.Location;
    }

    /// <summary>Raises <see cref="MouseUp"/> from the owner-drawn input path.</summary>
    internal void RaiseMouseUp(MouseEventArgs e) => _pointer?.Up?.Invoke(this, e);

    /// <summary>Raises <see cref="MouseWheel"/> from the owner-drawn input path.</summary>
    internal void RaiseMouseWheel(MouseEventArgs e) => _pointer?.Wheel?.Invoke(this, e);

    /// <summary>Raises <see cref="MouseDoubleClick"/> and <see cref="DoubleClick"/> from the owner-drawn input path.</summary>
    internal void RaiseMouseDoubleClick(MouseEventArgs e)
    {
        _pointer?.DoubleClickArgs?.Invoke(this, e);
        _pointer?.DoubleClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised while the pointer moves over this control, wherever the control is realized — the
    /// peer's own hover channel, so this works for native widgets and owner-drawn surfaces alike.
    /// Subscribing before realization is fine: the peer is hooked as soon as it exists.
    /// </summary>
    internal event EventHandler<MouseEventArgs>? PointerMove
    {
        add
        {
            (_pointer ??= new()).Move += value;
            this.HookPeerPointer();
        }

        remove
        {
            if (_pointer is { } relay)
                relay.Move -= value;
        }
    }

    /// <summary>Raised when the pointer leaves this control — the counterpart of <see cref="PointerMove"/>.</summary>
    internal event EventHandler? PointerLeave
    {
        add
        {
            (_pointer ??= new()).Leave += value;
            this.HookPeerPointer();
        }

        remove
        {
            if (_pointer is { } relay)
                relay.Leave -= value;
        }
    }

    /// <summary>Subscribes to the peer's pointer events once, as soon as both a listener and a peer exist.</summary>
    private void HookPeerPointer()
    {
        if (_pointer is not { Hooked: false } relay || _peer is not { } peer)
            return;

        relay.Hooked = true;
        peer.PointerMove += this.OnPeerPointerMove;
        peer.PointerLeave += this.OnPeerPointerLeave;
    }

    private void OnPeerPointerMove(object? sender, MouseEventArgs e)
    {
        if (_pointer is not { } relay)
            return;

        if (!relay.Inside)
        {
            relay.Inside = true;
            relay.Enter?.Invoke(this, EventArgs.Empty);
        }

        relay.Move?.Invoke(this, e);
    }

    private void OnPeerPointerLeave(object? sender, EventArgs e)
    {
        if (_pointer is not { } relay)
            return;

        relay.Inside = false;
        relay.Leave?.Invoke(this, e);
    }

    /// <summary>Creates the backend peer specific to this control kind (button, label, window …).</summary>
    private protected abstract IControlPeer CreatePeer(IPlatformBackend backend);

    /// <summary>The theme the ambient appearance defaults resolve against; the neutral fallback until realized.</summary>
    private ITheme AmbientTheme => _backend?.Theme ?? DefaultTheme.Instance;

    /// <summary>The theme text color an unset <see cref="ForeColor"/> resolves to.</summary>
    private protected virtual Color FallbackForeColor => this.AmbientTheme.ControlText;

    /// <summary>The theme surface color an unset <see cref="BackColor"/> resolves to. Field-like
    /// controls (lists, grids) override with the theme's field background, like Windows Forms
    /// per-control default colors.</summary>
    private protected virtual Color FallbackBackColor => this.AmbientTheme.ControlBackground;

    /// <summary>The nearest explicitly set font up the parent chain, or <see langword="null"/>.</summary>
    private Font? GetAmbientFont()
    {
        for (var control = this; control is not null; control = control.Parent)
            if (control._appearance?.Font is { } font)
                return font;

        return null;
    }

    /// <summary>The nearest explicitly set text color up the parent chain, or <see cref="Color.Empty"/>.</summary>
    private Color GetAmbientForeColor()
    {
        for (var control = this; control is not null; control = control.Parent)
            if (control._appearance is { } state && !state.ForeColor.IsEmpty)
                return state.ForeColor;

        return Color.Empty;
    }

    /// <summary>The nearest explicitly set background color up the parent chain, or <see cref="Color.Empty"/>.</summary>
    private Color GetAmbientBackColor()
    {
        for (var control = this; control is not null; control = control.Parent)
            if (control._appearance is { } state && !state.BackColor.IsEmpty)
                return state.BackColor;

        return Color.Empty;
    }

    /// <summary>The nearest explicitly set cursor up the parent chain, or <see langword="null"/>.</summary>
    private Cursor? GetAmbientCursor()
    {
        for (var control = this; control is not null; control = control.Parent)
            if (control._appearance?.Cursor is { } cursor)
                return cursor;

        return null;
    }

    /// <summary>
    /// Forwards a font change to this control's peer and to every descendant that inherits it
    /// (children with their own font keep theirs, cutting off that subtree's inheritance).
    /// </summary>
    private void PushAmbientFont(Font font)
    {
        _peer?.SetFont(font);
        this.OnAppearanceChanged();
        if (_controls is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            if (child._appearance?.Font is null)
                child.PushAmbientFont(font);
        }
    }

    /// <summary>
    /// Forwards a color change down the tree, re-resolving the ambient pair per control so a child
    /// with only one own color still combines it with the inherited other.
    /// </summary>
    private void PushAmbientColors()
    {
        _peer?.SetColors(this.GetAmbientForeColor(), this.GetAmbientBackColor());
        this.OnAppearanceChanged();
        if (_controls is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
            children[i].PushAmbientColors();
    }

    /// <summary>
    /// Pushes a cursor for a sub-region of this control's own surface, bypassing the ambient
    /// <see cref="Cursor"/> bookkeeping: <see cref="Cursor"/> itself keeps its value and nothing is
    /// forwarded to children, so a control that owns several hot zones — the splitter band inside a
    /// <see cref="SplitContainer"/> — can swap the shape as the pointer crosses them. Passing
    /// <see langword="null"/> restores the ambient cursor. Allocation-free, so it is safe to call
    /// from a mouse-move handler; callers are expected to filter out no-op transitions themselves.
    /// </summary>
    private protected void SetRegionCursor(Cursor? cursor) => _peer?.SetCursor(cursor ?? this.Cursor);

    /// <summary>
    /// Forwards a cursor change to this control's peer and to every descendant that inherits it.
    /// </summary>
    private void PushAmbientCursor(Cursor cursor)
    {
        _peer?.SetCursor(cursor);
        if (_controls is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            if (child._appearance?.Cursor is null)
                child.PushAmbientCursor(cursor);
        }
    }

    /// <summary>
    /// Hook for subclasses that render their own appearance: the effective font, colors or padding
    /// changed (on this control directly or inherited from an ancestor). Owner-drawn controls
    /// repaint here.
    /// </summary>
    private protected virtual void OnAppearanceChanged() { }

    /// <summary>Hook for subclasses to wire native events once the peer exists.</summary>
    private protected virtual void OnRealized(IControlPeer peer) { }

    /// <summary>Hook for subclasses to drop their typed peer references when the peer tree is torn down.</summary>
    private protected virtual void OnUnrealized() { }

    /// <summary>Hook for subclasses that lay out children whenever their own bounds change. The
    /// base schedules a layout pass, so plain containers reflow their Anchor/Dock children.</summary>
    private protected virtual void OnBoundsChanged() => this.PerformLayout();

    /// <summary>
    /// Hook for layout containers: a child joined <see cref="Controls"/>. The base lays out only when
    /// the new child <see cref="Dock"/>s — a docked child claims edge space and shifts its siblings, so
    /// the whole container must re-flow. An absolutely-placed or merely anchored child sits at its own
    /// bounds and moves nobody, so no layout is due; skipping it keeps building a form of N children
    /// linear instead of the quadratic a full pass per add would cost. Anchoring still applies on the
    /// next resize, and a scrolling panel re-derives its content extent on the next paint.
    /// </summary>
    private protected virtual void OnChildAdded(Control child)
    {
        if (child.Dock != DockStyle.None)
            this.PerformLayout();
    }

    /// <summary>Hook for layout containers: a child left <see cref="Controls"/>. The base
    /// schedules a layout pass so the remaining docked children reclaim its edge.</summary>
    private protected virtual void OnChildRemoved(Control child) => this.PerformLayout();

    /// <summary>
    /// Hook for layout containers: a child's <see cref="Bounds"/> or <see cref="Margin"/> changed —
    /// including by the container's own layout pass; <see cref="PerformLayout"/> swallows that
    /// re-entry, so overrides that call it (the base does) need no guard of their own.
    /// </summary>
    private protected virtual void OnChildLayoutChanged(Control child) => this.PerformLayout();

    /// <summary>
    /// Suspends the layout engine on this container until a matching <see cref="ResumeLayout()"/>,
    /// so bulk changes (adding many children, resizing several of them) coalesce into one pass.
    /// Calls nest; each needs its own resume.
    /// </summary>
    public void SuspendLayout()
    {
        if (_layoutSuspend < byte.MaxValue)
            ++_layoutSuspend;
    }

    /// <summary>Resumes the layout engine and runs the pass the suspension held back, if any.</summary>
    public void ResumeLayout() => this.ResumeLayout(performLayout: true);

    /// <summary>
    /// Resumes the layout engine after a <see cref="SuspendLayout"/>. With
    /// <paramref name="performLayout"/> the held-back pass runs now; without it the request is
    /// dropped and the children keep their current bounds until the next layout trigger or an
    /// explicit <see cref="PerformLayout"/>.
    /// </summary>
    public void ResumeLayout(bool performLayout)
    {
        if (_layoutSuspend > 0)
            --_layoutSuspend;

        if (_layoutSuspend > 0)
            return;

        var pending = (_state & State.LayoutPending) != 0;
        _state &= ~State.LayoutPending;
        if (performLayout && pending)
            this.PerformLayout();
    }

    /// <summary>
    /// Runs this container's layout pass — the Anchor/Dock engine, or the specialized layout of a
    /// flow, table, tab or splitter container. Deferred while suspended, swallowed while a pass is
    /// already running (the bounds writes of a pass re-enter here through the child hooks).
    /// </summary>
    public void PerformLayout()
    {
        if (_layoutSuspend > 0)
        {
            _state |= State.LayoutPending;
            return;
        }

        if ((_state & State.LayoutInProgress) != 0)
            return;

        _state |= State.LayoutInProgress;
        try
        {
            this.OnLayout();
        }
        finally
        {
            _state &= ~State.LayoutInProgress;
        }
    }

    /// <summary>
    /// The layout pass. The base is the Windows Forms default engine: docked children claim edges
    /// of the <see cref="DisplayRectangle"/> in <em>reverse</em> <see cref="Controls"/> order — the
    /// last-added child docks first, so designer-style <c>Add(fill); Add(toolbar); Add(menu)</c>
    /// stacks the menu topmost, exactly like Windows Forms — with <see cref="DockStyle.Fill"/>
    /// children taking the final remainder. Then every undocked child repositions per its
    /// <see cref="Anchor"/> against how each display-rectangle edge moved since the previous pass —
    /// anchored edges hold their distance, opposing anchors stretch, unanchored axes drift by half.
    /// The engine keeps one rectangle per container (the display rectangle it last laid out) and
    /// derives every child's anchor distance from its current bounds, instead of the per-child
    /// offset cache Windows Forms carries; the standard flows behave identically. Containers that
    /// own their children's bounds (flow, table, tabs, splitter) override this wholesale and
    /// thereby ignore Anchor/Dock, exactly like their WinForms counterparts.
    /// </summary>
    private protected virtual void OnLayout()
    {
        var display = this.DisplayRectangle;
        var previous = _layoutBounds;
        _layoutBounds = display;
        if (_controls is not { Count: > 0 } children)
            return;

        var count = children.Count;
        var remaining = display;
        var fills = false;
        for (var i = count - 1; i >= 0; --i)
        {
            var child = children[i];
            var size = child.Bounds.Size;
            switch (child.Dock)
            {
                case DockStyle.Top:
                    child.Bounds = new(remaining.X, remaining.Y, remaining.Width, size.Height);
                    remaining.Y += size.Height;
                    remaining.Height = Math.Max(0, remaining.Height - size.Height);
                    break;
                case DockStyle.Bottom:
                    child.Bounds = new(remaining.X, remaining.Bottom - size.Height, remaining.Width, size.Height);
                    remaining.Height = Math.Max(0, remaining.Height - size.Height);
                    break;
                case DockStyle.Left:
                    child.Bounds = new(remaining.X, remaining.Y, size.Width, remaining.Height);
                    remaining.X += size.Width;
                    remaining.Width = Math.Max(0, remaining.Width - size.Width);
                    break;
                case DockStyle.Right:
                    child.Bounds = new(remaining.Right - size.Width, remaining.Y, size.Width, remaining.Height);
                    remaining.Width = Math.Max(0, remaining.Width - size.Width);
                    break;
                case DockStyle.Fill:
                    fills = true;
                    break;
                case DockStyle.None:
                default:
                    break;
            }
        }

        if (fills)
            for (var i = 0; i < count; ++i)
            {
                var child = children[i];
                if (child.Dock == DockStyle.Fill)
                    child.Bounds = remaining;
            }

        // Per-edge deltas, so an inset shift (padding, a group box growing its caption) moves only
        // the children anchored to the edge that actually moved.
        var deltaLeft = display.X - previous.X;
        var deltaRight = display.Right - previous.Right;
        var deltaTop = display.Y - previous.Y;
        var deltaBottom = display.Bottom - previous.Bottom;
        if (previous == default || (deltaLeft == 0 && deltaRight == 0 && deltaTop == 0 && deltaBottom == 0))
            return;

        for (var i = 0; i < count; ++i)
        {
            var child = children[i];
            if (child.Dock != DockStyle.None)
                continue;

            var anchor = child.Anchor;
            var bounds = child.Bounds;
            var x = bounds.X;
            var width = bounds.Width;
            if ((anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right))
            {
                x += deltaLeft;
                width = Math.Max(0, width + deltaRight - deltaLeft);
            }
            else if ((anchor & AnchorStyles.Right) != 0)
                x += deltaRight;
            else if ((anchor & AnchorStyles.Left) != 0)
                x += deltaLeft;
            else
                x += (deltaLeft + deltaRight) / 2;

            var y = bounds.Y;
            var height = bounds.Height;
            if ((anchor & (AnchorStyles.Top | AnchorStyles.Bottom)) == (AnchorStyles.Top | AnchorStyles.Bottom))
            {
                y += deltaTop;
                height = Math.Max(0, height + deltaBottom - deltaTop);
            }
            else if ((anchor & AnchorStyles.Bottom) != 0)
                y += deltaBottom;
            else if ((anchor & AnchorStyles.Top) != 0)
                y += deltaTop;
            else
                y += (deltaTop + deltaBottom) / 2;

            child.Bounds = new(x, y, width, height);
        }
    }

    /// <summary>Routes <see cref="ControlCollection.Add"/> to the <see cref="OnChildAdded"/> hook.</summary>
    internal void NotifyChildAdded(Control child) => this.OnChildAdded(child);

    /// <summary>Routes <see cref="ControlCollection.Remove"/> to the <see cref="OnChildRemoved"/> hook.</summary>
    internal void NotifyChildRemoved(Control child) => this.OnChildRemoved(child);

    /// <summary>
    /// Maps a child's logical <see cref="Bounds"/> to the rectangle its peer occupies inside this
    /// container. The default is the identity; scrolling containers shift the result by their scroll
    /// offset so native children physically move while the child's logical bounds stay put.
    /// </summary>
    private protected virtual Rectangle GetChildPeerBounds(Control child) => child.Bounds;

    /// <summary>
    /// Whether a child's peer should currently be shown. The default honors the child's own local
    /// <see cref="Visible"/> flag (native nesting hides children with a hidden ancestor already);
    /// containers that hide their content wholesale (a collapsed expander) veto it without
    /// clobbering the child's logical visibility.
    /// </summary>
    private protected virtual bool GetChildPeerVisible(Control child) => child._visible;

    /// <summary>
    /// Adopts bounds the native peer reports (a user resize or move) without echoing them back into
    /// the widget — the write-back half of the two-way sync, guarded by value comparison exactly
    /// like the <see cref="TextBox"/> text sync.
    /// </summary>
    internal void SetBoundsFromPeer(Rectangle bounds)
    {
        if (_bounds == bounds)
            return;

        _bounds = bounds;
        this.OnBoundsChanged();
    }

    /// <summary>Re-applies this control's effective bounds to its peer through the parent's mapping.</summary>
    internal void PushPeerBounds()
    {
        if (_peer is null)
            return;

        if (this.Parent is not { } parent)
        {
            _peer.SetBounds(this.Bounds);
            return;
        }

        var bounds = parent.GetChildPeerBounds(this);

        // A right-to-left container mirrors where its children physically sit: the logical Bounds stay
        // left-to-right (so app code reads the same coordinates either way), only the peer placement
        // flips across the container's client width — the way Windows Forms mirrors a container's
        // layout under RightToLeft.
        if (parent.IsRightToLeft)
            bounds = parent.MirrorChildHorizontally(bounds);

        _peer.SetBounds(bounds);
    }

    /// <summary>Reflects a child's peer rectangle across this container's client width.</summary>
    private Rectangle MirrorChildHorizontally(Rectangle bounds)
    {
        var display = this.DisplayRectangle;
        return new Rectangle(display.Left + display.Right - bounds.Right, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>Re-applies this control's local visibility to its peer through the parent's veto.</summary>
    internal void PushPeerVisible()
        => _peer?.SetVisible(this.Parent is { } parent ? parent.GetChildPeerVisible(this) : _visible);

    /// <summary>
    /// Re-applies visibility to this control's peer and, depth-first, to every descendant's — each
    /// through its own parent's veto.
    ///
    /// Pushing only the direct children is not enough: a veto is asked per level
    /// (<see cref="GetChildPeerVisible"/>), so a grandchild inside a collapsed
    /// <see cref="Expander"/> — or inside a collapsed <see cref="SplitContainer"/> panel — has an
    /// answer of its own that nothing else recomputes. The native backends hide a widget subtree
    /// along with its parent and so happen to paper over a missed descendant; that is exactly why
    /// the whole subtree has to be re-pushed here rather than relying on it.
    /// </summary>
    internal void PushPeerVisibleTree()
    {
        this.PushPeerVisibleSubtree();

        // A hide may have pulled the mapped widget out from under keyboard focus; let the form move
        // focus to the next tab stop before anyone reads a stranded ActiveControl.
        this.FindForm()?.ReconcileActiveControlVisibility();
    }

    /// <summary>Pushes effective visibility across the whole subtree without reconciling focus — the recursion body.</summary>
    private void PushPeerVisibleSubtree()
    {
        this.PushPeerVisible();
        if (_controls is not { } children)
            return;

        for (var i = 0; i < children.Count; ++i)
            children[i].PushPeerVisibleSubtree();
    }

    /// <summary>
    /// Creates this control's peer and pushes its buffered state into it. When the peer can host
    /// children (<see cref="IContainerPeer"/>), the entire subtree is realized depth-first and each
    /// child peer is parented into this one — any control is a potential parent, exactly like
    /// Windows Forms.
    /// </summary>
    internal IControlPeer RealizeSelf(IPlatformBackend backend)
    {
        var peer = this.CreatePeer(backend);
        _peer = peer;
        _backend = backend;
        peer.GotFocus += this.OnPeerGotFocus;
        peer.LostFocus += this.OnPeerLostFocus;
        peer.ContextMenuRequested += this.OnPeerContextMenuRequested;
        this.PushPeerBounds();
        peer.SetText(this.Text);
        peer.SetEnabled(_enabled);
        this.PushPeerVisible();

        // Appearance is only flushed when set somewhere up the chain, so the overwhelmingly common
        // all-default control leaves the native widget's own font, colors and cursor untouched.
        if (this.GetAmbientFont() is { } font)
            peer.SetFont(font);

        var foreColor = this.GetAmbientForeColor();
        var backColor = this.GetAmbientBackColor();
        if (!foreColor.IsEmpty || !backColor.IsEmpty)
            peer.SetColors(foreColor, backColor);

        if (this.GetAmbientCursor() is { } cursor)
            peer.SetCursor(cursor);

        this.HookPeerPointer();
        this.OnRealized(peer);

        if (peer is IContainerPeer container && _controls is { } children)
            for (var i = 0; i < children.Count; ++i)
                container.AddChild(children[i].RealizeSelf(backend));

        return peer;
    }

    /// <summary>
    /// Realizes a child that was added after this control was already realized as a container. A
    /// no-op before realization — the child is picked up by the normal depth-first walk later.
    /// </summary>
    internal void RealizeAddedChild(Control child)
    {
        if (_peer is not IContainerPeer container || _backend is null)
            return;

        container.AddChild(child.RealizeSelf(_backend));
    }

    /// <summary>
    /// Tells this container's peer to forget a child it hosts, before the child's peer tree is
    /// disposed. A no-op when unrealized or when the child has no peer to drop.
    /// </summary>
    internal void UnrealizeChildPeer(Control child)
    {
        if (_peer is IContainerPeer container && child._peer is { } childPeer)
            container.RemoveChild(childPeer);
    }

    /// <summary>
    /// Disposes this control's peer and every descendant peer, children first. The managed state
    /// (text, bounds, children …) stays intact, so the control is back to its unrealized shape and
    /// can be realized again — for example after being re-added to a live container.
    /// </summary>
    internal void DisposePeerTree()
    {
        if (_controls is { } children)
            for (var i = 0; i < children.Count; ++i)
                children[i].DisposePeerTree();

        if (_peer is null)
            return;

        _peer.GotFocus -= this.OnPeerGotFocus;
        _peer.LostFocus -= this.OnPeerLostFocus;
        _peer.ContextMenuRequested -= this.OnPeerContextMenuRequested;

        // The popup bit goes with the peer: unrealizing tears every owned surface down, and leaving it
        // set would have the control swallow every focus loss it is ever told about again.
        _state &= ~(State.Focused | State.PopupOpen);
        _peer.Dispose();
        _peer = null;
        _backend = null;
        this.OnUnrealized();
    }
}
