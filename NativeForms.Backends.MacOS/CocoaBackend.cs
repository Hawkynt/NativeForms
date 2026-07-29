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
    public IWindowPeer CreateWindow() => new CocoaWindowPeer(this);

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => new CocoaCanvasPeer();

    /// <inheritdoc/>
    public IPopupPeer CreatePopup(IWindowPeer? owner) => new CocoaPopupPeer();

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
    /// <remarks>An <c>NSStatusItem</c> eventually; inert for now so a tray icon nobody can see does not
    /// take the application down.</remarks>
    public INotifyIconPeer CreateNotifyIcon() => new CocoaNotifyIconPeer();

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
    public IButtonPeer CreateButton() => new CocoaButtonPeer();

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => new CocoaLabelPeer();

    /// <inheritdoc/>
    public ITextBoxPeer CreateTextBox() => new CocoaTextBoxPeer();

    /// <inheritdoc/>
    /// <remarks>
    /// The promotions PRD §12 asks for that this backend can serve. A backend opts in by overriding —
    /// the factories are default-null interface methods — and offering one it cannot serve faithfully
    /// would be worse than declining, because the core would stop drawing the control that does work.
    /// The rest still decline: a combo box, a list box, a scroller, a slider and a hyperlink each carry
    /// state AppKit's nearest object does not hold, and a half-answer would show.
    /// </remarks>
    public ICheckBoxPeer CreateCheckBox() => new CocoaCheckBoxPeer();

    /// <inheritdoc/>
    public IRadioButtonPeer CreateRadioButton() => new CocoaRadioButtonPeer();

    /// <inheritdoc/>
    public IProgressBarPeer CreateProgressBar() => new CocoaProgressBarPeer();

    /// <inheritdoc/>
    public IGroupBoxPeer CreateGroupBox() => new CocoaGroupBoxPeer();

    /// <inheritdoc/>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
    {
        _ = owner;
        var alert = CocoaRuntime.Allocate("NSAlert");
        if (alert != 0)
            alert = CocoaRuntime.SendPointer(alert, CocoaRuntime.sel_registerName("init"));

        if (alert == 0)
            return buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Cancel;

        try
        {
            SetString(alert, "setMessageText:", caption.Length > 0 ? caption : text);
            if (caption.Length > 0)
                SetString(alert, "setInformativeText:", text);

            // NSAlertStyle: warning 0, informational 1, critical 2. Cocoa has no question style —
            // a dialog that asks something is informational there, and inventing an icon for it would
            // be less native, not more.
            CocoaRuntime.SendVoid(
                alert,
                CocoaRuntime.sel_registerName("setAlertStyle:"),
                icon switch { MessageBoxIcon.Error => 2, MessageBoxIcon.Warning => 0, _ => 1 });

            // Buttons are added in the platform's order: the default action first, which AppKit puts
            // at the right. The result is mapped by position rather than by comparing captions, so it
            // keeps working when these strings are localized.
            (string Title, DialogResult Result)[] choices = buttons switch
            {
                MessageBoxButtons.OKCancel => [("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel)],
                MessageBoxButtons.YesNo => [("Yes", DialogResult.Yes), ("No", DialogResult.No)],
                MessageBoxButtons.YesNoCancel =>
                    [("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel)],
                MessageBoxButtons.RetryCancel => [("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel)],
                MessageBoxButtons.AbortRetryIgnore =>
                    [("Abort", DialogResult.Abort), ("Retry", DialogResult.Retry), ("Ignore", DialogResult.Ignore)],
                _ => [("OK", DialogResult.OK)],
            };

            foreach (var choice in choices)
                SetString(alert, "addButtonWithTitle:", choice.Title);

            // runModal answers NSAlertFirstButtonReturn (1000) plus the button's index.
            var chosen = (int)(CocoaRuntime.SendInteger(alert, CocoaRuntime.sel_registerName("runModal")) - 1000);
            return (uint)chosen < (uint)choices.Length ? choices[chosen].Result : DialogResult.Cancel;
        }
        finally
        {
            CocoaRuntime.SendVoid(alert, CocoaRuntime.sel_registerName("release"));
        }
    }

    /// <summary>The file-system path behind an <c>NSURL</c>, or null.</summary>
    private static string? PathOf(nint url)
    {
        if (url == 0)
            return null;

        var path = CocoaRuntime.SendPointer(url, CocoaRuntime.sel_registerName("path"));
        var text = path == 0 ? null : CocoaNative.ReadString(path);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Sends a message taking one string, releasing the string afterwards.</summary>
    private static void SetString(nint target, string selector, string value)
    {
        var text = CocoaRuntime.NSString(value);
        if (text == 0)
            return;

        CocoaRuntime.SendVoid(target, CocoaRuntime.sel_registerName(selector), text);
        CocoaNative.CFRelease(text);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>NSOpenPanel</c> for opening and choosing a folder, <c>NSSavePanel</c> for saving — the same
    /// object family the rest of the platform uses, so the sidebar, tags, recents and every keyboard
    /// habit come for free rather than being reimplemented badly.
    /// </remarks>
    public string[]? ShowFileDialog(in FileDialogOptions options)
    {
        var saving = options.Kind == FileDialogKind.Save;
        var panel = CocoaRuntime.SendToClass(saving ? "NSSavePanel" : "NSOpenPanel", saving ? "savePanel" : "openPanel");
        if (panel == 0)
            return null;

        if (options.Title.Length > 0)
            SetString(panel, "setMessage:", options.Title);

        if (options.FileName.Length > 0)
            SetString(panel, "setNameFieldStringValue:", options.FileName);

        if (!saving)
        {
            var folders = options.Kind == FileDialogKind.SelectFolder;
            CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("setCanChooseFiles:"), !folders);
            CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("setCanChooseDirectories:"), folders);
            CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("setAllowsMultipleSelection:"), options.Multiselect);
        }

        // NSModalResponseOK
        if (CocoaRuntime.SendInteger(panel, CocoaRuntime.sel_registerName("runModal")) != 1)
            return null;

        // An open panel answers with every URL chosen; a save panel has exactly one.
        if (saving || !options.Multiselect)
            return PathOf(CocoaRuntime.SendPointer(panel, CocoaRuntime.sel_registerName("URL"))) is { } single
                ? [single]
                : null;

        var urls = CocoaRuntime.SendPointer(panel, CocoaRuntime.sel_registerName("URLs"));
        if (urls == 0)
            return null;

        var count = (int)CocoaRuntime.SendInteger(urls, CocoaRuntime.sel_registerName("count"));
        var chosen = new List<string>(count);
        for (var i = 0; i < count; ++i)
            if (PathOf(CocoaRuntime.SendIndex(urls, CocoaRuntime.sel_registerName("objectAtIndex:"), i)) is { } path)
                chosen.Add(path);

        return chosen.Count > 0 ? [.. chosen] : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>NSColorPanel</c> is a shared, modeless panel — the platform keeps one and shows it — so it
    /// has no modal answer to wait for. Running it properly means opening it and reporting changes as
    /// they happen, which is a different shape from the blocking call this seam offers; until that is
    /// wired the panel is not shown at all rather than shown and ignored.
    /// </remarks>
    public Color? ShowColorDialog(Color color) => null;

    /// <inheritdoc/>
    public Font? ShowFontDialog(Font font) => null;

    /// <inheritdoc/>
    public IRichTextBoxPeer CreateRichTextBox() => new CocoaRichTextBoxPeer();

    /// <inheritdoc/>
    /// <remarks><c>NSPasteboard</c>: cleared, then the string written under the plain-text type.</remarks>
    public void SetClipboardText(string text)
    {
        var board = CocoaRuntime.SendToClass("NSPasteboard", "generalPasteboard");
        if (board == 0)
            return;

        CocoaRuntime.SendPointer(board, CocoaRuntime.sel_registerName("clearContents"));
        var value = CocoaRuntime.NSString(text);
        var type = CocoaRuntime.NSString("public.utf8-plain-text");
        if (value != 0 && type != 0)
            CocoaRuntime.SendVoid(board, CocoaRuntime.sel_registerName("setString:forType:"), value, type);

        if (value != 0)
            CocoaNative.CFRelease(value);
        if (type != 0)
            CocoaNative.CFRelease(type);
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public string? GetClipboardText()
    {
        var board = CocoaRuntime.SendToClass("NSPasteboard", "generalPasteboard");
        if (board == 0)
            return null;

        var type = CocoaRuntime.NSString("public.utf8-plain-text");
        if (type == 0)
            return null;

        try
        {
            var value = CocoaRuntime.SendPointer(board, CocoaRuntime.sel_registerName("stringForType:"), type);
            return value == 0 ? null : CocoaNative.ReadString(value);
        }
        finally
        {
            CocoaNative.CFRelease(type);
        }
    }

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

            // The open popups get first refusal. A press outside the deepest one closes it and is
            // swallowed, which is what a pointer grab would do on the platforms that take one.
            if (!CocoaPopupPeer.Intercept(next))
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
