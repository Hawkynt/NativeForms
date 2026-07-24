using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Text;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// A display-free backend that records every peer interaction, so control-tree, realization, paint
/// and input logic can be unit-tested without a windowing system. <see cref="Run"/> returns
/// immediately rather than blocking on a message loop.
/// </summary>
internal sealed class HeadlessBackend : IPlatformBackend
{
    public List<HeadlessPeer> Created { get; } = [];
    public List<HeadlessTimerPeer> Timers { get; } = [];
    public List<HeadlessNotifyIconPeer> NotifyIcons { get; } = [];
    public bool DidRun { get; private set; }
    public bool DidQuit { get; private set; }

    /// <summary>Optional callback invoked inside <see cref="Run"/>, standing in for the work a real
    /// message loop would dispatch while it pumps (timer arming, event handlers, …).</summary>
    public Action? RunAction { get; set; }

    /// <summary>Optional callback invoked inside <see cref="HeadlessWindowPeer.RunModal"/>, standing in
    /// for the nested modal loop — tests use it to click buttons or close the dialog.</summary>
    public Action<HeadlessWindowPeer>? ModalAction { get; set; }

    /// <summary>Every <see cref="ShowMessageBox"/> call, in the order it arrived — including the
    /// owner window peer (or <see langword="null"/> for the ownerless overloads).</summary>
    public List<(string Text, string Caption, MessageBoxButtons Buttons, MessageBoxIcon Icon, IWindowPeer? Owner)> MessageBoxes { get; } = [];

    /// <summary>The scripted verdict <see cref="ShowMessageBox"/> returns.</summary>
    public DialogResult MessageBoxResult { get; set; } = DialogResult.OK;

    /// <summary>The options of the most recent <see cref="ShowFileDialog"/> call.</summary>
    public FileDialogOptions? LastFileDialog { get; private set; }

    /// <summary>The scripted paths <see cref="ShowFileDialog"/> returns (null = user cancelled).</summary>
    public string[]? FileDialogResult { get; set; }

    /// <summary>The initial color of the most recent <see cref="ShowColorDialog"/> call.</summary>
    public Color? LastColorDialogColor { get; private set; }

    /// <summary>The scripted color <see cref="ShowColorDialog"/> returns (null = user cancelled).</summary>
    public Color? ColorDialogResult { get; set; }

    /// <summary>The initial font of the most recent <see cref="ShowFontDialog"/> call.</summary>
    public Font? LastFontDialogFont { get; private set; }

    /// <summary>The scripted font <see cref="ShowFontDialog"/> returns (null = user cancelled).</summary>
    public Font? FontDialogResult { get; set; }

    /// <summary>Every <see cref="SetClipboardText"/> call, in the order it arrived.</summary>
    public List<string> ClipboardTexts { get; } = [];

    /// <summary>The scripted text <see cref="GetClipboardText"/> returns (null = no text on the clipboard).</summary>
    public string? ClipboardText { get; set; }

    /// <summary>Every action handed to <see cref="Post"/>, recorded before it executes.</summary>
    public List<Action> PostedActions { get; } = [];

    public string Name => "Headless";
    public bool IsSupported => true;

    /// <summary>The theme served to controls; swappable so tests can script a desktop theme change
    /// (swap, then <see cref="FireThemeChanged"/>).</summary>
    public ITheme Theme { get; set; } = DefaultTheme.Instance;

    /// <inheritdoc/>
    public event EventHandler? ThemeChanged;

    /// <summary>Raises <see cref="ThemeChanged"/> as the platform would after a desktop theme change.</summary>
    public void FireThemeChanged() => this.ThemeChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The fake DPI scale <see cref="GetDpiScale"/> reports; settable so tests can assert
    /// logical-to-device math against a known factor.</summary>
    public double DpiScale { get; set; } = 1.0;

    public double GetDpiScale() => this.DpiScale;

    /// <summary>The peer holding the simulated keyboard focus, or null while nothing is focused.</summary>
    public HeadlessPeer? FocusedPeer { get; private set; }

    /// <summary>Moves the simulated focus like a real windowing system: the previous peer raises
    /// LostFocus first, then the new one raises GotFocus.</summary>
    internal void SetSimulatedFocus(HeadlessPeer peer)
    {
        if (ReferenceEquals(this.FocusedPeer, peer))
            return;

        var previous = this.FocusedPeer;
        this.FocusedPeer = peer;
        previous?.RaiseLostFocus();
        peer.RaiseGotFocus();
    }

    /// <summary>The fake screen size <see cref="GetScreenSize"/> reports; settable so tests can
    /// assert the core's centering math against a known geometry.</summary>
    public Size ScreenSize { get; set; } = new(1920, 1080);

    public Size GetScreenSize() => this.ScreenSize;

    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
    {
        this.MessageBoxes.Add((text, caption, buttons, icon, owner));
        return this.MessageBoxResult;
    }

    public string[]? ShowFileDialog(in FileDialogOptions options)
    {
        this.LastFileDialog = options;
        return this.FileDialogResult;
    }

    public Color? ShowColorDialog(Color color)
    {
        this.LastColorDialogColor = color;
        return this.ColorDialogResult;
    }

    public Font? ShowFontDialog(Font font)
    {
        this.LastFontDialogFont = font;
        return this.FontDialogResult;
    }

