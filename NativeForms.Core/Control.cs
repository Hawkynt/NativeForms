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
    private string _text = string.Empty;
    private Rectangle _bounds;
    private bool _visible = true;
    private bool _enabled = true;
    private IControlPeer? _peer;

    /// <summary>Initializes the control and its (initially empty) child collection.</summary>
    protected Control() => this.Controls = new(this);

    /// <summary>The caption text: a button label, a form's title bar, a label's text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
                return;

            _text = value;
            _peer?.SetText(value);
            this.OnTextChanged(EventArgs.Empty);
        }
    }

    /// <summary>Position and size relative to the parent's client area, in pixels.</summary>
    public Rectangle Bounds
    {
        get => _bounds;
        set
        {
            if (_bounds == value)
                return;

            _bounds = value;
            _peer?.SetBounds(value);
        }
    }

    /// <summary>The top-left corner of <see cref="Bounds"/>.</summary>
    public Point Location
    {
        get => _bounds.Location;
        set => this.Bounds = new(value, _bounds.Size);
    }

    /// <summary>The size of <see cref="Bounds"/>.</summary>
    public Size Size
    {
        get => _bounds.Size;
        set => this.Bounds = new(_bounds.Location, value);
    }

    /// <summary>The x-coordinate of the left edge.</summary>
    public int Left
    {
        get => _bounds.X;
        set => this.Location = new(value, _bounds.Y);
    }

    /// <summary>The y-coordinate of the top edge.</summary>
    public int Top
    {
        get => _bounds.Y;
        set => this.Location = new(_bounds.X, value);
    }

    /// <summary>The width in pixels.</summary>
    public int Width
    {
        get => _bounds.Width;
        set => this.Size = new(value, _bounds.Height);
    }

    /// <summary>The height in pixels.</summary>
    public int Height
    {
        get => _bounds.Height;
        set => this.Size = new(_bounds.Width, value);
    }

    /// <summary>Whether the widget is shown.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
                return;

            _visible = value;
            _peer?.SetVisible(value);
        }
    }

    /// <summary>Whether the widget accepts user interaction.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            _peer?.SetEnabled(value);
        }
    }

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
        peer.SetBounds(_bounds);
        peer.SetText(_text);
        peer.SetEnabled(_enabled);
        peer.SetVisible(_visible);
        this.OnRealized(peer);
        return peer;
    }
}
