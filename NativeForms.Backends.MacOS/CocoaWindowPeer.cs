using System.ComponentModel;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A top-level window: a real <c>NSWindow</c> driven through Objective-C messaging.
/// </summary>
/// <remarks>
/// <para>
/// Cocoa's screen origin is the bottom left and its y-axis grows upward, while the toolkit's — like
/// Win32's and GTK's — is the top left growing down. Every rectangle crossing this boundary is flipped
/// against the main screen's height, in one place, so the rest of the backend can think in the
/// toolkit's coordinates. Getting this wrong does not crash; it puts windows off-screen and reads as
/// "nothing appeared", which is why it lives in one named pair of helpers rather than at each call.
/// </para>
/// <para>
/// Members that AppKit needs a hosted view or a run loop for are honest no-ops for now rather than
/// throws: a throw from <c>SetCursor</c> would take the process down over a cursor, and the probe
/// exists to report how far the window gets, which it cannot do if every unimplemented corner is
/// fatal. What is genuinely missing is tracked in <c>docs/PRD.md</c> §2 rather than by exception.
/// </para>
/// </remarks>
internal sealed class CocoaWindowPeer : IWindowPeer
{
    private readonly CocoaBackend _backend;
    // NSWindowStyleMask
    private const nint _Titled = 1 << 0;
    private const nint _Closable = 1 << 1;
    private const nint _Miniaturizable = 1 << 2;
    private const nint _Resizable = 1 << 3;
    private const nint _Borderless = 0;

    /// <summary>NSBackingStoreBuffered.</summary>
    private const nint _Buffered = 2;

    private readonly nint _window;
    private Rectangle _bounds = new(0, 0, 400, 300);
    private nint _style = _Titled | _Closable | _Miniaturizable | _Resizable;

    private bool _quitsOnClose;

    public CocoaWindowPeer(CocoaBackend backend)
    {
        _backend = backend;
        var allocated = CocoaRuntime.Allocate("NSWindow");
        _window = allocated == 0
            ? 0
            : CocoaRuntime.SendRect(
                allocated,
                CocoaRuntime.sel_registerName("initWithContentRect:styleMask:backing:defer:"),
                ToCocoa(_bounds),
                _style,
                _Buffered,
                false);
    }

    /// <summary>The window handle, for the parts of the backend that message it directly.</summary>
    internal nint Handle => _window;

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<MouseEventArgs>? PointerMove;
    public event EventHandler? PointerLeave;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
    public event EventHandler<CancelEventArgs>? CloseRequested;
    public event EventHandler? Closed;
    public event EventHandler<Rectangle>? BoundsChangedByUser;
    public event EventHandler<FormWindowState>? WindowStateChanged;

    /// <summary>The main screen's height, which is the axis every flip turns around.</summary>
    private static double ScreenHeight => CocoaNative.CGDisplayPixelsHigh(CocoaNative.CGMainDisplayID());

    /// <summary>Turns a toolkit rectangle into Cocoa's bottom-left space.</summary>
    private static CocoaRuntime.CGRect ToCocoa(Rectangle bounds)
        => new(bounds.X, ScreenHeight - bounds.Y - bounds.Height, bounds.Width, bounds.Height);

    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (_window != 0)
            CocoaRuntime.SendRectVoid(_window, CocoaRuntime.sel_registerName("setFrame:display:"), ToCocoa(bounds), true);
    }

    public void SetText(string text)
    {
        if (_window == 0)
            return;

        var title = CocoaRuntime.NSString(text);
        if (title == 0)
            return;

        CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setTitle:"), title);
        CocoaNative.CFRelease(title);
    }

    public void SetVisible(bool visible)
    {
        if (_window == 0)
            return;

        if (visible)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("makeKeyAndOrderFront:"), 0);
        else
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("orderOut:"), 0);
    }

    public void Show() => this.SetVisible(true);

    public void Close()
    {
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("close"));

        Closed?.Invoke(this, EventArgs.Empty);

        // The main window closing is what ends the application. AppKit would normally do this through
        // NSApplicationDelegate's applicationShouldTerminateAfterLastWindowClosed:, which needs a
        // delegate object; the peer knows the same fact and can say so directly.
        if (_quitsOnClose)
            _backend.Quit();
    }

    public void SetBorderStyle(FormBorderStyle borderStyle)
    {
        _style = borderStyle switch
        {
            FormBorderStyle.None => _Borderless,
            FormBorderStyle.FixedSingle or FormBorderStyle.FixedDialog or FormBorderStyle.FixedToolWindow
                => _Titled | _Closable,
            _ => _Titled | _Closable | _Miniaturizable | _Resizable,
        };

        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setStyleMask:"), _style);
    }

    public void SetWindowState(FormWindowState state)
    {
        if (_window == 0)
            return;

        switch (state)
        {
            case FormWindowState.Minimized:
                CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("miniaturize:"), 0);
                break;
            case FormWindowState.Maximized:
                CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("zoom:"), 0);
                break;
            default:
                CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("deminiaturize:"), 0);
                break;
        }
    }

    public void SetTopMost(bool topMost)
    {
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setLevel:"), topMost ? 3 : 0);
    }

    public void SetOpacity(double opacity)
    {
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setAlphaValue:"), opacity);
    }

    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    // --- Not yet, and deliberately not fatal (docs/PRD.md §2) ------------------------------------

    public void SetEnabled(bool enabled) { }

    public void SetFont(Font font) { }

    public void SetColors(Color foreColor, Color backColor) { }

    public void SetCursor(Cursor cursor) { }

    public void Focus() { }

    public void ShowToolTip(string? text) { }

    public void AddChild(IControlPeer child) { }

    public void RemoveChild(IControlPeer child) { }

    public void RunModal(IWindowPeer? owner) => this.Show();

    public void SetMinimizeBox(bool visible) { }

    public void SetMaximizeBox(bool visible) { }

    public void SetSizeLimits(Size minimum, Size maximum) { }

    public void SetIcon(int width, int height, ReadOnlySpan<int> argb) { }

    public void SetQuitsOnClose(bool quits) => _quitsOnClose = quits;

    public void Dispose() => this.Close();

    /// <summary>Keeps the compiler from warning that these are never raised while AppKit events are pending.</summary>
    private void Unused()
    {
        GotFocus?.Invoke(this, EventArgs.Empty);
        LostFocus?.Invoke(this, EventArgs.Empty);
        PointerLeave?.Invoke(this, EventArgs.Empty);
        PointerMove?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        ContextMenuRequested?.Invoke(this, new(Point.Empty));
        CloseRequested?.Invoke(this, new());
        BoundsChangedByUser?.Invoke(this, _bounds);
        WindowStateChanged?.Invoke(this, FormWindowState.Normal);
    }
}
