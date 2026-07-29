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

    /// <summary>Whether <see cref="Close"/> has already run, which is also what ends a modal loop.</summary>
    private volatile bool _closed;

    /// <summary>NSWindowButton: the miniaturize button is 1 and the zoom button 2.</summary>
    private const nint _MiniaturizeButton = 1;
    private const nint _ZoomButton = 2;

    /// <summary>
    /// What AppKit's own <c>maxSize</c> starts as, and therefore what "no limit on this axis" is.
    /// </summary>
    private const double _Unbounded = double.MaxValue;

    // Buffered, because a border-style change rewrites the whole style mask and AppKit rebuilds the
    // caption's buttons from it — so whatever was said about them has to be said again afterwards.
    private bool _minimizeBox = true;
    private bool _maximizeBox = true;

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

        // Off by default, and a window that has it off never even generates the events its views would
        // have to be offered — so a press arrived, a drag arrived, the wheel arrived, and hover did
        // not. Nothing under the pointer highlighted and a menu could not be read at all.
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setAcceptsMouseMovedEvents:"), true);

        // A window built this way frees itself when it is closed, which is right for a document window
        // AppKit owns and wrong for one a peer holds a pointer to: the core closes a modal form and
        // then disposes its peer tree, so the second message would go to memory that is no longer a
        // window. Held instead, and the peer is the only owner there is.
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setReleasedWhenClosed:"), false);

        // A window's content view is a plain NSView, so its origin is the bottom left and every direct
        // child lands mirrored — the menu bar at the foot of the window, the tab strip off the top.
        // Replacing it with the canvas class, which answers isFlipped, makes the window agree with the
        // coordinates its children were laid out in.
        if (_window != 0 && CocoaCanvasPeer.CreateFlippedView() is var content && content != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setContentView:"), content);
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
        // Once. The core closes a form and then disposes its peer tree, and disposal closes too, so a
        // form shown modally would otherwise report itself closed twice and end a session that had
        // already ended.
        if (_closed)
            return;

        _closed = true;
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

        if (_window == 0)
            return;

        CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setStyleMask:"), _style);
        this.ApplyCaptionButtons();
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

    /// <inheritdoc/>
    /// <remarks>
    /// A window has no cursor of its own here — the shape belongs to whichever view the pointer is
    /// over — so the form's goes to its content view, which is one of the backend's own flipped views
    /// and therefore already answers <c>resetCursorRects</c>. A child that sets its own shape claims
    /// its own rectangle on top of this one, exactly as the ambient cursor is meant to work.
    /// </remarks>
    public void SetCursor(Cursor cursor)
    {
        if (_window != 0)
            CocoaCursor.Apply(CocoaRuntime.SendPointer(_window, CocoaRuntime.sel_registerName("contentView")), cursor);
    }

    /// <inheritdoc/>
    /// <remarks>A window has no hover text of its own here either, so the form's goes to the view
    /// that fills it — the same place its cursor goes.</remarks>
    public void ShowToolTip(string? text)
    {
        if (_window != 0)
            CocoaToolTip.Apply(CocoaRuntime.SendPointer(_window, CocoaRuntime.sel_registerName("contentView")), text);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A window takes the keyboard by becoming the key window, which on this desktop also brings it
    /// forward — there is no way to be key and behind, and pretending otherwise would leave a form
    /// that answers the keyboard from underneath another window.
    /// </remarks>
    public void Focus()
    {
        if (_window != 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("makeKeyAndOrderFront:"), 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only the background. A window has a colour of its own — what shows through wherever no view
    /// paints — and that is exactly what a form's <c>BackColor</c> means here. A foreground has
    /// nothing to apply to: a window draws no text, and the ambient colour reaches the children that
    /// do through the core rather than through this call.
    /// </remarks>
    public void SetColors(Color foreColor, Color backColor)
    {
        if (_window != 0 && CocoaRuntime.NSColorOf(backColor) is var back and not 0)
            CocoaRuntime.SendVoid(_window, CocoaRuntime.sel_registerName("setBackgroundColor:"), back);
    }

    // --- Not applicable to a window on this desktop ----------------------------------------------
    //
    // There is no disabled window here. Windows is asked to grey one out while a modal dialog is up;
    // AppKit runs a modal session instead, which withholds events from every other window of the
    // application without any of them being told. And a window has no font: the caption is drawn by
    // the desktop in the desktop's own, which is the point of it looking like every other window.

    public void SetEnabled(bool enabled) { }

    public void SetFont(Font font) { }

    /// <summary>
    /// Puts a child's view into the window's content view, which is what makes it appear at all.
    /// </summary>
    public void AddChild(IControlPeer child)
    {
        if (_window == 0 || ViewOf(child) is not { } view)
            return;

        var content = CocoaRuntime.SendPointer(_window, CocoaRuntime.sel_registerName("contentView"));
        if (content != 0)
            CocoaRuntime.SendVoid(content, CocoaRuntime.sel_registerName("addSubview:"), view);
    }

    /// <inheritdoc/>
    public void RemoveChild(IControlPeer child)
    {
        if (ViewOf(child) is { } view)
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("removeFromSuperview"));
    }

    /// <summary>The AppKit object behind a peer, whichever kind of peer it is.</summary>
    private static nint? ViewOf(IControlPeer child)
        => child switch
        {
            CocoaCanvasPeer canvas when canvas.Handle != 0 => canvas.Handle,
            CocoaControlPeer control when control.Handle != 0 => control.Handle,
            _ => null,
        };

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The owner is not disabled, and there is nothing missing in that: Windows greys the owner out
    /// while a dialog is up, where AppKit withholds events from every other window of the application
    /// through the modal session itself, without any of them being told. Passing the owner in would
    /// buy nothing here — the session is against this window, and every other one is behind it by
    /// consequence rather than by instruction.
    /// </para>
    /// <para>
    /// The window is shown first and the session only entered if it is still open, because a form
    /// whose <c>Load</c> closed it again has nothing to block on: entering a session for a window that
    /// is already gone is how a dialog turns into a hang.
    /// </para>
    /// </remarks>
    public void RunModal(IWindowPeer? owner)
    {
        this.Show();
        if (!_closed)
            _backend.RunModal(this);
    }

    /// <summary>Whether the window has been closed, which is one of the things that ends a modal loop.</summary>
    internal bool IsClosed => _closed;

    /// <inheritdoc/>
    /// <remarks>Greyed rather than removed, which is what this desktop does: the traffic lights are
    /// three, always in that order, and a window missing one of them reads as broken rather than as
    /// restricted.</remarks>
    public void SetMinimizeBox(bool visible)
    {
        _minimizeBox = visible;
        this.ApplyCaptionButtons();
    }

    /// <inheritdoc cref="SetMinimizeBox"/>
    public void SetMaximizeBox(bool visible)
    {
        _maximizeBox = visible;
        this.ApplyCaptionButtons();
    }

    /// <summary>Pushes both caption flags onto the window's own buttons.</summary>
    private void ApplyCaptionButtons()
    {
        SetButtonEnabled(_window, _MiniaturizeButton, _minimizeBox);
        SetButtonEnabled(_window, _ZoomButton, _maximizeBox);
    }

    /// <summary>
    /// Enables or greys one of the caption's standard buttons, if the window has one to give.
    /// </summary>
    /// <remarks>A borderless window answers nothing for any of them, so the absence is expected rather
    /// than a failure.</remarks>
    private static void SetButtonEnabled(nint window, nint which, bool enabled)
    {
        var button = window == 0
            ? 0
            : CocoaRuntime.SendPointer(window, CocoaRuntime.sel_registerName("standardWindowButton:"), which);

        if (button != 0)
            CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setEnabled:"), enabled);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// AppKit's limits are on the window's frame rather than on its content, which is the measurement
    /// the toolkit states its bounds in here too — so the number a caller gives is the number the user
    /// drags against, chrome included, exactly as on the other two platforms. A zero component lifts
    /// the limit on that axis: zero is already AppKit's own minimum, and the maximum goes back to the
    /// enormous value it starts at rather than to zero, which would pin the window shut.
    /// </remarks>
    public void SetSizeLimits(Size minimum, Size maximum)
    {
        if (_window == 0)
            return;

        CocoaRuntime.SendVoid(
            _window,
            CocoaRuntime.sel_registerName("setMinSize:"),
            new CocoaRuntime.CGSize(Math.Max(0, minimum.Width), Math.Max(0, minimum.Height)));

        CocoaRuntime.SendVoid(
            _window,
            CocoaRuntime.sel_registerName("setMaxSize:"),
            new CocoaRuntime.CGSize(
                maximum.Width > 0 ? maximum.Width : _Unbounded,
                maximum.Height > 0 ? maximum.Height : _Unbounded));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Refused rather than approximated. A window has no icon on this desktop: the caption shows a
    /// proxy icon only for a window that stands for a file on disk, and the only icon a running process
    /// can set is the application's own in the Dock — one per process, where this property is one per
    /// window, so a second form would silently replace the first form's. An application that set a
    /// per-window icon and got a per-process one would be worse off than one that got nothing, because
    /// nothing is visible in this page and a wrong Dock icon is not.
    /// </remarks>
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
