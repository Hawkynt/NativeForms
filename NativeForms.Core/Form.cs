using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A top-level window. Maps to a native window on every platform; its <see cref="Control.Text"/> is
/// the title-bar caption and its <see cref="Control.Controls"/> are laid out in the client area.
/// Shown modelessly through <see cref="Application.Run(Form)"/> or modally through
/// <see cref="ShowDialog"/>.
/// </summary>
/// <remarks>
/// Window-management state — border style, window state, size limits, icon, top-most, opacity — is
/// buffered in managed fields until the form is realized, flushed into the peer when the native
/// window is created, and forwarded live afterwards. Native changes (the user resizing the window or
/// clicking the caption buttons) flow back into <see cref="Control.Bounds"/> and
/// <see cref="WindowState"/> without echoing. MDI (<c>MdiParent</c>/<c>MdiChildren</c>) is a
/// deliberate non-goal of this toolkit: it is a legacy Windows-only shell metaphor with no native
/// counterpart on GTK or Cocoa — use multiple top-level forms or a <see cref="TabControl"/> instead.
/// </remarks>
public class Form : Control
{
    private IWindowPeer? _window;
    private bool _modal;
    private FormWindowState _windowState;
    private Size _lastSize;
    private int[]? _iconPixels;
    private int _iconWidth;
    private int _iconHeight;

    /// <summary>The realized native window peer, or <see langword="null"/> before realization.</summary>
    internal IWindowPeer? WindowPeer => _window;

    /// <summary>Raised after the user closes the window.</summary>
    public event EventHandler? FormClosed;

    /// <summary>Raised whenever the form's size changes — programmatically or by a native resize.</summary>
    public event EventHandler? Resize;

    /// <summary>Raised after <see cref="Resize"/> whenever the form's size changes.</summary>
    public event EventHandler? SizeChanged;

    /// <summary>
    /// The verdict this form reports from <see cref="ShowDialog"/>. Setting a value other than
    /// <see cref="DialogResult.None"/> while the form is shown modally closes it — the WinForms
    /// contract a <see cref="Button.DialogResult"/> click relies on.
    /// </summary>
    public DialogResult DialogResult
    {
        get => field;
        set
        {
            field = value;
            if (value != DialogResult.None && _modal)
                this.Close();
        }
    }

    /// <summary>
    /// The button that should act on Enter. Stored for the pending §7.1 focus/key model — nothing
    /// routes the Enter key yet, so today the button only acts when actually clicked (honoring its
    /// <see cref="Button.DialogResult"/>).
    /// </summary>
    public Button? AcceptButton { get; set; }

    /// <summary>
    /// The button that should act on Escape. As in WinForms, assigning it gives the button a
    /// <see cref="DialogResult.Cancel"/> result when it still has none; Escape routing itself waits
    /// on the pending §7.1 focus/key model, so today the button only acts when actually clicked.
    /// </summary>
    public Button? CancelButton
    {
        get => field;
        set
        {
            field = value;
            if (value is { DialogResult: DialogResult.None })
                value.DialogResult = DialogResult.Cancel;
        }
    }

    /// <summary>
    /// Where the form is placed when it is shown. Centering is computed in the core against
    /// <see cref="IPlatformBackend.GetScreenSize"/> (or the owner's bounds) and written into
    /// <see cref="Control.Bounds"/> just before realization.
    /// </summary>
    public FormStartPosition StartPosition { get; set; }

