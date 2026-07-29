using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The macOS (Cocoa/AppKit) backend. This is currently a wired-but-unimplemented placeholder: it
/// reports support on macOS and fails with an explicit, actionable message rather than pretending to
/// draw. Implementing it — <c>NSApplication</c>, <c>NSWindow</c>, <c>NSButton</c>, <c>NSTextField</c>
/// via <c>objc_msgSend</c> P/Invoke — is tracked in <c>docs/PRD.md</c>.
/// </summary>
public sealed class CocoaBackend : IPlatformBackend
{
    private const string _NotImplemented =
        "The NativeForms Cocoa (macOS) backend is not implemented yet — see docs/PRD.md for status. "
        + "Until then, run on Windows (Win32) or Linux (GTK).";

    /// <inheritdoc/>
    public string Name => "Cocoa";

    /// <inheritdoc/>
    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <inheritdoc/>
    public ITheme Theme => DefaultTheme.Instance;

    /// <inheritdoc/>
    /// <remarks>Never raised: the placeholder serves the static fallback theme only.</remarks>
    public event EventHandler? ThemeChanged { add { } remove { } }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>[[NSScreen mainScreen] backingScaleFactor]</c>: 2.0 on a Retina display, 1.0 otherwise. Also
    /// the first thing in this backend to go through Objective-C messaging, which makes it the check
    /// that the messaging layer works on a real machine — a wrong <c>objc_msgSend</c> signature does not
    /// fail, it returns plausible nonsense, so the first use of it wants to be something whose right
    /// answer is known.
    /// </remarks>
    public double GetDpiScale()
    {
        if (!CocoaRuntime.Available)
            return 1.0;

        var screen = CocoaRuntime.SendToClass("NSScreen", "mainScreen");
        if (screen == 0)
            return 1.0;

        var scale = CocoaRuntime.SendDouble(screen, CocoaRuntime.sel_registerName("backingScaleFactor"));
        return scale is > 0 and < 16 ? scale : 1.0;
    }

    /// <inheritdoc/>
    public IWindowPeer CreateWindow() => new CocoaWindowPeer();

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IPopupPeer CreatePopup(IWindowPeer? owner) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => new CocoaImage(width, height, argb);

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// A managed timer that hands its tick back through <see cref="Post"/>, so the callback runs on the
    /// UI thread with everything else the loop drains. An <c>NSTimer</c> would want a target object with
    /// an Objective-C method on it — a class built at run time purely to receive one message — which is
    /// more machinery than a queue drain for the same guarantee.
    /// </remarks>
    public ITimerPeer CreateTimer() => new CocoaTimerPeer(this);

    /// <inheritdoc/>
    public INotifyIconPeer CreateNotifyIcon() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Size GetScreenSize()
    {
        var display = CocoaNative.CGMainDisplayID();
        return new((int)CocoaNative.CGDisplayPixelsWide(display), (int)CocoaNative.CGDisplayPixelsHigh(display));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CoreText, so the measurement is the one the platform would lay the text out with rather than a
    /// guess from character counts. A refusal falls back to the shared metric estimate instead of
    /// reporting zero, which would quietly collapse every layout that measures.
    /// </remarks>
    public Size MeasureText(string text, Font font)
    {
        if (text.Length == 0)
            return Size.Empty;

        return CocoaNative.TryMeasure(text, font.Family, font.SizeInPoints, out var width, out var height)
            ? new((int)Math.Ceiling(width), (int)Math.Ceiling(height))
            : new((int)Math.Ceiling(text.Length * font.SizeInPoints * 0.6), (int)Math.Ceiling(font.SizeInPoints * 1.3));
    }

    /// <inheritdoc/>
    public IButtonPeer CreateButton() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ITextBoxPeer CreateTextBox() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
        => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public string[]? ShowFileDialog(in FileDialogOptions options) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Color? ShowColorDialog(Color color) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Font? ShowFontDialog(Font font) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IRichTextBoxPeer CreateRichTextBox() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void SetClipboardText(string text) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public string? GetClipboardText() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    /// <summary>Work queued from any thread, drained by <see cref="Run"/> on the UI thread.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _posted = new();

    /// <inheritdoc/>
    /// <remarks>
    /// A queue the loop drains rather than <c>dispatch_async</c> onto the main queue. Both work; this
    /// one keeps the ordering guarantee in managed code where it can be reasoned about, and needs no
    /// block trampoline — a block is an Objective-C object with a calling convention, which is exactly
    /// the sort of thing §2's rules exist to keep out.
    /// </remarks>
    public void Post(Action action) => _posted.Enqueue(action);

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The application has to be told it is a real one before a window will take the keyboard or appear
    /// in the Dock: a process launched from a terminal is <c>NSApplicationActivationPolicyProhibited</c>
    /// until <c>setActivationPolicy:</c> says otherwise, and a window shown before that is a window
    /// nobody can click.
    /// </para>
    /// <para>
    /// The loop pulls one event at a time with <c>nextEventMatchingMask:untilDate:inMode:dequeue:</c>
    /// rather than calling <c>[NSApp run]</c>, because the queue posted from other threads has to be
    /// drained between events and <c>run</c> never comes back to let that happen.
    /// </para>
    /// </remarks>
    public void Run(IWindowPeer mainWindow)
    {
        var app = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        if (app == 0)
            return;

        // NSApplicationActivationPolicyRegular
        CocoaRuntime.SendVoid(app, CocoaRuntime.sel_registerName("setActivationPolicy:"), 0);
        CocoaRuntime.SendVoid(app, CocoaRuntime.sel_registerName("finishLaunching"));
        CocoaRuntime.SendVoid(app, CocoaRuntime.sel_registerName("activateIgnoringOtherApps:"), true);
        mainWindow.Show();

        _running = true;
        var nextEvent = CocoaRuntime.sel_registerName("nextEventMatchingMask:untilDate:inMode:dequeue:");
        var sendEvent = CocoaRuntime.sel_registerName("sendEvent:");
        var mode = CocoaRuntime.NSString("kCFRunLoopDefaultMode");
        var distantPast = CocoaRuntime.SendToClass("NSDate", "distantPast");

        while (_running)
        {
            while (_posted.TryDequeue(out var action))
                action();

            var next = CocoaRuntime.SendEvent(app, nextEvent, unchecked((nint)ulong.MaxValue), distantPast, mode, true);
            if (next == 0)
            {
                // Nothing waiting: yield rather than spin, so an idle application is not a busy one.
                System.Threading.Thread.Sleep(5);
                continue;
            }

            CocoaRuntime.SendVoid(app, sendEvent, next);
        }

        if (mode != 0)
            CocoaNative.CFRelease(mode);
    }

    private volatile bool _running;

    /// <inheritdoc/>
    /// <inheritdoc/>
    public void Quit() => _running = false;
}
