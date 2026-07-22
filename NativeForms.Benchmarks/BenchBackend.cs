using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Benchmarks;

/// <summary>
/// A display-free backend for the benchmark harness: every peer is a silent state sink, so measured
/// numbers contain only the toolkit's own work — no windowing system, no recording overhead. Kept in
/// this project (rather than sharing the test fakes) so the harness stays a plain Core consumer with
/// zero extra dependencies.
/// </summary>
internal sealed class BenchBackend : IPlatformBackend
{
    /// <summary>Every canvas peer created, in realization order — the surfaces the paint benchmarks drive.</summary>
    public List<BenchCanvasPeer> Canvases { get; } = [];

    public string Name => "Bench";
    public bool IsSupported => true;
    public ITheme Theme => DefaultTheme.Instance;

    public event EventHandler? ThemeChanged { add { } remove { } }

    public double GetDpiScale() => 1.0;
    public IWindowPeer CreateWindow() => new BenchWindowPeer();
    public IButtonPeer CreateButton() => new BenchButtonPeer();
    public ILabelPeer CreateLabel() => new BenchLabelPeer();
    public ITextBoxPeer CreateTextBox() => new BenchTextBoxPeer();
    public IRichTextBoxPeer CreateRichTextBox() => new BenchRichTextBoxPeer();

    public ICanvasPeer CreateCanvas()
    {
        var canvas = new BenchCanvasPeer();
        this.Canvases.Add(canvas);
        return canvas;
    }

    public IPopupPeer CreatePopup(IWindowPeer? owner) => new BenchPopupPeer();
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => new BenchImage(width, height);
    public ITimerPeer CreateTimer() => new BenchTimerPeer();
    public INotifyIconPeer CreateNotifyIcon() => new BenchNotifyIconPeer();
    public Size GetScreenSize() => new(1920, 1080);
    public Size MeasureText(string text, Font font) => BenchGraphics.Measure(text);
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null) => DialogResult.OK;
    public string[]? ShowFileDialog(in FileDialogOptions options) => null;
    public Color? ShowColorDialog(Color color) => null;
    public Font? ShowFontDialog(Font font) => null;
    public void SetClipboardText(string text) { }
    public string? GetClipboardText() => null;
    public void Post(Action action) => action();
    public void Run(IWindowPeer mainWindow) { }
    public void Quit() { }
}

/// <summary>An <see cref="IGraphics"/> that draws nothing, so paint benchmarks time only the control's
/// own path. Text measures with fixed per-character metrics to stay deterministic across machines.</summary>
internal sealed class BenchGraphics : IGraphics
{
    private const int _CharWidth = 7;
    private const int _LineHeight = 16;

    public void FillRectangle(Color color, Rectangle bounds) { }
    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1) { }
    public void FillEllipse(Color color, Rectangle bounds) { }
    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1) { }
    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius) { }
    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1) { }
    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1) { }
    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft) { }
    public Size MeasureText(string text, Font font) => Measure(text);
    public void DrawImage(IImage image, Rectangle bounds) { }
    public void PushClip(Rectangle bounds) { }
    public void PopClip() { }

    /// <summary>The deterministic measurement shared with <see cref="BenchBackend.MeasureText"/>.</summary>
    internal static Size Measure(string text) => new((text?.Length ?? 0) * _CharWidth, _LineHeight);
}

/// <summary>Base silent peer: accepts every state push, raises nothing.</summary>
internal abstract class BenchPeer : IControlPeer
{
    protected Rectangle BoundsField;

    public event EventHandler? GotFocus { add { } remove { } }
    public event EventHandler? LostFocus { add { } remove { } }
    public event EventHandler<MouseEventArgs>? PointerMove { add { } remove { } }
    public event EventHandler? PointerLeave { add { } remove { } }

    public void SetBounds(Rectangle bounds) => this.BoundsField = bounds;
    public virtual void SetText(string text) { }
    public void SetVisible(bool visible) { }
    public void SetEnabled(bool enabled) { }
    public void SetFont(Font font) { }
    public void SetColors(Color foreColor, Color backColor) { }
    public void SetCursor(Cursor cursor) { }
    public Point PointToScreen(Point clientPoint) => clientPoint;
    public void Focus() { }
    public void ShowToolTip(string? text) { }
    public void Dispose() { }
}

internal sealed class BenchWindowPeer : BenchPeer, IWindowPeer
{
    public event EventHandler? Closed { add { } remove { } }
    public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested { add { } remove { } }
    public event EventHandler<Rectangle>? BoundsChangedByUser { add { } remove { } }
    public event EventHandler<FormWindowState>? WindowStateChanged { add { } remove { } }

    public void AddChild(IControlPeer child) { }
    public void RemoveChild(IControlPeer child) { }
    public void Show() { }
    public void RunModal(IWindowPeer? owner) { }
    public void Close() { }
    public void SetBorderStyle(FormBorderStyle borderStyle) { }
    public void SetWindowState(FormWindowState state) { }
    public void SetMinimizeBox(bool visible) { }
    public void SetMaximizeBox(bool visible) { }
    public void SetSizeLimits(Size minimum, Size maximum) { }
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb) { }
    public void SetTopMost(bool topMost) { }

    public void SetQuitsOnClose(bool quits) { }
    public void SetOpacity(double opacity) { }
}

internal sealed class BenchButtonPeer : BenchPeer, IButtonPeer
{
    public event EventHandler? Clicked { add { } remove { } }

    public void SetImage(IImage? image, ContentAlignment imageAlign, TextImageRelation relation) { }

    public void SetDefault(bool isDefault) { }
}

