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
    private Control? _activeControl;
    private bool _modal;
    private FormWindowState _windowState;
    private Size _lastSize;
    private int[]? _iconPixels;
    private int _iconWidth;
    private int _iconHeight;
    private CloseReason _closeReason;

    /// <summary>The realized native window peer, or <see langword="null"/> before realization.</summary>
    internal IWindowPeer? WindowPeer => _window;

    /// <summary>
    /// Whether closing this form quits the application's message loop. A normal top-level form does —
    /// the program ends with its main window — but a secondary window such as a floating docking pane
    /// overrides this to <see langword="false"/> so closing it leaves the application running.
    /// </summary>
    private protected virtual bool QuitsOnLoopClose => true;

    /// <summary>
    /// Raised after the form is realized and before it is first shown — the moment initialization
    /// code that needs live peers (measuring, focusing) traditionally runs. Fires on every show:
    /// once per <see cref="Application.Run(Form)"/>, <see cref="Show"/> or <see cref="ShowDialog"/>,
    /// since the form unrealizes between them.
    /// </summary>
    public event EventHandler? Load;

    /// <summary>
    /// Raised before the form closes — by the native close button, <see cref="Close"/>, or a modal
    /// verdict — carrying the <see cref="CloseReason"/>. Set
    /// <see cref="FormClosingEventArgs.Cancel"/> to veto and keep the window open.
    /// </summary>
    public event EventHandler<FormClosingEventArgs>? FormClosing;

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
    /// The button Enter clicks through the form's dialog-key chain (honoring its
    /// <see cref="Button.DialogResult"/>), unless the focused control claims Enter for itself — an
    /// open drop-down, a grid edit. The chain sees keys from owner-drawn surfaces; a focused native
    /// text widget consumes its keys inside the widget, where no preview exists yet.
    /// </summary>
    public Button? AcceptButton { get; set; }

    /// <summary>
    /// The button Escape clicks through the form's dialog-key chain. As in WinForms, assigning it
    /// gives the button a <see cref="DialogResult.Cancel"/> result when it still has none. Like
    /// <see cref="AcceptButton"/>, the routing sees keys from owner-drawn surfaces only, and a
    /// focused control may claim Escape (to close its own drop-down or cancel its edit) first.
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

    /// <summary>
    /// The child control that holds keyboard focus, tracked from peer focus events. Assigning
    /// focuses the control when it can take focus; assigned before the form is shown, it becomes
    /// the initial focus instead of the first control in tab order — the WinForms contract.
    /// </summary>
    public Control? ActiveControl
    {
        get => _activeControl;
        set
        {
            _activeControl = value;
            value?.Focus();
        }
    }

    /// <summary>
    /// Adopts a focus gain reported by a child's peer: records it as <see cref="ActiveControl"/> and
    /// fires the container-chain crossings — <see cref="Control.Leave"/> on containers that no longer
    /// host focus (innermost first), then <see cref="Control.Enter"/> on the newly entered ones
    /// (outermost first, the WinForms order). The gaining control raises its own Enter/GotFocus pair
    /// afterwards; the losing control already raised LostFocus/Leave from its own peer event.
    /// </summary>
    internal void NotifyFocusGained(Control gained)
    {
        if (ReferenceEquals(gained, this))
            return; // window-level focus is activation, not a child focus change

        var previous = _activeControl;
        _activeControl = gained;
        if (previous is null || ReferenceEquals(previous, gained))
            return;

        for (var parent = previous.Parent; parent is not null && !ReferenceEquals(parent, this); parent = parent.Parent)
            if (!parent.IsAncestorOf(gained))
                parent.RaiseLeave();

        EnterContainerChain(gained.Parent, previous);

        void EnterContainerChain(Control? container, Control left)
        {
            if (container is null || ReferenceEquals(container, this))
                return;

            EnterContainerChain(container.Parent, left);
            if (!container.IsAncestorOf(left))
                container.RaiseEnter();
        }
    }

    /// <summary>
    /// The form-level dialog-key chain, run by a focused owner-drawn control before its own key
    /// handling: registered menu shortcuts, Alt+mnemonics, Tab/Shift+Tab navigation, Enter →
    /// <see cref="AcceptButton"/> and Escape → <see cref="CancelButton"/>. Returns whether the key
    /// was consumed. A control that needs one of these keys claims it via
    /// <see cref="Control.IsInputKey"/> and handles it itself. Native widgets (text boxes) consume
    /// their keys inside the widget, so their keystrokes never reach this chain — a documented
    /// platform limit until peer key previews exist.
    /// </summary>
    internal bool ProcessDialogKey(Control source, KeyEventArgs e)
    {
        var keyData = e.KeyData;
        if (source.WantsInputKey(keyData))
            return false;

        if (DispatchMenuShortcut(this, keyData))
            return true;

        if ((keyData & Keys.Modifiers) == Keys.Alt && this.ProcessMnemonic(keyData & Keys.KeyCode))
            return true;

        switch (keyData)
        {
            case Keys.Tab:
            case Keys.Tab | Keys.Shift:
                this.MoveFocus(source, forward: (keyData & Keys.Shift) == 0);
                return true;

            case Keys.Enter:
                return PerformDialogClick(this.AcceptButton);

            case Keys.Escape:
                return PerformDialogClick(this.CancelButton);

            default:
                return false;
        }
    }

    /// <summary>Clicks a dialog button when it is present, visible and enabled.</summary>
    private static bool PerformDialogClick(Button? button)
    {
        if (button is not { Visible: true, Enabled: true })
            return false;

        button.PerformClick();
        return true;
    }

    /// <summary>Depth-first dispatch of a shortcut chord to every <see cref="MenuStrip"/> hosted on the form.</summary>
    private static bool DispatchMenuShortcut(Control parent, Keys keyData)
    {
        if (parent.ChildrenOrNull is not { } children)
            return false;

        for (var i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            if (child is MenuStrip strip && strip.ProcessShortcut(keyData))
                return true;

            if (DispatchMenuShortcut(child, keyData))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Alt+letter/digit handling: opens the matching <see cref="MenuStrip"/> top-level menu, or — for
    /// a <see cref="Label"/> mnemonic — focuses the next tab stop after the label, the WinForms label
    /// contract. Owner-drawn surfaces feed this; button mnemonics stay with the native widgets.
    /// </summary>
    private bool ProcessMnemonic(Keys keyCode)
    {
        if (keyCode is not ((>= Keys.A and <= Keys.Z) or (>= Keys.D0 and <= Keys.D9)))
            return false;

        var mnemonic = (char)keyCode;
        return OpenMenuMnemonic(this, mnemonic) || this.FocusLabelMnemonic(mnemonic);
    }

    /// <summary>Finds the first <see cref="MenuStrip"/> whose top-level mnemonic matches and opens that menu.</summary>
    private static bool OpenMenuMnemonic(Control parent, char mnemonic)
    {
        if (parent.ChildrenOrNull is not { } children)
            return false;

        for (var i = 0; i < children.Count; ++i)
        {
            var child = children[i];
            if (child is MenuStrip strip && strip.OpenMnemonic(mnemonic))
                return true;

            if (OpenMenuMnemonic(child, mnemonic))
                return true;
        }

        return false;
    }

    /// <summary>Scratch list the focus walks reuse, so a Tab press or mnemonic allocates nothing
    /// once warm. Safe to share: every walk stops touching it the moment it moves focus.</summary>
    private List<Control>? _tabOrder;

    /// <summary>Rebuilds the flattened tab order into the reused scratch list.</summary>
    private List<Control> BuildTabOrder()
    {
        var order = _tabOrder ??= [];
        order.Clear();
        AppendInTabOrder(this, order);
        return order;
    }

    /// <summary>Finds the label carrying <paramref name="mnemonic"/> and focuses the next tab stop after it.</summary>
    private bool FocusLabelMnemonic(char mnemonic)
    {
        var order = this.BuildTabOrder();
        for (var i = 0; i < order.Count; ++i)
        {
            if (order[i] is not Label label || label.Mnemonic != mnemonic)
                continue;

            // The label itself never takes focus: move on to the next tab stop, wrapping around.
            for (var j = 1; j < order.Count; ++j)
            {
                var candidate = order[(i + j) % order.Count];
                if (candidate.TabStop && candidate.CanFocus)
                {
                    candidate.Focus();
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Moves focus to the next (or previous) tab stop relative to <paramref name="from"/>, wrapping
    /// around the form. <see langword="null"/> starts from the top — the initial-focus walk.
    /// </summary>
    internal void MoveFocus(Control? from, bool forward)
    {
        var order = this.BuildTabOrder();
        var count = order.Count;
        if (count == 0)
            return;

        var start = from is null ? forward ? -1 : count : order.IndexOf(from);
        var step = forward ? 1 : -1;
        for (var i = 1; i <= count; ++i)
        {
            var candidate = order[(((start + (step * i)) % count) + count) % count];
            if (!candidate.TabStop || !candidate.CanFocus)
                continue;

            candidate.Focus();
            return;
        }
    }

    /// <summary>
    /// Flattens <paramref name="parent"/>'s subtree depth-first in tab order: siblings ascend by
    /// <see cref="Control.TabIndex"/> (a stable sort, so ties keep insertion order) and every child
    /// is followed immediately by its own subtree — the WinForms traversal. Invisible or disabled
    /// subtrees are skipped wholesale: their children cannot receive focus either.
    /// </summary>
    private static void AppendInTabOrder(Control parent, List<Control> order)
    {
        if (parent.ChildrenOrNull is not { Count: > 0 } children)
            return;

        var count = children.Count;

        Span<int> indices = count <= 64 ? stackalloc int[count] : new int[count];
        for (var i = 0; i < count; ++i)
            indices[i] = i;

        // Stable insertion sort by TabIndex; sibling lists are small.
        for (var i = 1; i < count; ++i)
        {
            var current = indices[i];
            var key = children[current].TabIndex;
            var j = i - 1;
            for (; j >= 0 && children[indices[j]].TabIndex > key; --j)
                indices[j + 1] = indices[j];

            indices[j + 1] = current;
        }

        for (var i = 0; i < count; ++i)
        {
            var child = children[indices[i]];
            if (!child.Visible || !child.Enabled)
                continue;

            order.Add(child);
            AppendInTabOrder(child, order);
        }
    }

    /// <summary>
    /// Gives a freshly shown form its initial focus, exactly like WinForms: the buffered
    /// <see cref="ActiveControl"/> wish when it can take focus, otherwise the first tab stop.
    /// </summary>
    private void ApplyInitialFocus()
    {
        if (_activeControl is { CanFocus: true } wish)
        {
            wish.Focus();
            return;
        }

        this.MoveFocus(null, forward: true);
    }

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateWindow();

    /// <summary>Raises <see cref="Load"/>.</summary>
    protected virtual void OnLoad(EventArgs e) => this.Load?.Invoke(this, e);

    /// <summary>Raises <see cref="FormClosing"/>.</summary>
    protected virtual void OnFormClosing(FormClosingEventArgs e) => this.FormClosing?.Invoke(this, e);

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
        window.SetQuitsOnClose(this.QuitsOnLoopClose);
        window.CloseRequested += this.OnPeerCloseRequested;
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

        window.CloseRequested -= this.OnPeerCloseRequested;
        window.Closed -= this.OnPeerClosed;
        window.BoundsChangedByUser -= this.OnPeerBoundsChanged;
        window.WindowStateChanged -= this.OnPeerWindowStateChanged;
        _window = null;
        _activeControl = null;
    }

    /// <summary>Runs the Anchor/Dock layout pass over the form's children, then raises
    /// <see cref="Resize"/> and <see cref="SizeChanged"/> whenever the size part of the bounds
    /// changed — so handlers doing manual layout see the engine's results, not the other way
    /// around.</summary>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        var size = this.Bounds.Size;
        if (size == _lastSize)
            return;

        _lastSize = size;
        this.OnResize(EventArgs.Empty);
        this.OnSizeChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Closes the form as the native close button would, running the <see cref="FormClosing"/> veto
    /// with <see cref="CloseReason.ProgrammaticClosing"/> first. A no-op before realization; on a
    /// modal form this ends the <see cref="ShowDialog"/> loop.
    /// </summary>
    public void Close()
    {
        if (_window is not { } window)
            return;

        _closeReason = CloseReason.ProgrammaticClosing;
        try
        {
            window.Close();
        }
        finally
        {
            _closeReason = CloseReason.None;
        }
    }

    /// <summary>
    /// The size of the form. Windows Forms subtracts the non-client frame here; no peer reports its
    /// non-client metrics yet, so for now <see cref="ClientSize"/> equals <see cref="Control.Size"/>
    /// on every platform — a documented platform gap, not a contract.
    /// </summary>
    public Size ClientSize
    {
        get => this.Size;
        set => this.Size = value;
    }

    /// <summary>
    /// Shows this form modelessly on the running application's backend: the form realizes, appears,
    /// and the call returns immediately — the window then lives on the already-pumping message loop
    /// and closes through the usual <see cref="FormClosing"/>/<see cref="FormClosed"/> path.
    /// Showing an already-realized form just re-shows its window.
    /// </summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public void Show()
    {
        if (_window is { } window)
        {
            window.Show();
            return;
        }

        var backend = Application.Current ?? throw new InvalidOperationException(
            "Form.Show needs a running message loop — call it while Application.Run is active.");
        this.RealizeWindow(backend);
    }

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
        this.OnLoad(EventArgs.Empty);
        this.ApplyInitialFocus();
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
        this.OnLoad(EventArgs.Empty);
        window.Show();
        this.ApplyInitialFocus();
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

    /// <summary>
    /// Runs the <see cref="FormClosing"/> veto for a close the peer announces before committing:
    /// the reason is <see cref="CloseReason.ProgrammaticClosing"/> while a <see cref="Close"/> call
    /// is on the stack, <see cref="CloseReason.UserClosing"/> for a platform-initiated close (the
    /// native close button, Alt+F4). Cancelling keeps the window open.
    /// </summary>
    private void OnPeerCloseRequested(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var args = new FormClosingEventArgs(_closeReason == CloseReason.None ? CloseReason.UserClosing : _closeReason);
        this.OnFormClosing(args);
        e.Cancel = args.Cancel;
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
