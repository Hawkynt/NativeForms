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
    public IPopupPeer CreatePopup() => this.Track(new HeadlessPopupPeer());
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

    /// <summary>Where the widget's client origin sits on the fake screen; settable so tests can
    /// assert client-to-screen placement math without a windowing system.</summary>
    public Point ScreenOrigin { get; set; }

    /// <summary>The owning backend, wired by tracking — lets <see cref="Focus"/> drive the
    /// backend-wide focus simulation.</summary>
    internal HeadlessBackend? Owner { get; set; }

    /// <summary>Whether <see cref="Focus"/> was called on this peer.</summary>
    public bool FocusRequested { get; private set; }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;

    public void SetBounds(Rectangle bounds) => this.Bounds = bounds;
    public void SetText(string text) => this.Text = text;
    public void SetVisible(bool visible) => this.Visible = visible;
    public void SetEnabled(bool enabled) => this.Enabled = enabled;
    public void SetFont(Font font) => this.Font = font;

    public void SetColors(Color foreColor, Color backColor)
    {
        this.ForeColor = foreColor;
        this.BackColor = backColor;
    }

    public void SetCursor(Cursor cursor) => this.Cursor = cursor;
    public Point PointToScreen(Point clientPoint) => new(this.ScreenOrigin.X + clientPoint.X, this.ScreenOrigin.Y + clientPoint.Y);

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

    public void AddChild(IControlPeer child) => this.Children.Add(child);
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

    public event EventHandler? Clicked;

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
    {
        this.SetText(text);
        this.SelectionStart = text.Length;
        this.SelectionLength = 0;
        this.TextChangedByUser?.Invoke(this, EventArgs.Empty);
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

    public void AddChild(IControlPeer child) => this.Children.Add(child);

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

    private PaintEventArgs? _paintArgs;

    // Test helpers — drive the control as the native surface would.
    public RecordingGraphics RaisePaint()
    {
        var graphics = new RecordingGraphics();
        this.Paint?.Invoke(this, new PaintEventArgs(graphics, new Rectangle(Point.Empty, this.Bounds.Size)));
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
        => this.MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    public void RaiseMouseWheel(int delta, int x = 0, int y = 0, KeyModifiers modifiers = KeyModifiers.None)
        => this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, delta, modifiers));

    public void RaiseKeyDown(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyDown?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyUp(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyUp?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyPress(char c) => this.KeyPress?.Invoke(this, new KeyPressEventArgs(c));

    public void RaiseMouseLeave() => this.MouseLeave?.Invoke(this, EventArgs.Empty);
}

/// <summary>A popup peer that records every ShowAt/Hide and lets tests trigger light dismissal.</summary>
internal sealed class HeadlessPopupPeer : HeadlessCanvasPeer, IPopupPeer
{
    public List<(Point Location, Size Size)> ShowCalls { get; } = [];
    public int HideCount { get; private set; }
    public bool IsShown { get; private set; }

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
}

/// <summary>An <see cref="IGraphics"/> that records draw calls for assertions and measures text deterministically.</summary>
internal sealed class RecordingGraphics : IGraphics
{
    private const int _CharWidth = 7;
    private const int _LineHeight = 16;

    public List<string> Operations { get; } = [];

    /// <summary>Every text draw with the font it used, so tests can assert font adoption.</summary>
    public List<(string Text, Font Font)> TextDraws { get; } = [];

    public void FillRectangle(Color color, Rectangle bounds)
        => this.Operations.Add($"fill {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
        => this.Operations.Add($"rect {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void FillEllipse(Color color, Rectangle bounds)
        => this.Operations.Add($"fillellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
        => this.Operations.Add($"ellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius)
        => this.Operations.Add($"fillround {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height} r{radius}");

    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1)
        => this.Operations.Add($"round {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height} r{radius}");

    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
        => this.Operations.Add($"line {Hex(color)} {x1},{y1}-{x2},{y2}");

    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft)
    {
        this.Operations.Add($"text \"{text}\" {Hex(color)} {alignment} @{bounds.X},{bounds.Y}");
        this.TextDraws.Add((text, font));
    }

    public Size MeasureText(string text, Font font) => Measure(text);

    /// <summary>The deterministic measurement shared with <see cref="HeadlessBackend.MeasureText"/>.</summary>
    internal static Size Measure(string text) => new((text?.Length ?? 0) * _CharWidth, _LineHeight);

    public void DrawImage(IImage image, Rectangle bounds)
        => this.Operations.Add($"image {image.Width}x{image.Height} @{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void PushClip(Rectangle bounds) => this.Operations.Add($"clip {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void PopClip() => this.Operations.Add("unclip");

    /// <summary>Whether any recorded draw-text op contains the given substring.</summary>
    public bool DrewText(string substring) => this.Operations.Exists(o => o.StartsWith("text ") && o.Contains(substring));

    /// <summary>Whether any text containing the given substring was drawn in the given font.</summary>
    public bool DrewTextWithFont(string substring, Font font)
        => this.TextDraws.Exists(d => d.Text.Contains(substring) && d.Font == font);

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