    /// <summary>The frame the native window wears. Defaults to <see cref="FormBorderStyle.Sizable"/>.</summary>
    public FormBorderStyle FormBorderStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _window?.SetBorderStyle(value);
        }
    } = FormBorderStyle.Sizable;

    /// <summary>
    /// Whether the window is shown normally, minimized or maximized. Two-way: assigning drives the
    /// native window, and native state changes (the caption buttons) flow back in without echoing,
    /// raising <see cref="Resize"/>/<see cref="SizeChanged"/>.
    /// </summary>
    public FormWindowState WindowState
    {
        get => _windowState;
        set
        {
            if (_windowState == value)
                return;

            _windowState = value;
            _window?.SetWindowState(value);
        }
    }

    /// <summary>
    /// Whether the caption shows a minimize button. On GTK the value is advisory — the window
    /// manager owns the caption buttons.
    /// </summary>
    public bool MinimizeBox
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _window?.SetMinimizeBox(value);
        }
    } = true;

    /// <summary>
    /// Whether the caption shows a maximize button. On GTK the value is advisory — the window
    /// manager owns the caption buttons.
    /// </summary>
    public bool MaximizeBox
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _window?.SetMaximizeBox(value);
        }
    } = true;

    /// <summary>
    /// The smallest size the user can resize the form to; <see cref="Size.Empty"/> (or a zero
    /// component) leaves that axis unconstrained. Assigning clamps the current size immediately.
    /// </summary>
    public Size MinimumSize
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ApplySizeLimits();
        }
    }

    /// <summary>
    /// The largest size the user can resize the form to; <see cref="Size.Empty"/> (or a zero
    /// component) leaves that axis unconstrained. Assigning clamps the current size immediately.
    /// </summary>
    public Size MaximumSize
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ApplySizeLimits();
        }
    }

    /// <summary>Keeps the window above all normal windows.</summary>
    public bool TopMost
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _window?.SetTopMost(value);
        }
    }

    /// <summary>
    /// The window's overall opacity, clamped to 0 (invisible) … 1 (opaque). On Linux the effect
    /// needs a compositing window manager.
    /// </summary>
    public double Opacity
    {
        get => field;
        set
        {
            value = Math.Clamp(value, 0d, 1d);
            if (field == value)
                return;

            field = value;
            _window?.SetOpacity(value);
        }
    } = 1d;

    /// <summary>
    /// Replaces the caption/taskbar icon from 32-bit ARGB pixels (row-major, length =
    /// width * height) — the same decoder-free pipeline as <see cref="ImageList"/> and
    /// <see cref="NotifyIcon.SetIcon"/>.
    /// </summary>
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
        _window?.SetIcon(width, height, _iconPixels);
    }

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateWindow();

    /// <summary>Raises <see cref="FormClosed"/>.</summary>
    protected virtual void OnFormClosed(EventArgs e) => this.FormClosed?.Invoke(this, e);

    /// <summary>Raises <see cref="Resize"/>.</summary>
    protected virtual void OnResize(EventArgs e) => this.Resize?.Invoke(this, e);

    /// <summary>Raises <see cref="SizeChanged"/>.</summary>
    protected virtual void OnSizeChanged(EventArgs e) => this.SizeChanged?.Invoke(this, e);

    /// <summary>Wires the window peer and flushes the buffered window-management state into it.</summary>
    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not IWindowPeer window)
            return;

        _window = window;
        _lastSize = this.Bounds.Size;
        window.Closed += this.OnPeerClosed;
        window.BoundsChangedByUser += this.OnPeerBoundsChanged;
        window.WindowStateChanged += this.OnPeerWindowStateChanged;
        window.SetBorderStyle(this.FormBorderStyle);
        window.SetMinimizeBox(this.MinimizeBox);
        window.SetMaximizeBox(this.MaximizeBox);
        window.SetSizeLimits(this.MinimumSize, this.MaximumSize);
        window.SetWindowState(_windowState);
        window.SetTopMost(this.TopMost);
        window.SetOpacity(this.Opacity);
        if (_iconPixels is { } pixels)
            window.SetIcon(_iconWidth, _iconHeight, pixels);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        if (_window is not { } window)
            return;

        window.Closed -= this.OnPeerClosed;
        window.BoundsChangedByUser -= this.OnPeerBoundsChanged;
        window.WindowStateChanged -= this.OnPeerWindowStateChanged;
        _window = null;
    }

    /// <summary>Raises <see cref="Resize"/> and <see cref="SizeChanged"/> whenever the size part of the bounds changes.</summary>
    private protected override void OnBoundsChanged()
    {
        var size = this.Bounds.Size;
        if (size == _lastSize)
            return;

        _lastSize = size;
        this.OnResize(EventArgs.Empty);
        this.OnSizeChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Closes the form as the native close button would. A no-op before realization; on a modal form
    /// this ends the <see cref="ShowDialog"/> loop.
    /// </summary>
    public void Close() => _window?.Close();

    /// <summary>
    /// Shows this form modally: <paramref name="owner"/> (when given) is disabled while a nested
    /// native message loop runs, and the call blocks until the form closes. Returns
    /// <see cref="DialogResult"/> — <see cref="DialogResult.Cancel"/> when the form was closed
    /// without a verdict (close box, Alt+F4). The form unrealizes on return and can be shown again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public DialogResult ShowDialog(Form? owner = null)
        => this.ShowDialog(owner, Application.Current ?? throw new InvalidOperationException(
            "Form.ShowDialog needs a running backend — call it from inside Application.Run."));

    /// <summary>Shows this form modally on an explicit backend. Intended for tests.</summary>
    internal DialogResult ShowDialog(Form? owner, IPlatformBackend backend)
    {
        if (_modal)
            throw new InvalidOperationException("The form is already being shown modally.");

        this.DialogResult = DialogResult.None;
        this.ApplyStartPosition(backend, owner);
        var window = this.RealizeAsWindow(backend);
        _modal = true;
        try
        {
            window.RunModal(owner?.WindowPeer);
        }
        finally
        {
            _modal = false;
            this.DisposePeerTree();
        }

        if (this.DialogResult == DialogResult.None)
            this.DialogResult = DialogResult.Cancel;

        return this.DialogResult;
    }

    /// <summary>
    /// Realizes this form and its children against <paramref name="backend"/>, then shows it. Returns
    /// the native window peer that <see cref="Application.Run(Form)"/> hands to the message loop.
    /// Child realization happens inside <see cref="Control.RealizeSelf"/>, which walks the whole
    /// control tree depth-first.
    /// </summary>
    internal IWindowPeer RealizeWindow(IPlatformBackend backend)
    {
        this.ApplyStartPosition(backend, owner: null);
        var window = this.RealizeAsWindow(backend);
        window.Show();
        return window;
    }

    /// <summary>Realizes the control tree into a window peer (wired by <see cref="OnRealized"/>).</summary>
    private IWindowPeer RealizeAsWindow(IPlatformBackend backend) => (IWindowPeer)this.RealizeSelf(backend);

    /// <summary>
    /// Applies <see cref="StartPosition"/> by rewriting <see cref="Control.Bounds"/> before the
    /// window is realized: centering math happens here in the core, against the backend's screen
    /// size or the owner's bounds, so no peer ever sees the placement policy.
    /// </summary>
    private void ApplyStartPosition(IPlatformBackend backend, Form? owner)
    {
        var area = this.StartPosition switch
        {
            FormStartPosition.CenterScreen => new Rectangle(Point.Empty, backend.GetScreenSize()),
            FormStartPosition.CenterParent when owner is not null => owner.Bounds,
            FormStartPosition.CenterParent => new Rectangle(Point.Empty, backend.GetScreenSize()),
            _ => Rectangle.Empty,
        };

        if (area.IsEmpty)
            return;

        var size = this.Bounds.Size;
        this.Location = new(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (area.Height - size.Height) / 2);
    }

    /// <summary>Forwards the peer's close notification to <see cref="OnFormClosed"/>.</summary>
    private void OnPeerClosed(object? sender, EventArgs e) => this.OnFormClosed(EventArgs.Empty);

    /// <summary>
    /// Adopts bounds the native window reports (a user resize or move) without echoing them back;
    /// a size change raises <see cref="Resize"/>/<see cref="SizeChanged"/> through
    /// <see cref="OnBoundsChanged"/>.
    /// </summary>
    private void OnPeerBoundsChanged(object? sender, Rectangle bounds) => this.SetBoundsFromPeer(bounds);

    /// <summary>
    /// Syncs a native minimize/maximize/restore back into <see cref="WindowState"/>. Guarded by
    /// value comparison, so echoes of programmatic state writes never loop; a real change raises
    /// <see cref="Resize"/>/<see cref="SizeChanged"/> like WinForms does.
    /// </summary>
    private void OnPeerWindowStateChanged(object? sender, FormWindowState state)
    {
        if (_windowState == state)
            return;

        _windowState = state;
        this.OnResize(EventArgs.Empty);
        this.OnSizeChanged(EventArgs.Empty);
    }

    /// <summary>Clamps the current size to the limits and pushes both limits to the peer.</summary>
    private void ApplySizeLimits()
    {
        var minimum = this.MinimumSize;
        var maximum = this.MaximumSize;
        var size = this.Bounds.Size;
        var width = Math.Max(size.Width, minimum.Width);
        var height = Math.Max(size.Height, minimum.Height);
        if (maximum.Width > 0)
            width = Math.Min(width, maximum.Width);
        if (maximum.Height > 0)
            height = Math.Min(height, maximum.Height);

        this.Size = new(width, height);
        _window?.SetSizeLimits(minimum, maximum);
    }
}
