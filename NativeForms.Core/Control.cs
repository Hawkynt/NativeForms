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
    /// <summary>Packed boolean state, kept in one byte so the focus model costs no per-flag fields.</summary>
    [Flags]
    private enum State : byte
    {
        /// <summary><see cref="TabStop"/> was assigned explicitly and overrides the per-kind default.</summary>
        TabStopAssigned = 1,

        /// <summary>The explicitly assigned <see cref="TabStop"/> value.</summary>
        TabStop = 2,

        /// <summary>The peer currently holds keyboard focus.</summary>
        Focused = 4,
    }

    private IControlPeer? _peer;
    private IPlatformBackend? _backend;
    private Rectangle _bounds;
    private int _tabIndex;
    private State _state;
    private AppearanceState? _appearance;

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

    /// <summary>Initializes the control and its (initially empty) child collection.</summary>
    protected Control() => this.Controls = new(this);

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

    /// <summary>Whether the widget is shown.</summary>
    public bool Visible
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushPeerVisible();
        }
    } = true;

    /// <summary>Whether the widget accepts user interaction.</summary>
    public bool Enabled
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetEnabled(value);
        }
    } = true;

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
    /// Moves keyboard focus to this control by asking the peer (<c>SetFocus</c> on Win32,
    /// <c>gtk_widget_grab_focus</c> on GTK). A no-op while <see cref="CanFocus"/> is
    /// <see langword="false"/>; <see cref="Focused"/> flips when the platform reports the change.
    /// </summary>
    public void Focus()
    {
        if (this.CanFocus)
            _peer!.Focus();
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
    /// <see cref="NativeForms.RightToLeft.No"/>. Owner-drawn controls mirror their glyph/text
    /// painting when it resolves to <see cref="NativeForms.RightToLeft.Yes"/>; containers do not
    /// mirror their child layout yet (tracked in <c>docs/PRD.md</c> §8).
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
        for (var i = 0; i < this.Controls.Count; ++i)
            if (this.Controls[i].RightToLeft == RightToLeft.Inherit)
                this.Controls[i].NotifyRightToLeftChanged();
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
    /// for none. Owner-drawn controls open it from their mouse pipeline; native-widget controls need
    /// right-click peer events first (tracked in <c>docs/PRD.md</c>).
    /// </summary>
    public ContextMenuStrip? ContextMenuStrip { get; set; }

    /// <summary>The child controls hosted by this control.</summary>
    public ControlCollection Controls { get; }

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
    /// </summary>
    private void OnPeerLostFocus(object? sender, EventArgs e)
    {
        _state &= ~State.Focused;
        this.OnLostFocus(EventArgs.Empty);
        this.OnLeave(EventArgs.Empty);
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

    /// <summary>Programmatically triggers the <see cref="Click"/> event.</summary>
    public void PerformClick() => this.OnClick(EventArgs.Empty);

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

    /// <summary>The backend this control is realized on, or <see langword="null"/> before realization.</summary>
    internal IPlatformBackend? Backend => _backend;

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
        for (var i = 0; i < this.Controls.Count; ++i)
        {
            var child = this.Controls[i];
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
        for (var i = 0; i < this.Controls.Count; ++i)
            this.Controls[i].PushAmbientColors();
    }

    /// <summary>
    /// Forwards a cursor change to this control's peer and to every descendant that inherits it.
    /// </summary>
    private void PushAmbientCursor(Cursor cursor)
    {
        _peer?.SetCursor(cursor);
        for (var i = 0; i < this.Controls.Count; ++i)
        {
            var child = this.Controls[i];
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

    /// <summary>Hook for subclasses that lay out children whenever their own bounds change.</summary>
    private protected virtual void OnBoundsChanged() { }

    /// <summary>Hook for layout containers: a child joined <see cref="Controls"/>.</summary>
    private protected virtual void OnChildAdded(Control child) { }

    /// <summary>Hook for layout containers: a child left <see cref="Controls"/>.</summary>
    private protected virtual void OnChildRemoved(Control child) { }

    /// <summary>
    /// Hook for layout containers: a child's <see cref="Bounds"/> or <see cref="Margin"/> changed —
    /// including by the container's own layout pass, so containers guard against re-entry.
    /// </summary>
    private protected virtual void OnChildLayoutChanged(Control child) { }

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
    /// Whether a child's peer should currently be shown. The default honors the child's own
    /// <see cref="Visible"/>; containers that hide their content wholesale (a collapsed expander)
    /// veto it without clobbering the child's logical visibility.
    /// </summary>
    private protected virtual bool GetChildPeerVisible(Control child) => child.Visible;

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
        => _peer?.SetBounds(this.Parent is { } parent ? parent.GetChildPeerBounds(this) : this.Bounds);

    /// <summary>Re-applies this control's effective visibility to its peer through the parent's veto.</summary>
    internal void PushPeerVisible()
        => _peer?.SetVisible(this.Parent is { } parent ? parent.GetChildPeerVisible(this) : this.Visible);

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
        this.PushPeerBounds();
        peer.SetText(this.Text);
        peer.SetEnabled(this.Enabled);
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

        this.OnRealized(peer);

        if (peer is IContainerPeer container)
            for (var i = 0; i < this.Controls.Count; ++i)
                container.AddChild(this.Controls[i].RealizeSelf(backend));

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
    /// Disposes this control's peer and every descendant peer, children first. The managed state
    /// (text, bounds, children …) stays intact, so the control is back to its unrealized shape and
    /// can be realized again — for example after being re-added to a live container.
    /// </summary>
    internal void DisposePeerTree()
    {
        for (var i = 0; i < this.Controls.Count; ++i)
            this.Controls[i].DisposePeerTree();

        if (_peer is null)
            return;

        _peer.GotFocus -= this.OnPeerGotFocus;
        _peer.LostFocus -= this.OnPeerLostFocus;
        _state &= ~State.Focused;
        _peer.Dispose();
        _peer = null;
        _backend = null;
        this.OnUnrealized();
    }
}