    public IWindowPeer CreateWindow() => this.Track(new HeadlessWindowPeer(this));
    public IButtonPeer CreateButton() => this.Track(new HeadlessButtonPeer());
    public ILabelPeer CreateLabel() => this.Track(new HeadlessLabelPeer());
    public ITextBoxPeer CreateTextBox() => this.Track(new HeadlessTextBoxPeer());
    public IRichTextBoxPeer CreateRichTextBox() => this.Track(new HeadlessRichTextBoxPeer());
    public ICanvasPeer CreateCanvas() => this.Track(new HeadlessCanvasPeer());
    public IPopupPeer CreatePopup(IWindowPeer? owner) => this.Track(new HeadlessPopupPeer { OwnerWindow = owner });
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => new HeadlessImage(width, height);
    public Size MeasureText(string text, Font font) => RecordingGraphics.Measure(text);

    public ITimerPeer CreateTimer()
    {
        var peer = new HeadlessTimerPeer();
        this.Timers.Add(peer);
        return peer;
    }

    public INotifyIconPeer CreateNotifyIcon()
    {
        var peer = new HeadlessNotifyIconPeer();
        this.NotifyIcons.Add(peer);
        return peer;
    }

    public void Run(IWindowPeer mainWindow)
    {
        this.DidRun = true;
        this.RunAction?.Invoke();
    }

    public void SetClipboardText(string text) => this.ClipboardTexts.Add(text);

    public string? GetClipboardText() => this.ClipboardText;

    /// <summary>Records the action, then executes it inline — there is no real loop to defer to, so
    /// the "queue" drains immediately and marshalling logic stays observable and deterministic.</summary>
    public void Post(Action action)
    {
        this.PostedActions.Add(action);
        action();
    }

    public void Quit() => this.DidQuit = true;

    private T Track<T>(T peer) where T : HeadlessPeer
    {
        peer.Owner = this;
        this.Created.Add(peer);
        return peer;
    }
}

