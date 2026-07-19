using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A system-tray icon, the moral equivalent of <c>System.Windows.Forms.NotifyIcon</c>. Icon pixels
/// and hover text are buffered in managed state until the icon first becomes <see cref="Visible"/>
/// while an application loop is running; from then on changes forward straight to the shell. The
/// icon is set decoder-free from raw ARGB pixels, matching the <see cref="ImageList"/> pipeline.
/// </summary>
/// <remarks>
/// Backed by <c>Shell_NotifyIconW</c> on Windows. Linux has no supported tray surface in this
/// toolkit yet (<c>GtkStatusIcon</c> is deprecated; StatusNotifier/D-Bus is tracked in
/// <c>docs/PRD.md</c> §7.7), so showing the icon there throws <see cref="NotSupportedException"/>.
/// </remarks>
public sealed class NotifyIcon : IDisposable
{
    private readonly IPlatformBackend? _backend;
    private INotifyIconPeer? _peer;
    private int[]? _iconPixels;
    private int _iconWidth;
    private int _iconHeight;
    private string _text = string.Empty;
    private bool _visible;

    /// <summary>Creates a tray icon bound to whatever backend the application runs on.</summary>
    public NotifyIcon() { }

    /// <summary>Creates a tray icon against an explicit backend. Intended for tests.</summary>
    internal NotifyIcon(IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Raised when the user clicks the icon with the primary button.</summary>
    public event EventHandler? Click;

    /// <summary>Raised when the user double-clicks the icon with the primary button.</summary>
    public event EventHandler? DoubleClick;

    /// <summary>The hover text the shell shows next to the icon.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
                return;

            _text = value;
            _peer?.SetToolTip(value);
        }
    }

    /// <summary>
    /// Whether the icon sits in the tray. The first show creates the native peer and flushes the
    /// buffered icon and text into it; without a running application loop the wish is kept until the
    /// property is touched while one runs.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            if (!value)
            {
                _peer?.SetVisible(false);
                return;
            }

            var peer = _peer ?? this.RealizePeer();
            peer?.SetVisible(true);
        }
    }

    /// <summary>Replaces the icon from 32-bit ARGB pixels (row-major, length = width * height).</summary>
    /// <exception cref="ArgumentException">The pixel count does not match the dimensions.</exception>
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        if (argb.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels ({width}×{height}), got {argb.Length}.", nameof(argb));

        _iconWidth = width;
        _iconHeight = height;
        _iconPixels = argb.ToArray();
        _peer?.SetIcon(width, height, _iconPixels);
    }

    /// <summary>Removes the icon from the tray and releases the native peer.</summary>
    public void Dispose()
    {
        _visible = false;
        var peer = _peer;
        if (peer is null)
            return;

        _peer = null;
        peer.Click -= this.OnPeerClick;
        peer.DoubleClick -= this.OnPeerDoubleClick;
        peer.Dispose();
    }

    /// <summary>Creates the peer against the bound (or running) backend and flushes the buffered state.</summary>
    private INotifyIconPeer? RealizePeer()
    {
        var backend = _backend ?? Application.Current;
        if (backend is null)
            return null;

        var peer = backend.CreateNotifyIcon();
        _peer = peer;
        peer.Click += this.OnPeerClick;
        peer.DoubleClick += this.OnPeerDoubleClick;
        if (_iconPixels is { } pixels)
            peer.SetIcon(_iconWidth, _iconHeight, pixels);

        if (_text.Length > 0)
            peer.SetToolTip(_text);

        return peer;
    }

    /// <summary>Forwards a native primary-button click to <see cref="Click"/>.</summary>
    private void OnPeerClick(object? sender, EventArgs e) => this.Click?.Invoke(this, EventArgs.Empty);

    /// <summary>Forwards a native primary-button double-click to <see cref="DoubleClick"/>.</summary>
    private void OnPeerDoubleClick(object? sender, EventArgs e) => this.DoubleClick?.Invoke(this, EventArgs.Empty);
}
