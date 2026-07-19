using System.Drawing;
using Hawkynt.NativeForms.Backends;

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
    private IControlPeer? _peer;
    private IPlatformBackend? _backend;

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
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushPeerBounds();
            this.OnBoundsChanged();
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

    /// <summary>The containing control, or <see langword="null"/> for a top-level form.</summary>
    public Control? Parent { get; internal set; }

    /// <summary>The child controls hosted by this control.</summary>
    public ControlCollection Controls { get; }

    /// <summary>Raised when the control is activated by the user.</summary>
    public event EventHandler? Click;

    /// <summary>Raised after <see cref="Text"/> changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raises <see cref="Click"/>.</summary>
    protected virtual void OnClick(EventArgs e) => this.Click?.Invoke(this, e);

    /// <summary>Raises <see cref="TextChanged"/>.</summary>
    protected virtual void OnTextChanged(EventArgs e) => this.TextChanged?.Invoke(this, e);

    /// <summary>Programmatically triggers the <see cref="Click"/> event.</summary>
    public void PerformClick() => this.OnClick(EventArgs.Empty);

    /// <summary>
    /// Maps a point from this control's client space to screen coordinates. Only the native widget
    /// knows where it sits on screen, so the control must be realized first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The control has not been realized yet.</exception>
    public Point PointToScreen(Point clientPoint)
        => _peer is not null
            ? _peer.PointToScreen(clientPoint)
            : throw new InvalidOperationException("The control must be realized before client coordinates can be mapped to the screen.");

    /// <summary>The realized native peer, or <see langword="null"/> before realization.</summary>
    internal IControlPeer? Peer => _peer;

    /// <summary>The backend this control is realized on, or <see langword="null"/> before realization.</summary>
    internal IPlatformBackend? Backend => _backend;

    /// <summary>Creates the backend peer specific to this control kind (button, label, window …).</summary>
    private protected abstract IControlPeer CreatePeer(IPlatformBackend backend);

    /// <summary>Hook for subclasses to wire native events once the peer exists.</summary>
    private protected virtual void OnRealized(IControlPeer peer) { }

    /// <summary>Hook for subclasses to drop their typed peer references when the peer tree is torn down.</summary>
    private protected virtual void OnUnrealized() { }

    /// <summary>Hook for subclasses that lay out children whenever their own bounds change.</summary>
    private protected virtual void OnBoundsChanged() { }

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
        this.PushPeerBounds();
        peer.SetText(this.Text);
        peer.SetEnabled(this.Enabled);
        this.PushPeerVisible();
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

        _peer.Dispose();
        _peer = null;
        _backend = null;
        this.OnUnrealized();
    }
}
