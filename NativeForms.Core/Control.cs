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

    /// <summary>Initializes the control and its (initially empty) child collection.</summary>
    protected Control() => this.Controls = new(this);

    /// <summary>The caption text: a button label, a form's title bar, a label's text.</summary>
    public string Text
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
            _peer?.SetBounds(value);
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
            _peer?.SetVisible(value);
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

    /// <summary>The realized native peer, or <see langword="null"/> before realization.</summary>
    internal IControlPeer? Peer => _peer;

    /// <summary>Creates the backend peer specific to this control kind (button, label, window …).</summary>
    private protected abstract IControlPeer CreatePeer(IPlatformBackend backend);

    /// <summary>Hook for subclasses to wire native events once the peer exists.</summary>
    private protected virtual void OnRealized(IControlPeer peer) { }

    /// <summary>
    /// Creates this control's peer and pushes its buffered state into it. Child realization is driven
    /// by the owning window (see <see cref="Form"/>).
    /// </summary>
    internal IControlPeer RealizeSelf(IPlatformBackend backend)
    {
        var peer = this.CreatePeer(backend);
        _peer = peer;
        peer.SetBounds(this.Bounds);
        peer.SetText(this.Text);
        peer.SetEnabled(this.Enabled);
        peer.SetVisible(this.Visible);
        this.OnRealized(peer);
        return peer;
    }
}