internal sealed class BenchLabelPeer : BenchPeer, ILabelPeer
{
    public void SetTextAlign(ContentAlignment alignment) { }
    public void SetBorderStyle(BorderStyle borderStyle) { }
    public void SetUseMnemonic(bool useMnemonic) { }
    public void SetImage(IImage? image, ContentAlignment imageAlign) { }
}

internal class BenchTextBoxPeer : BenchPeer, ITextBoxPeer
{
    private string _text = string.Empty;

    public event EventHandler? TextChangedByUser { add { } remove { } }
    public event EventHandler<KeyEventArgs>? KeyDown { add { } remove { } }

    public override void SetText(string text) => _text = text;
    public void SetMultiline(bool multiline) { }
    public void SetPlaceholder(string placeholder) { }
    public void SetPasswordChar(char passwordChar) { }
    public void SetReadOnly(bool readOnly) { }
    public void SetMaxLength(int maxLength) { }
    public void SetSelection(int start, int length) { }
    public (int Start, int Length) GetSelection() => (0, 0);
    public string GetText() => _text;
}

internal sealed class BenchRichTextBoxPeer : BenchTextBoxPeer, IRichTextBoxPeer
{
    public event EventHandler<string>? LinkClicked { add { } remove { } }

    public void SetSelectionStyle(FontStyle style, bool enabled) { }
    public void SetSelectionColor(Color color) { }
    public void SetSelectionFontSize(float sizeInPoints) { }
    public void SetSelectionAlignment(ContentAlignment alignment) { }
    public void SetSelectionBullet(bool bullet) { }
    public void SetDetectUrls(bool detectUrls) { }
    public void SetZoom(float factor) { }
    public string GetRtf() => string.Empty;
    public void SetRtf(string rtf) { }
}

/// <summary>The canvas the paint benchmarks drive: exposes Perform* raisers and reuses one
/// <see cref="PaintEventArgs"/> across frames exactly like the real canvas peers, so a steady-state
/// paint through it allocates nothing on the harness side.</summary>
internal class BenchCanvasPeer : BenchPeer, ICanvasPeer
{
    private PaintEventArgs? _paintArgs;
    private BenchGraphics? _paintGraphics;

    public event EventHandler<PaintEventArgs>? Paint;
    public event EventHandler<MouseEventArgs>? MouseDown;
    public event EventHandler<MouseEventArgs>? MouseUp;
    public event EventHandler<MouseEventArgs>? MouseMove;
    public event EventHandler<MouseEventArgs>? MouseWheel;
    public event EventHandler? MouseLeave;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;
    public event EventHandler<KeyPressEventArgs>? KeyPress;

    public void AddChild(IControlPeer child) { }
    public void RemoveChild(IControlPeer child) { }
    public void Invalidate(Rectangle bounds) { }
    public void InvalidateAll() { }
    public void SetFocusable(bool focusable) { }

    /// <summary>Raises one frame's <see cref="Paint"/> over the shared null surface.</summary>
    public void PerformPaint()
    {
        var graphics = _paintGraphics ??= new();
        var args = _paintArgs ??= new(graphics, new Rectangle(Point.Empty, this.BoundsField.Size));
        this.Paint?.Invoke(this, args);
    }

    /// <summary>Raises a key press as the platform would — the scroll driver of the traversal benchmarks.</summary>
    public void PerformKeyDown(Keys key) => this.KeyDown?.Invoke(this, new KeyEventArgs(key, KeyModifiers.None));

    /// <summary>Raises a wheel turn as the platform would.</summary>
    public void PerformMouseWheel(int delta) => this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, 0, delta));

    /// <summary>Raises a button press, for completeness of the surface contract.</summary>
    public void PerformMouseDown(int x, int y) => this.MouseDown?.Invoke(this, new MouseEventArgs(MouseButtons.Left, x, y, 0));

    /// <summary>Raises a button release.</summary>
    public void PerformMouseUp(int x, int y) => this.MouseUp?.Invoke(this, new MouseEventArgs(MouseButtons.Left, x, y, 0));

    /// <summary>Raises a pointer move.</summary>
    public void PerformMouseMove(int x, int y) => this.MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    /// <summary>Raises a pointer leave.</summary>
    public void PerformMouseLeave() => this.MouseLeave?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises a key release.</summary>
    public void PerformKeyUp(Keys key) => this.KeyUp?.Invoke(this, new KeyEventArgs(key, KeyModifiers.None));

    /// <summary>Raises a typed character.</summary>
    public void PerformKeyPress(char c) => this.KeyPress?.Invoke(this, new KeyPressEventArgs(c));
}

internal sealed class BenchPopupPeer : BenchCanvasPeer, IPopupPeer
{
    public event EventHandler? Dismissed { add { } remove { } }

    public bool LightDismiss { get; set; } = true;

    public void ShowAt(Point screenLocation, Size size) { }
    public void Hide() { }
}

internal sealed class BenchTimerPeer : ITimerPeer
{
    public event EventHandler? Tick { add { } remove { } }

    public void Start(int intervalMs) { }
    public void Stop() { }
    public void Dispose() { }
}

internal sealed class BenchNotifyIconPeer : INotifyIconPeer
{
    public event EventHandler? Click { add { } remove { } }
    public event EventHandler? DoubleClick { add { } remove { } }

    public void SetIcon(int width, int height, ReadOnlySpan<int> argb) { }
    public void SetToolTip(string text) { }
    public void SetVisible(bool visible) { }
    public void Dispose() { }
}

internal sealed class BenchImage(int width, int height) : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public void Dispose() { }
}
