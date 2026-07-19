using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// A display-free backend that records every peer interaction, so control-tree, realization, paint
/// and input logic can be unit-tested without a windowing system. <see cref="Run"/> returns
/// immediately rather than blocking on a message loop.
/// </summary>
internal sealed class HeadlessBackend : IPlatformBackend
{
    public List<HeadlessPeer> Created { get; } = [];
    public bool DidRun { get; private set; }
    public bool DidQuit { get; private set; }

    public string Name => "Headless";
    public bool IsSupported => true;
    public ITheme Theme => DefaultTheme.Instance;

    public IWindowPeer CreateWindow() => this.Track(new HeadlessWindowPeer());
    public IButtonPeer CreateButton() => this.Track(new HeadlessButtonPeer());
    public ILabelPeer CreateLabel() => this.Track(new HeadlessLabelPeer());
    public ICanvasPeer CreateCanvas() => this.Track(new HeadlessCanvasPeer());
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => new HeadlessImage(width, height);

    public void Run(IWindowPeer mainWindow) => this.DidRun = true;
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

    public void SetBounds(Rectangle bounds) => this.Bounds = bounds;
    public void SetText(string text) => this.Text = text;
    public void SetVisible(bool visible) => this.Visible = visible;
    public void SetEnabled(bool enabled) => this.Enabled = enabled;
    public void Dispose() => this.Disposed = true;
}

internal sealed class HeadlessWindowPeer : HeadlessPeer, IWindowPeer
{
    public List<IControlPeer> Children { get; } = [];
    public bool Shown { get; private set; }

    public event EventHandler? Closed;

    public void AddChild(IControlPeer child) => this.Children.Add(child);
    public void Show() => this.Shown = true;

    public void RaiseClosed() => this.Closed?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessButtonPeer : HeadlessPeer, IButtonPeer
{
    public event EventHandler? Clicked;

    public void RaiseClicked() => this.Clicked?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessLabelPeer : HeadlessPeer, ILabelPeer;

internal sealed class HeadlessImage(int width, int height) : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public void Dispose() { }
}

/// <summary>A canvas peer whose events tests can raise directly, with a recording graphics surface.</summary>
internal sealed class HeadlessCanvasPeer : HeadlessPeer, ICanvasPeer
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

    public void RaiseMouseDown(int x, int y, MouseButtons button = MouseButtons.Left)
        => this.MouseDown?.Invoke(this, new MouseEventArgs(button, x, y, 0));

    public void RaiseMouseUp(int x, int y, MouseButtons button = MouseButtons.Left)
        => this.MouseUp?.Invoke(this, new MouseEventArgs(button, x, y, 0));

    public void RaiseMouseMove(int x, int y)
        => this.MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    public void RaiseMouseWheel(int delta, int x = 0, int y = 0)
        => this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, delta));

    public void RaiseKeyDown(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyDown?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyUp(Keys key, KeyModifiers modifiers = KeyModifiers.None)
        => this.KeyUp?.Invoke(this, new KeyEventArgs(key, modifiers));

    public void RaiseKeyPress(char c) => this.KeyPress?.Invoke(this, new KeyPressEventArgs(c));

    public void RaiseMouseLeave() => this.MouseLeave?.Invoke(this, EventArgs.Empty);

    public void RaiseGotFocus() => this.GotFocus?.Invoke(this, EventArgs.Empty);
    public void RaiseLostFocus() => this.LostFocus?.Invoke(this, EventArgs.Empty);
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

    public Size MeasureText(string text, Font font)
        => new((text?.Length ?? 0) * _CharWidth, _LineHeight);

    public void DrawImage(IImage image, Rectangle bounds)
        => this.Operations.Add($"image {image.Width}x{image.Height} @{bounds.X},{bounds.Y}");

    public void PushClip(Rectangle bounds) => this.Operations.Add($"clip {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");

    public void PopClip() => this.Operations.Add("unclip");

    /// <summary>Whether any recorded draw-text op contains the given substring.</summary>
    public bool DrewText(string substring) => this.Operations.Exists(o => o.StartsWith("text ") && o.Contains(substring));

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