/// <summary>Base recorder peer.</summary>
internal abstract class HeadlessPeer : IControlPeer
{
    public Rectangle Bounds { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool Visible { get; private set; }
    public bool Enabled { get; private set; }
    public bool Disposed { get; private set; }

    /// <summary>The last font pushed by the core, or null while the platform default applies.</summary>
    public Font? Font { get; private set; }

    /// <summary>The last text color pushed by the core; <see cref="Color.Empty"/> = platform default.</summary>
    public Color ForeColor { get; private set; }

    /// <summary>The last background color pushed by the core; <see cref="Color.Empty"/> = platform default.</summary>
    public Color BackColor { get; private set; }

    /// <summary>The last cursor pushed by the core, or null while the platform default applies.</summary>
    public Cursor? Cursor { get; private set; }

    private Point _screenOrigin;
    private bool _screenOriginPinned;

    /// <summary>
    /// Pins where this surface's client origin sits on the fake screen. A surface that was never
    /// pinned has no screen position of its own: it derives one from its bounds plus every ancestor's,
    /// up to the nearest pinned surface or the top level — exactly like a real widget hierarchy.
    /// Pinning is therefore a fixture shortcut that says "this surface is known to be here", and
    /// everything nested below it still resolves through the chain.
    /// </summary>
    public Point ScreenOrigin
    {
        get => _screenOrigin;
        set
        {
            _screenOrigin = value;
            _screenOriginPinned = true;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? PointerMove;

    /// <inheritdoc/>
    public event EventHandler? PointerLeave;

    /// <summary>Simulates the pointer moving over this peer — the fake's stand-in for a native
    /// motion event, so hover-driven behavior is assertable without a display.</summary>
    public void RaisePointerMove(int x, int y)
        => this.PointerMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    /// <summary>Simulates the pointer leaving this peer.</summary>
    public void RaisePointerLeave() => this.PointerLeave?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>Simulates a right-click / Menu-key context-menu request at a client-space point — the
    /// fake's stand-in for the native <c>WM_CONTEXTMENU</c> / button-3 press. Returns whether the core
    /// opened a menu, exactly as a real peer reads back to suppress the widget's own default menu.</summary>
    public bool RaiseContextMenu(int x, int y)
    {
        if (this.ContextMenuRequested is not { } handler)
            return false;

        var args = new ContextMenuRequestedEventArgs(new Point(x, y));
        handler(this, args);
        return args.Handled;
    }

    /// <summary>The platform tip text last asked for, or null while no tip is up — the fake's record
    /// of <see cref="IControlPeer.ShowToolTip"/>.</summary>
    public string? ToolTipText { get; private set; }

    /// <inheritdoc/>
    public void ShowToolTip(string? text) => this.ToolTipText = string.IsNullOrEmpty(text) ? null : text;

    /// <summary>Records the container that adopted this peer — the chain that
    /// <see cref="PointToScreen"/>, input routing and effective visibility all walk.</summary>
    internal void AttachTo(HeadlessPeer parent) => this.ParentPeer = parent;

    /// <summary>The child surfaces this peer hosts; empty for a leaf.</summary>
    internal virtual IReadOnlyList<IControlPeer> ChildPeers => [];

    /// <summary>The owning backend, wired by tracking — lets <see cref="Focus"/> drive the
    /// backend-wide focus simulation.</summary>
    internal HeadlessBackend? Owner { get; set; }

    /// <summary>
    /// The peer this one was parented into, recorded by <see cref="IContainerPeer.AddChild"/>.
    /// Deliberately modelled: the real backends nest native widgets, so hiding a container hides its
    /// subtree implicitly and masks a core that forgets to re-push a descendant's visibility. The
    /// fake refuses to mask it — it keeps <see cref="Visible"/> as the value the core actually
    /// pushed and exposes the composed truth separately as <see cref="EffectivelyVisible"/>.
    /// </summary>
    public HeadlessPeer? ParentPeer { get; internal set; }

    /// <summary>
    /// Whether this peer would really be on screen: its own pushed visibility AND every ancestor's.
    /// A descendant the core never re-showed reports <see langword="false"/> here even though its
    /// own <see cref="Visible"/> flag says otherwise, which is what makes "expanding restores every
    /// descendant" assertable.
    /// </summary>
    public bool EffectivelyVisible
    {
        get
        {
            for (var peer = this; peer is not null; peer = peer.ParentPeer)
                if (!peer.Visible)
                    return false;

            return true;
        }
    }

    /// <summary>Every value the core pushed through <see cref="SetVisible"/>, in order — so a test
    /// can tell "never re-pushed" apart from "re-pushed with the same value".</summary>
    public List<bool> VisiblePushes { get; } = [];

    /// <summary>Whether <see cref="Focus"/> was called on this peer.</summary>
    public bool FocusRequested { get; private set; }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;

    public void SetBounds(Rectangle bounds) => this.Bounds = bounds;
    public void SetText(string text) => this.Text = text;

    public void SetVisible(bool visible)
    {
        this.Visible = visible;
        this.VisiblePushes.Add(visible);
    }

    public void SetEnabled(bool enabled) => this.Enabled = enabled;
    public void SetFont(Font font) => this.Font = font;

    public void SetColors(Color foreColor, Color backColor)
    {
        this.ForeColor = foreColor;
        this.BackColor = backColor;
    }

    public void SetCursor(Cursor cursor) => this.Cursor = cursor;

    /// <summary>
    /// Maps a client point to fake-screen coordinates the way a real backend does: accumulate this
    /// surface's own origin and every ancestor's on the way up, then add the top-level's
    /// <see cref="ScreenOrigin"/>. A fake that ignored the ancestor chain would report the same screen
    /// point for a control and for its deeply nested child, and every placement bug that depends on
    /// nesting — a context menu or drop-down opening at the wrong spot — would pass unnoticed.
    /// </summary>
    public Point PointToScreen(Point clientPoint)
    {
        var x = clientPoint.X;
        var y = clientPoint.Y;
        for (var peer = this; peer is not null; peer = peer.ParentPeer)
        {
            if (peer._screenOriginPinned || peer.ParentPeer is null)
            {
                x += peer._screenOrigin.X;
                y += peer._screenOrigin.Y;
                break;
            }

            x += peer.Bounds.X;
            y += peer.Bounds.Y;
        }

        return new(x, y);
    }

    /// <summary>
    /// Resolves the surface that owns an input point given in this surface's client space: the
    /// topmost visible descendant whose bounds contain it, with the point re-expressed in that
    /// surface's own client space.
    /// </summary>
    /// <remarks>
    /// This is the routing contract every real backend implements and the fake used not to model at
    /// all. A windowing system hands the event to exactly one surface — the innermost one under the
    /// pointer — and to nobody else; an ancestor never sees a child's event, and above all never sees
    /// it still carrying the child's untranslated coordinates. Tests that dispatch through
    /// <see cref="RouteMouseDown"/> and friends can therefore prove input does not leak up the tree.
    /// </remarks>
    internal (HeadlessCanvasPeer? Target, Point Location) Route(Point clientPoint)
    {
        var container = this;
        var point = clientPoint;
        var target = this as HeadlessCanvasPeer;
        for (var descended = true; descended;)
        {
            descended = false;
            var children = container.ChildPeers;

            // Last added sits on top, so it is the first candidate an event would land on.
            for (var i = children.Count - 1; i >= 0; --i)
            {
                if (children[i] is not HeadlessCanvasPeer child || !child.Visible || !child.Bounds.Contains(point))
                    continue;

                point = new(point.X - child.Bounds.X, point.Y - child.Bounds.Y);
                container = child;
                target = child;
                descended = true;
                break;
            }
        }

        return (target, point);
    }

    /// <summary>Delivers a press to the surface that owns the point, and to that surface only.</summary>
    internal (HeadlessCanvasPeer? Target, Point Location) RouteMouseDown(Point clientPoint, MouseButtons button = MouseButtons.Left)
    {
        var route = this.Route(clientPoint);
        route.Target?.RaiseMouseDown(route.Location.X, route.Location.Y, button);
        return route;
    }

    /// <summary>Delivers a release to the surface that owns the point, and to that surface only.</summary>
    internal (HeadlessCanvasPeer? Target, Point Location) RouteMouseUp(Point clientPoint, MouseButtons button = MouseButtons.Left)
    {
        var route = this.Route(clientPoint);
        route.Target?.RaiseMouseUp(route.Location.X, route.Location.Y, button);
        return route;
    }

    /// <summary>Delivers a move to the surface that owns the point, and to that surface only.</summary>
    internal (HeadlessCanvasPeer? Target, Point Location) RouteMouseMove(Point clientPoint)
    {
        var route = this.Route(clientPoint);
        route.Target?.RaiseMouseMove(route.Location.X, route.Location.Y);
        return route;
    }

    /// <summary>Records the request and moves the backend's simulated focus here, so the previous
    /// peer loses focus before this one gains it — exactly like a real windowing system.</summary>
    public void Focus()
    {
        this.FocusRequested = true;
        this.Owner?.SetSimulatedFocus(this);
    }

    /// <summary>Raises <see cref="GotFocus"/> as the platform would.</summary>
    public void RaiseGotFocus() => this.GotFocus?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="LostFocus"/> as the platform would.</summary>
    public void RaiseLostFocus() => this.LostFocus?.Invoke(this, EventArgs.Empty);

    public void Dispose() => this.Disposed = true;
}

internal sealed class HeadlessWindowPeer(HeadlessBackend? backend = null) : HeadlessPeer, IWindowPeer
{
    public List<IControlPeer> Children { get; } = [];
    public bool Shown { get; private set; }

    /// <summary>Whether <see cref="RunModal"/> ran this window as a modal dialog.</summary>
    public bool WasModal { get; private set; }

    /// <summary>The owner peer <see cref="RunModal"/> received, or null.</summary>
    public IWindowPeer? ModalOwner { get; private set; }

    /// <summary>How often <see cref="Close"/> was called.</summary>
    public int CloseCount { get; private set; }

    // Window-management state, recorded exactly as the core pushes it.
    public FormBorderStyle BorderStyle { get; private set; } = FormBorderStyle.Sizable;
    public FormWindowState WindowState { get; private set; }
    public bool MinimizeBox { get; private set; } = true;
    public bool MaximizeBox { get; private set; } = true;
    public Size MinimumSize { get; private set; }
    public Size MaximumSize { get; private set; }
    public int IconWidth { get; private set; }
    public int IconHeight { get; private set; }
    public int[]? IconPixels { get; private set; }
    public bool TopMost { get; private set; }
    public double Opacity { get; private set; } = 1d;

    /// <summary>Every window-management Set* call, in the order it arrived — the echo detector.</summary>
    public List<string> Calls { get; } = [];

    public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested;
    public event EventHandler? Closed;
    public event EventHandler<Rectangle>? BoundsChangedByUser;
    public event EventHandler<FormWindowState>? WindowStateChanged;

    /// <inheritdoc/>
    internal override IReadOnlyList<IControlPeer> ChildPeers => this.Children;

    public void AddChild(IControlPeer child)
    {
        this.Children.Add(child);
        (child as HeadlessPeer)?.AttachTo(this);
    }

    public void RemoveChild(IControlPeer child) => this.Children.Remove(child);

    public void Show() => this.Shown = true;

    public void SetBorderStyle(FormBorderStyle borderStyle)
    {
        this.BorderStyle = borderStyle;
        this.Calls.Add($"borderStyle={borderStyle}");
    }

    public void SetWindowState(FormWindowState state)
    {
        this.WindowState = state;
        this.Calls.Add($"state={state}");
    }

    public void SetMinimizeBox(bool visible)
    {
        this.MinimizeBox = visible;
        this.Calls.Add($"minimizeBox={visible}");
    }

    public void SetMaximizeBox(bool visible)
    {
        this.MaximizeBox = visible;
        this.Calls.Add($"maximizeBox={visible}");
    }

    public void SetSizeLimits(Size minimum, Size maximum)
    {
        this.MinimumSize = minimum;
        this.MaximumSize = maximum;
        this.Calls.Add($"sizeLimits={minimum.Width}x{minimum.Height},{maximum.Width}x{maximum.Height}");
    }

    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        this.IconWidth = width;
        this.IconHeight = height;
        this.IconPixels = argb.ToArray();
        this.Calls.Add($"icon={width}x{height}");
    }

    public void SetTopMost(bool topMost)
    {
        this.TopMost = topMost;
        this.Calls.Add($"topMost={topMost}");
    }

    public bool QuitsOnClose { get; private set; } = true;

    public void SetQuitsOnClose(bool quits)
    {
        this.QuitsOnClose = quits;
        this.Calls.Add($"quitsOnClose={quits}");
    }

    public void SetOpacity(double opacity)
    {
        this.Opacity = opacity;
        this.Calls.Add($"opacity={opacity}");
    }

    /// <summary>Reports a native move/resize as the platform would (no <see cref="IControlPeer.SetBounds"/> involved).</summary>
    public void FireBoundsChanged(Rectangle bounds) => this.BoundsChangedByUser?.Invoke(this, bounds);

    /// <summary>Reports a native minimize/maximize/restore as the platform would.</summary>
    public void FireWindowStateChanged(FormWindowState state)
    {
        this.WindowState = state;
        this.WindowStateChanged?.Invoke(this, state);
    }

    /// <summary>Records the modality, then hands control to the test's
    /// <see cref="HeadlessBackend.ModalAction"/> — the stand-in for the nested native loop, inside
    /// which test code closes the dialog.</summary>
    public void RunModal(IWindowPeer? owner)
    {
        this.WasModal = true;
        this.ModalOwner = owner;
        this.Shown = true;
        backend?.ModalAction?.Invoke(this);
    }

    /// <summary>Closes the window as the native close button would: runs the
    /// <see cref="CloseRequested"/> veto, then hides the window and raises <see cref="Closed"/> —
    /// or leaves it open when a subscriber cancelled.</summary>
    public void Close()
    {
        ++this.CloseCount;
        if (this.RaiseCloseRequested())
            return;

        this.Shown = false;
        this.RaiseClosed();
    }

    /// <summary>Raises <see cref="CloseRequested"/> and reports whether a subscriber vetoed the close.</summary>
    public bool RaiseCloseRequested()
    {
        var args = new System.ComponentModel.CancelEventArgs();
        this.CloseRequested?.Invoke(this, args);
        return args.Cancel;
    }

    public void RaiseClosed() => this.Closed?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessButtonPeer : HeadlessPeer, IButtonPeer
{
    public IImage? Image { get; private set; }
    public ContentAlignment ImageAlign { get; private set; }
    public TextImageRelation ImageRelation { get; private set; }

    /// <summary>Whether the form marked this button as its default (accept) button.</summary>
    public bool IsDefault { get; private set; }

    public event EventHandler? Clicked;

    public void SetDefault(bool isDefault) => this.IsDefault = isDefault;

    public void SetImage(IImage? image, ContentAlignment imageAlign, TextImageRelation relation)
    {
        this.Image = image;
        this.ImageAlign = imageAlign;
        this.ImageRelation = relation;
    }

    public void RaiseClicked() => this.Clicked?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessLabelPeer : HeadlessPeer, ILabelPeer
{
    public ContentAlignment TextAlign { get; private set; }
    public BorderStyle BorderStyle { get; private set; }
    public bool UseMnemonic { get; private set; } = true;
    public IImage? Image { get; private set; }
    public ContentAlignment ImageAlign { get; private set; }

    public void SetTextAlign(ContentAlignment alignment) => this.TextAlign = alignment;
    public void SetBorderStyle(BorderStyle borderStyle) => this.BorderStyle = borderStyle;
    public void SetUseMnemonic(bool useMnemonic) => this.UseMnemonic = useMnemonic;

    public void SetImage(IImage? image, ContentAlignment imageAlign)
    {
        this.Image = image;
        this.ImageAlign = imageAlign;
    }
}

/// <summary>A text-box peer that records every edit-specific setting and lets tests simulate user edits.</summary>
internal class HeadlessTextBoxPeer : HeadlessPeer, ITextBoxPeer
{
    public bool Multiline { get; private set; }
    public string Placeholder { get; private set; } = string.Empty;
    public char PasswordChar { get; private set; }
    public bool ReadOnly { get; private set; }
    public int MaxLength { get; private set; }
    public int SelectionStart { get; private set; }
    public int SelectionLength { get; private set; }

    /// <summary>Every textbox-specific Set* call, in the order it arrived.</summary>
    public List<string> Calls { get; } = [];

    public event EventHandler? TextChangedByUser;

    public event EventHandler<KeyEventArgs>? KeyDown;

    public void SetMultiline(bool multiline)
    {
        this.Multiline = multiline;
        this.Calls.Add($"multiline={multiline}");
    }

    public void SetPlaceholder(string placeholder)
    {
        this.Placeholder = placeholder;
        this.Calls.Add($"placeholder={placeholder}");
    }

    public void SetPasswordChar(char passwordChar)
    {
        this.PasswordChar = passwordChar;
        this.Calls.Add($"passwordChar={passwordChar}");
    }

    public void SetReadOnly(bool readOnly)
    {
        this.ReadOnly = readOnly;
        this.Calls.Add($"readOnly={readOnly}");
    }

    public void SetMaxLength(int maxLength)
    {
        this.MaxLength = maxLength;
        this.Calls.Add($"maxLength={maxLength}");
    }

    public void SetSelection(int start, int length)
    {
        this.SelectionStart = start;
        this.SelectionLength = length;
        this.Calls.Add($"selection={start},{length}");
    }

    public (int Start, int Length) GetSelection() => (this.SelectionStart, this.SelectionLength);

    public string GetText() => this.Text;

    /// <summary>Simulates the user replacing the widget's content, leaving the caret at the end.</summary>
    public void SimulateUserInput(string text)
        => this.SimulateUserInput(text, text.Length);

    /// <summary>
    /// Simulates the user editing the widget's content, reporting <paramref name="caret"/> as the
    /// place the edit began — the convention <see cref="ITextBoxPeer.GetSelection"/> promises for the
    /// duration of a change, and the only way to express which edit produced a candidate when the
    /// text alone is ambiguous.
    /// </summary>
    public void SimulateUserInput(string text, int caret)
    {
        this.SetText(text);
        this.SelectionStart = caret;
        this.SelectionLength = 0;
        this.TextChangedByUser?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates keystrokes typed at the caret, as the native editor would apply them.</summary>
    public void SimulateTyping(string characters)
    {
        foreach (var c in characters)
        {
            var caret = Math.Clamp(this.SelectionStart, 0, this.Text.Length);
            this.SimulateUserInput(string.Concat(this.Text.AsSpan(0, caret), c.ToString(), this.Text.AsSpan(caret)), caret);
        }
    }

    /// <summary>Simulates a key pressed inside the widget; returns whether a handler consumed it.</summary>
    public bool SimulateKeyDown(Keys key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs(key, modifiers);
        this.KeyDown?.Invoke(this, args);
        return args.Handled;
    }
}

/// <summary>
/// A rich-text peer that records every formatting call together with the selection it applied to,
/// keeps the last document set through RTF, and round-trips <see cref="GetRtf"/> through the core
/// <see cref="RtfSerializer"/> — the reference implementation of the RTF-less platform contract.
/// </summary>
internal sealed class HeadlessRichTextBoxPeer : HeadlessTextBoxPeer, IRichTextBoxPeer
{
    private RichDocument? _document;

    /// <summary>Every rich-specific call, in order, formatted as <c>name=value@selectionStart,selectionLength</c>.</summary>
    public List<string> RichCalls { get; } = [];

    public bool DetectUrls { get; private set; }
    public float Zoom { get; private set; } = 1f;

    public event EventHandler<string>? LinkClicked;

    public void SetSelectionStyle(FontStyle style, bool enabled)
        => this.RichCalls.Add($"style={style},{enabled}@{this.SelectionStart},{this.SelectionLength}");

    public void SetSelectionColor(Color color)
        => this.RichCalls.Add($"color=#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}@{this.SelectionStart},{this.SelectionLength}");

    public void SetSelectionFontSize(float sizeInPoints)
        => this.RichCalls.Add($"fontSize={sizeInPoints}@{this.SelectionStart},{this.SelectionLength}");

    public void SetSelectionAlignment(ContentAlignment alignment)
        => this.RichCalls.Add($"alignment={alignment}@{this.SelectionStart},{this.SelectionLength}");

    public void SetSelectionBullet(bool bullet)
        => this.RichCalls.Add($"bullet={bullet}@{this.SelectionStart},{this.SelectionLength}");

    public void SetDetectUrls(bool detectUrls)
    {
        this.DetectUrls = detectUrls;
        this.RichCalls.Add($"detectUrls={detectUrls}");
    }

    public void SetZoom(float factor)
    {
        this.Zoom = factor;
        this.RichCalls.Add($"zoom={factor}");
    }

    public string GetRtf() => RtfSerializer.Write(_document ?? RichDocument.FromPlainText(this.GetText()));

    public void SetRtf(string rtf)
    {
        _document = RtfSerializer.Parse(rtf);
        this.SimulateUserInput(_document.ToPlainText());
    }

    /// <summary>Raises <see cref="LinkClicked"/> as the platform would when the user activates a link.</summary>
    public void FireLinkClicked(string linkText) => this.LinkClicked?.Invoke(this, linkText);
}

/// <summary>A timer peer that records every Start/Stop and lets tests raise ticks by hand.</summary>
internal sealed class HeadlessTimerPeer : ITimerPeer
{
    public List<int> StartedIntervals { get; } = [];
    public int StopCount { get; private set; }
    public bool IsRunning { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler? Tick;

    public void Start(int intervalMs)
    {
        this.StartedIntervals.Add(intervalMs);
        this.IsRunning = true;
    }

    public void Stop()
    {
        ++this.StopCount;
        this.IsRunning = false;
    }

    public void Dispose()
    {
        this.IsRunning = false;
        this.Disposed = true;
    }

    /// <summary>Raises <see cref="Tick"/> as the platform message loop would.</summary>
    public void FireTick() => this.Tick?.Invoke(this, EventArgs.Empty);
}

/// <summary>A tray-icon peer that records every state push and lets tests raise clicks by hand.</summary>
internal sealed class HeadlessNotifyIconPeer : INotifyIconPeer
{
    public int IconWidth { get; private set; }
    public int IconHeight { get; private set; }
    public int[]? IconPixels { get; private set; }
    public string ToolTip { get; private set; } = string.Empty;
    public bool Visible { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler? Click;
    public event EventHandler? DoubleClick;

    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        this.IconWidth = width;
        this.IconHeight = height;
        this.IconPixels = argb.ToArray();
    }

    public void SetToolTip(string text) => this.ToolTip = text;

    public void SetVisible(bool visible) => this.Visible = visible;

    public void Dispose()
    {
        this.Visible = false;
        this.Disposed = true;
    }

    /// <summary>Raises <see cref="Click"/> as a shell primary-button click would.</summary>
    public void FireClick() => this.Click?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="DoubleClick"/> as a shell double-click would.</summary>
    public void FireDoubleClick() => this.DoubleClick?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessImage(int width, int height) : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public void Dispose() { }
}

/// <summary>A canvas peer whose events tests can raise directly, with a recording graphics surface.</summary>
internal class HeadlessCanvasPeer : HeadlessPeer, ICanvasPeer
{
    public List<IControlPeer> Children { get; } = [];
    public bool Focusable { get; private set; }
    public int InvalidateCount { get; private set; }

    /// <inheritdoc/>
    internal override IReadOnlyList<IControlPeer> ChildPeers => this.Children;

    public void AddChild(IControlPeer child)
    {
        this.Children.Add(child);
        (child as HeadlessPeer)?.AttachTo(this);
    }

    public void RemoveChild(IControlPeer child) => this.Children.Remove(child);

    public event EventHandler<PaintEventArgs>? Paint;
    public event EventHandler<MouseEventArgs>? MouseDown;
    public event EventHandler<MouseEventArgs>? MouseUp;
    public event EventHandler<MouseEventArgs>? MouseMove;
    public event EventHandler<MouseEventArgs>? MouseWheel;
    public event EventHandler? MouseLeave;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;
    public event EventHandler<KeyPressEventArgs>? KeyPress;

    public void Invalidate(Rectangle bounds) => ++this.InvalidateCount;
    public void InvalidateAll() => ++this.InvalidateCount;
    public void SetFocusable(bool focusable) => this.Focusable = focusable;

    /// <summary>
    /// The visible child peers whose bounds leave this surface's client rectangle — the ones a
    /// backend has to cut off at the edge. A scrolling container moves its children's peers rather
    /// than their logical bounds, so the ones scrolled out of view land here; the real backends must
    /// clip them instead of letting them paint over the container's neighbours.
    /// </summary>
    public List<IControlPeer> ChildrenOutsideClientRectangle
    {
        get
        {
            var client = new Rectangle(Point.Empty, this.Bounds.Size);
            var result = new List<IControlPeer>();
            foreach (var child in this.Children)
                if (child is HeadlessPeer peer && peer.Visible && !client.Contains(peer.Bounds))
                    result.Add(child);

            return result;
        }
    }

    private PaintEventArgs? _paintArgs;

    // Test helpers — drive the control as the native surface would.

    /// <summary>Raises <see cref="Paint"/> over a fresh recorder whose surface is the control's
    /// client rectangle, so anything drawn past it lands in
    /// <see cref="RecordingGraphics.OutOfBoundsOperations"/> — a real backend's window would clip it
    /// away, and the fake must not pretend otherwise.</summary>
    public RecordingGraphics RaisePaint()
    {
        var client = new Rectangle(Point.Empty, this.Bounds.Size);
        var graphics = new RecordingGraphics { Surface = client };
        this.Paint?.Invoke(this, new PaintEventArgs(graphics, client));
        return graphics;
    }

    /// <summary>Raises <see cref="Paint"/> over a caller-supplied surface, reusing one
    /// <see cref="PaintEventArgs"/> across calls exactly like the real canvas peers — the hook the
    /// steady-state paint-allocation test drives.</summary>
    public void RaisePaint(IGraphics graphics)
    {
        var args = _paintArgs ??= new PaintEventArgs(graphics, default);
        args.Reset(graphics, new Rectangle(Point.Empty, this.Bounds.Size));
        this.Paint?.Invoke(this, args);
    }

    public void RaiseMouseDown(int x, int y, MouseButtons button = MouseButtons.Left, KeyModifiers modifiers = KeyModifiers.None)
        => this.MouseDown?.Invoke(this, new MouseEventArgs(button, x, y, 0, modifiers));

    public void RaiseMouseUp(int x, int y, MouseButtons button = MouseButtons.Left, KeyModifiers modifiers = KeyModifiers.None)
        => this.MouseUp?.Invoke(this, new MouseEventArgs(button, x, y, 0, modifiers));

    public void RaiseMouseMove(int x, int y)
    {
        // Mirrors the real canvas peers, which bridge a move onto the shared pointer channel so a
        // control's MouseMove/MouseEnter surface for owner-drawn surfaces exactly as for native ones.
        this.MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));
        this.RaisePointerMove(x, y);
    }

    public void RaiseMouseWheel(int delta, int x = 0, int y = 0, KeyModifiers modifiers = KeyModifiers.None)
        => this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, delta, modifiers));

    public void RaiseKeyDown(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyDown?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyUp(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyUp?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyPress(char c) => this.KeyPress?.Invoke(this, new KeyPressEventArgs(c));

    public void RaiseMouseLeave()
    {
        this.MouseLeave?.Invoke(this, EventArgs.Empty);
        this.RaisePointerLeave();
    }
}

/// <summary>A popup peer that records every ShowAt/Hide and lets tests trigger light dismissal.</summary>
internal sealed class HeadlessPopupPeer : HeadlessCanvasPeer, IPopupPeer
{
    /// <summary>The window the surface was created for, so tests can assert that a popup is anchored
    /// to the form that opened it rather than floating unowned.</summary>
    public IWindowPeer? OwnerWindow { get; init; }

    public List<(Point Location, Size Size)> ShowCalls { get; } = [];
    public int HideCount { get; private set; }
    public bool IsShown { get; private set; }

    public bool LightDismiss { get; set; } = true;

    /// <summary>How many times the menu engine told this popup a grab handoff to a child is expected.</summary>
    public int ExpectGrabHandoffCount { get; private set; }

    /// <summary>How many times the menu engine had this popup re-take the grab a closed child held.</summary>
    public int RegrabCount { get; private set; }

    /// <summary>The popup this one was anchored to as a nested level, so tests can assert a submenu
    /// chains to the level that opened it rather than to the owning window.</summary>
    public IPopupPeer? ParentPopup { get; private set; }

    public void ExpectGrabHandoff() => ++this.ExpectGrabHandoffCount;
    public void Regrab() => ++this.RegrabCount;
    public void SetParentPopup(IPopupPeer parent) => this.ParentPopup = parent;

    public event EventHandler? Dismissed;

    public void ShowAt(Point screenLocation, Size size)
    {
        this.ShowCalls.Add((screenLocation, size));
        this.IsShown = true;
    }

    public void Hide()
    {
        ++this.HideCount;
        this.IsShown = false;
    }

    /// <summary>Dismisses the popup as the platform would: hides the surface first, then raises
    /// <see cref="Dismissed"/>. A no-op while the popup is not shown, matching every real backend.</summary>
    public void FireDismiss()
    {
        if (!this.IsShown)
            return;

        this.Hide();
        this.Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="Dismissed"/> without hiding — the grab-broken a real backend reports
    /// when a child popup takes the grab from this one. The surface stays shown (the child owns the
    /// grab), so this reproduces the async parent-dismissal that must not tear the cascade down.</summary>
    public void RaiseGrabBroken() => this.Dismissed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An <see cref="IGraphics"/> that records draw calls for assertions and measures text
/// deterministically.
///
/// It models the clip region for real rather than just logging the push/pop: every draw is
/// intersected with the current clip, so a control that paints outside its client rectangle is
/// detectable instead of silently recorded. <see cref="Operations"/> keeps listing what the control
/// asked for (assertions about intent keep working), while <see cref="ClippedOperations"/> lists
/// only what would actually reach the surface and <see cref="OutOfBoundsOperations"/> lists the ops
/// that fell wholly or partly outside — the assertion hook for "a control's paint is clipped to its
/// client rectangle".
/// </summary>
internal sealed class RecordingGraphics : IGraphics
{
    private const int _CharWidth = 7;
    private const int _LineHeight = 16;

    private readonly Stack<Rectangle> _clips = new();

    /// <summary>Every draw call the control issued, clipped or not — the record of intent.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>Only the draws that survive the current clip region, in order.</summary>
    public List<string> ClippedOperations { get; } = [];

    /// <summary>Every draw whose bounds left the clip region, as <c>op | bounds</c>.</summary>
    public List<string> OutOfBoundsOperations { get; } = [];

    /// <summary>Every text draw with the font it used, so tests can assert font adoption.</summary>
    public List<(string Text, Font Font)> TextDraws { get; } = [];

    /// <summary>
    /// The clip the surface starts with — the control's client rectangle. Unbounded by default so
    /// the many tests that only assert draw calls stay unaffected; the canvas peer sets it to the
    /// control's size when it raises a paint.
    /// </summary>
    public Rectangle Surface { get; set; } = new(int.MinValue / 2, int.MinValue / 2, int.MaxValue, int.MaxValue);

    /// <summary>The clip currently in force: the surface intersected with every pushed rectangle.</summary>
    public Rectangle CurrentClip => _clips.Count > 0 ? _clips.Peek() : this.Surface;

    /// <summary>Whether every draw so far stayed inside the clip region.</summary>
    public bool StayedInBounds => this.OutOfBoundsOperations.Count == 0;

    private void Record(string op, Rectangle bounds)
    {
        this.Operations.Add(op);

        var clip = this.CurrentClip;
        if (!clip.IntersectsWith(bounds))
        {
            this.OutOfBoundsOperations.Add($"{op} | {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
            return;
        }

        if (!clip.Contains(bounds))
            this.OutOfBoundsOperations.Add($"{op} | {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

        this.ClippedOperations.Add(op);
    }

    public void FillRectangle(Color color, Rectangle bounds)
        => this.Record($"fill {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}", bounds);

    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
        => this.Record($"rect {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}", bounds);

    public void FillEllipse(Color color, Rectangle bounds)
        => this.Record($"fillellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}", bounds);

    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
        => this.Record($"ellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}", bounds);

    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius)
        => this.Record($"fillround {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height} r{radius}", bounds);

    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1)
        => this.Record($"round {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height} r{radius}", bounds);

    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
        => this.Record(
            $"line {Hex(color)} {x1},{y1}-{x2},{y2}",
            Rectangle.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2) + 1, Math.Max(y1, y2) + 1));

    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft)
    {
        this.Record($"text \"{text}\" {Hex(color)} {alignment} @{bounds.X},{bounds.Y}", bounds);
        this.TextDraws.Add((text, font));
    }

    public Size MeasureText(string text, Font font) => Measure(text);

    /// <summary>The deterministic measurement shared with <see cref="HeadlessBackend.MeasureText"/>.</summary>
    internal static Size Measure(string text) => new((text?.Length ?? 0) * _CharWidth, _LineHeight);

    public void DrawImage(IImage image, Rectangle bounds)
        => this.Record($"image {image.Width}x{image.Height} @{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}", bounds);

    public void PushClip(Rectangle bounds)
    {
        this.Operations.Add($"clip {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

        // A push narrows the region; it can never widen it, exactly like cairo_clip and GDI's
        // IntersectClipRect.
        var clip = this.CurrentClip;
        clip.Intersect(bounds);
        _clips.Push(clip);
    }

    public void PopClip()
    {
        this.Operations.Add("unclip");
        if (_clips.Count > 0)
            _clips.Pop();
    }

    /// <summary>Whether any recorded draw-text op contains the given substring.</summary>
    public bool DrewText(string substring) => this.Operations.Exists(o => o.StartsWith("text ") && o.Contains(substring));

    /// <summary>Whether text containing the given substring survived the clip region.</summary>
    public bool DrewTextClipped(string substring)
        => this.ClippedOperations.Exists(o => o.StartsWith("text ") && o.Contains(substring));

    /// <summary>Whether any text containing the given substring was drawn in the given font.</summary>
    public bool DrewTextWithFont(string substring, Font font)
        => this.TextDraws.Exists(d => d.Text.Contains(substring) && d.Font == font);

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
