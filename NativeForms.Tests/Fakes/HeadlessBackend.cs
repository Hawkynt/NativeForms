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
    public bool DidRun { get; private set; }
    public bool DidQuit { get; private set; }

    /// <summary>Optional callback invoked inside <see cref="Run"/>, standing in for the work a real
    /// message loop would dispatch while it pumps (timer arming, event handlers, …).</summary>
    public Action? RunAction { get; set; }

    /// <summary>Optional callback invoked inside <see cref="HeadlessWindowPeer.RunModal"/>, standing in
    /// for the nested modal loop — tests use it to click buttons or close the dialog.</summary>
    public Action<HeadlessWindowPeer>? ModalAction { get; set; }

    /// <summary>Every <see cref="ShowMessageBox"/> call, in the order it arrived.</summary>
    public List<(string Text, string Caption, MessageBoxButtons Buttons, MessageBoxIcon Icon)> MessageBoxes { get; } = [];

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

    public string Name => "Headless";
    public bool IsSupported => true;
    public ITheme Theme => DefaultTheme.Instance;

    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        this.MessageBoxes.Add((text, caption, buttons, icon));
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

    public void Run(IWindowPeer mainWindow)
    {
        this.DidRun = true;
        this.RunAction?.Invoke();
    }

    public void Quit() => this.DidQuit = true;

    private T Track<T>(T peer) where T : HeadlessPeer
    {
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

    /// <summary>Where the widget's client origin sits on the fake screen; settable so tests can
    /// assert client-to-screen placement math without a windowing system.</summary>
    public Point ScreenOrigin { get; set; }

    public void SetBounds(Rectangle bounds) => this.Bounds = bounds;
    public void SetText(string text) => this.Text = text;
    public void SetVisible(bool visible) => this.Visible = visible;
    public void SetEnabled(bool enabled) => this.Enabled = enabled;
    public Point PointToScreen(Point clientPoint) => new(this.ScreenOrigin.X + clientPoint.X, this.ScreenOrigin.Y + clientPoint.Y);
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

    public event EventHandler? Closed;

    public void AddChild(IControlPeer child) => this.Children.Add(child);
    public void Show() => this.Shown = true;

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

    /// <summary>Closes the window as the native close button would: hides it and raises <see cref="Closed"/>.</summary>
    public void Close()
    {
        ++this.CloseCount;
        this.Shown = false;
        this.RaiseClosed();
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
    public bool FocusRequested { get; private set; }
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
    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;

    public void Invalidate(Rectangle bounds) => ++this.InvalidateCount;
    public void InvalidateAll() => ++this.InvalidateCount;
    public void Focus() => this.FocusRequested = true;
    public void SetFocusable(bool focusable) => this.Focusable = focusable;

    // Test helpers — drive the control as the native surface would.
    public RecordingGraphics RaisePaint()
    {
        var graphics = new RecordingGraphics();
        this.Paint?.Invoke(this, new PaintEventArgs(graphics, new Rectangle(Point.Empty, this.Bounds.Size)));
        return graphics;
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

    public void RaiseGotFocus() => this.GotFocus?.Invoke(this, EventArgs.Empty);
    public void RaiseLostFocus() => this.LostFocus?.Invoke(this, EventArgs.Empty);
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

    public void FillRectangle(Color color, Rectangle bounds)
        => this.Operations.Add($"fill {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
        => this.Operations.Add($"rect {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void FillEllipse(Color color, Rectangle bounds)
        => this.Operations.Add($"fillellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
        => this.Operations.Add($"ellipse {Hex(color)} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
        => this.Operations.Add($"line {Hex(color)} {x1},{y1}-{x2},{y2}");

    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft)
        => this.Operations.Add($"text \"{text}\" {Hex(color)} {alignment} @{bounds.X},{bounds.Y}");

    public Size MeasureText(string text, Font font) => Measure(text);

    /// <summary>The deterministic measurement shared with <see cref="HeadlessBackend.MeasureText"/>.</summary>
    internal static Size Measure(string text) => new((text?.Length ?? 0) * _CharWidth, _LineHeight);

    public void DrawImage(IImage image, Rectangle bounds)
        => this.Operations.Add($"image {image.Width}x{image.Height} @{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void PushClip(Rectangle bounds) => this.Operations.Add($"clip {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void PopClip() => this.Operations.Add("unclip");

    /// <summary>Whether any recorded draw-text op contains the given substring.</summary>
    public bool DrewText(string substring) => this.Operations.Exists(o => o.StartsWith("text ") && o.Contains(substring));

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
