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

    private ITheme? _theme;

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the desktop on first ask and kept, which is what a theme being an immutable snapshot
    /// means. The first ask comes with the first control, by which point <c>NSApplication</c> exists
    /// and the semantic colours have an appearance to resolve against. The snapshot is dropped when the
    /// appearance changes, so the next ask reads the palette the user just switched to.
    /// </remarks>
    public ITheme Theme => _theme ??= new CocoaTheme();

    /// <summary>The KVO observer watching the application's appearance, or zero while none is installed.</summary>
    private nint _appearance;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A desktop switched into dark mode while the application is running changes <c>NSApp</c>'s
    /// <c>effectiveAppearance</c>, which is a property rather than a notification — so the observation is
    /// KVO, through the run-time class in <see cref="CocoaAppearanceObserver"/>.
    /// </para>
    /// <para>
    /// It is installed on the first subscriber rather than in the constructor, because the property
    /// belongs to <c>NSApplication</c> and the backend is built before there is one. A failed attempt
    /// leaves nothing behind and is simply made again by the next subscriber, which is every owner-drawn
    /// control as it realizes — so the observation arms itself as soon as there is an application to
    /// watch, without anything having to know when that was.
    /// </para>
    /// </remarks>
    public event EventHandler? ThemeChanged
    {
        add
        {
            if (_appearance == 0)
                _appearance = CocoaAppearanceObserver.Observe(this.OnAppearanceChanged);

            _themeChanged += value;
        }

        remove => _themeChanged -= value;
    }

    private EventHandler? _themeChanged;

    /// <summary>
    /// The appearance changed: throw the snapshot away and tell everything painting from it.
    /// </summary>
    /// <remarks>
    /// In that order, because a handler's first move is to repaint and a repaint reads
    /// <see cref="Theme"/> — so the fresh palette has to be what the next ask builds, not the one the
    /// event was raised against.
    /// </remarks>
    private void OnAppearanceChanged()
    {
        _theme = null;
        _themeChanged?.Invoke(this, EventArgs.Empty);
    }

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
    /// <remarks>
    /// An <c>NSStatusItem</c> in the shared status bar — this desktop's menu bar is where Windows has a
    /// notification area, and it is the only tray surface macOS has.
    /// </remarks>
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

        return CocoaNative.TryMeasure(text, font, out var width, out var height)
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
    /// All nine are now served. The last three needed arithmetic rather than wiring — a scroller holds a
    /// fraction where the toolkit holds a range, and a slider has no step model at all — so what each
    /// cannot express is refused in the peer and written down in <c>docs/backends.md</c> rather than
    /// approximated.
    /// </remarks>
    public ICheckBoxPeer CreateCheckBox() => new CocoaCheckBoxPeer();

    /// <inheritdoc/>
    public IRadioButtonPeer CreateRadioButton() => new CocoaRadioButtonPeer();

    /// <inheritdoc/>
    public IProgressBarPeer CreateProgressBar() => new CocoaProgressBarPeer();

    /// <inheritdoc/>
    public IGroupBoxPeer CreateGroupBox() => new CocoaGroupBoxPeer();

    /// <inheritdoc/>
    public IListBoxPeer CreateListBox() => new CocoaListBoxPeer();

    /// <inheritdoc/>
    public ILinkLabelPeer CreateLinkLabel() => new CocoaLinkLabelPeer();

    /// <inheritdoc/>
    public IComboBoxPeer CreateComboBox() => new CocoaComboBoxPeer();

    /// <inheritdoc/>
    public IScrollBarPeer CreateScrollBar(bool vertical) => new CocoaScrollBarPeer(vertical);

    /// <inheritdoc/>
    public ITrackBarPeer CreateTrackBar(bool vertical) => new CocoaTrackBarPeer(vertical);

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
    /// <para>
    /// <c>NSColorPanel</c> is a shared, modeless panel: the platform keeps exactly one, shows it, and
    /// has no notion of being dismissed with an answer. What makes it fit this seam's blocking shape is
    /// a modal *session* — <c>beginModalSessionForWindow:</c> and <c>runModalSession:</c> — pumped
    /// until the panel is no longer on screen. <c>runModalForWindow:</c> would be the obvious call and
    /// the wrong one: it ends when something calls <c>stopModal</c>, and nothing on this panel ever
    /// does, because it has no button that means "done".
    /// </para>
    /// <para>
    /// Which is also why cancellation is inferred rather than reported. The panel has no OK and no
    /// Cancel, only a close box, so what is answered is the colour if the user changed it and nothing
    /// if they did not — the same outcome either reading would produce for any caller, since a dialog
    /// that hands back exactly what it was given has not changed anything.
    /// </para>
    /// </remarks>
    public Color? ShowColorDialog(Color color)
    {
        var panel = CocoaRuntime.SendToClass("NSColorPanel", "sharedColorPanel");
        if (panel == 0)
            return null;

        var colours = CocoaRuntime.objc_getClass("NSColor");
        var initial = colours == 0
            ? 0
            : CocoaRuntime.SendColor(
                colours,
                CocoaRuntime.sel_registerName("colorWithSRGBRed:green:blue:alpha:"),
                color.R / 255.0,
                color.G / 255.0,
                color.B / 255.0,
                1.0);

        // No alpha: the other two platforms' colour dialogs have none, and a channel one backend can
        // answer and two cannot is a difference an application would have to code around.
        CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("setShowsAlpha:"), false);
        if (initial != 0)
            CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("setColor:"), initial);

        if (!RunPanel(panel))
            return null;

        var chosen = ReadColor(CocoaRuntime.SendPointer(panel, CocoaRuntime.sel_registerName("color")));
        return chosen is { } picked && picked != color ? picked : null;
    }

    /// <summary>An <c>NSColor</c> as the toolkit's, converted to sRGB first, or null.</summary>
    /// <remarks>
    /// The conversion is not optional. A colour picked from the panel's crayons or its spectrum is in
    /// whatever space that picker works in, and asking such a colour for its red component does not
    /// convert it — it raises, because the component is not one it has.
    /// </remarks>
    private static Color? ReadColor(nint color)
    {
        if (color == 0)
            return null;

        var space = CocoaRuntime.SendToClass("NSColorSpace", "sRGBColorSpace");
        var converted = space == 0
            ? 0
            : CocoaRuntime.SendPointer(color, CocoaRuntime.sel_registerName("colorUsingColorSpace:"), space);

        if (converted == 0)
            return null;

        var red = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("redComponent"));
        var green = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("greenComponent"));
        var blue = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("blueComponent"));
        return Color.FromArgb(Channel(red), Channel(green), Channel(blue));
    }

    /// <summary>One component of a colour, as a byte.</summary>
    private static int Channel(double component) => (int)Math.Clamp(Math.Round(component * 255), 0, 255);

    /// <summary>
    /// Shows a shared panel and pumps events until the user closes it, answering whether it ever
    /// appeared.
    /// </summary>
    /// <remarks>
    /// The loop's exit condition is the panel's own visibility rather than the session's return value
    /// alone, because a panel with no "done" button never stops the session. A panel that refuses to
    /// appear ends the wait at once — a call that blocked forever on a window nobody can see is the one
    /// failure worse than answering as if cancelled, which is what this seam did before.
    /// </remarks>
    private static bool RunPanel(nint panel)
    {
        var app = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        if (app == 0)
            return false;

        CocoaRuntime.SendVoid(panel, CocoaRuntime.sel_registerName("makeKeyAndOrderFront:"), 0);
        if (!CocoaRuntime.SendBool(panel, CocoaRuntime.sel_registerName("isVisible")))
            return false;

        var session = CocoaRuntime.SendPointer(app, CocoaRuntime.sel_registerName("beginModalSessionForWindow:"), panel);
        if (session == 0)
            return false;

        try
        {
            // NSModalResponseContinue
            const nint running = -1002;
            var run = CocoaRuntime.sel_registerName("runModalSession:");
            var visible = CocoaRuntime.sel_registerName("isVisible");

            while (CocoaRuntime.SendInteger(app, run, session) == running
                && CocoaRuntime.SendBool(panel, visible))
                System.Threading.Thread.Sleep(5); // the session returns at once when the queue is empty
        }
        finally
        {
            CocoaRuntime.SendVoid(app, CocoaRuntime.sel_registerName("endModalSession:"), session);
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The font panel is run the same way, and read a different way. It reports nothing directly:
    /// changing a setting sends <c>changeFont:</c> up the responder chain, and what that handler is
    /// expected to do is ask <c>NSFontManager</c> to convert its current font. So this asks the same
    /// question once, at the end — <c>convertFont:</c> applies whatever the user did to the font that
    /// was handed in, which needs no target object and no responder of our own.
    /// </remarks>
    public Font? ShowFontDialog(Font font)
    {
        var manager = CocoaRuntime.SendToClass("NSFontManager", "sharedFontManager");
        var initial = CocoaRuntime.NSFontOf(font);
        if (manager == 0 || initial == 0)
            return null;

        CocoaRuntime.SendVoid(manager, CocoaRuntime.sel_registerName("setSelectedFont:isMultiple:"), initial, false);

        // fontPanel:YES creates the panel if there is not one yet.
        var panel = CocoaRuntime.SendBoolArgument(manager, CocoaRuntime.sel_registerName("fontPanel:"), true);
        if (panel == 0 || !RunPanel(panel))
            return null;

        var chosen = CocoaRuntime.SendPointer(manager, CocoaRuntime.sel_registerName("convertFont:"), initial);
        if (chosen == 0 || CocoaRuntime.SendBool(chosen, CocoaRuntime.sel_registerName("isEqual:"), initial))
            return null;

        var family = CocoaRuntime.SendPointer(chosen, CocoaRuntime.sel_registerName("familyName"));
        var name = family == 0 ? font.Family : CocoaNative.ReadString(family);
        var size = CocoaRuntime.SendDouble(chosen, CocoaRuntime.sel_registerName("pointSize"));

        // NSFontTraitMask: italic is bit 0, bold bit 1 — the same pair the rich text box converts with.
        var traits = CocoaRuntime.SendPointer(manager, CocoaRuntime.sel_registerName("traitsOfFont:"), chosen);
        var style = FontStyle.Regular;
        if ((traits & 2) != 0)
            style |= FontStyle.Bold;
        if ((traits & 1) != 0)
            style |= FontStyle.Italic;

        return new(
            name.Length > 0 ? name : font.Family,
            size > 0 ? (float)size : font.SizeInPoints,
            style);
    }

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

            if (!Intercept(next))
                CocoaRuntime.SendVoid(app, sendEvent, next);
        }

        if (mode != 0)
            CocoaNative.CFRelease(mode);
    }

    /// <summary>
    /// Offers an event to the toolkit before AppKit dispatches it, answering whether it was consumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The open popups get first refusal. A press outside the deepest one closes it and is swallowed,
    /// which is what a pointer grab would do on the platforms that take one. The text box gets second
    /// refusal, for the reason <see cref="CocoaTextBoxPeer.InterceptKey"/> gives: this is the only
    /// place on this platform that stands ahead of the editor's own handling, so a key the toolkit
    /// consumes is one that is never sent on.
    /// </para>
    /// <para>
    /// One method rather than a pair of calls at the site that made them, because there are two sites
    /// now: a modal session dispatches its own events, so <see cref="RunModal"/> has to make the same
    /// offer or a dialog becomes a place where popups do not close.
    /// </para>
    /// </remarks>
    private static bool Intercept(nint theEvent)
        => CocoaPopupPeer.Intercept(theEvent) || CocoaTextBoxPeer.InterceptKey(theEvent);

    private volatile bool _running;

    /// <summary>Whether something has asked the application to stop, which also ends any modal.</summary>
    /// <remarks>
    /// Separate from <see cref="_running"/> on purpose. That flag says the loop is turning, and it is
    /// false both before <see cref="Run"/> starts and after it ends — so a modal dialog reading it
    /// would close itself instantly when one is shown from an application that never called
    /// <see cref="Run"/> at all. This one only ever goes true, and only because somebody said so.
    /// </remarks>
    private volatile bool _quitting;

    /// <inheritdoc/>
    /// <inheritdoc/>
    public void Quit()
    {
        _quitting = true;
        _running = false;
    }

    /// <summary>
    /// Blocks on a window until it closes, withholding events from the rest of the application while
    /// it is up. This is what <see cref="IWindowPeer.RunModal"/> is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A modal <em>session</em> rather than <c>runModalForWindow:</c>, which is the obvious call and
    /// the wrong one twice over. It does not come back until something calls <c>stopModal</c>, and
    /// nothing here does — a form closes, it does not stop a modal it knows nothing about — and while
    /// it is inside, this loop is not: the queue <see cref="Post"/> fills would stop being drained, so
    /// every timer tick and every piece of cross-thread work an application does would stall for as
    /// long as the dialog was open. The session is the shape that lets the pumping stay here, which is
    /// the same reason the colour and font panels are run this way.
    /// </para>
    /// <para>
    /// Three things end it, and the first is the one that matters: the window is gone. A dialog closed
    /// with its own close box tells the toolkit nothing — there is no window delegate on this backend —
    /// so the flag the peer sets when the core closes it cannot be the only exit, or a user dismissing
    /// a dialog by its red button would hang the application. <c>isVisible</c> is asked as well, which
    /// covers both. Quitting ends it too, so a session cannot outlive the application that owns it.
    /// </para>
    /// <para>
    /// The session dispatches its own events, which used to mean this loop never saw them: the two
    /// interceptions <see cref="Run"/> makes — light dismiss and the text box's key seam — did not run
    /// inside a dialog, so a toolkit popup opened from one stayed up because the press that should
    /// have closed it went straight to whatever was underneath. The queue is therefore looked at
    /// before each turn of the session and <em>not</em> taken from: whatever
    /// <see cref="Intercept"/> consumes is then removed, and everything the toolkit does not want is
    /// left exactly where the session expects to find it. That keeps modality AppKit's — the
    /// alternatives were re-implementing it by hand or making every popup a child window of the dialog
    /// — while there is still only one place the toolkit stands ahead of the platform.
    /// </para>
    /// </remarks>
    internal void RunModal(CocoaWindowPeer peer)
    {
        var app = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        var visible = CocoaRuntime.sel_registerName("isVisible");
        var window = peer.Handle;
        if (app == 0 || window == 0 || !CocoaRuntime.SendBool(window, visible))
            return; // a window nobody can see is not something to block on

        var session = CocoaRuntime.SendPointer(app, CocoaRuntime.sel_registerName("beginModalSessionForWindow:"), window);
        if (session == 0)
            return;

        var mode = CocoaRuntime.NSString("kCFRunLoopDefaultMode");

        try
        {
            // NSModalResponseContinue
            const nint running = -1002;
            var run = CocoaRuntime.sel_registerName("runModalSession:");

            while (!_quitting && !peer.IsClosed)
            {
                // Ahead of the session's turn, so a press the toolkit wants is gone before the session
                // can dispatch it to whatever sits behind the popup it should have closed.
                InterceptPending(app, mode);

                if (CocoaRuntime.SendInteger(app, run, session) != running
                    || !CocoaRuntime.SendBool(window, visible))
                    break;

                // The reason this is a session at all. A dialog is not a pause in the application:
                // its timers still tick and its background work still comes home, and both arrive
                // through this queue.
                while (_posted.TryDequeue(out var action))
                    action();

                System.Threading.Thread.Sleep(5); // the session returns at once when the queue is empty
            }
        }
        finally
        {
            CocoaRuntime.SendVoid(app, CocoaRuntime.sel_registerName("endModalSession:"), session);
            if (mode != 0)
                CocoaNative.CFRelease(mode);
        }
    }

    /// <summary>
    /// Offers whatever is at the head of the queue to <see cref="Intercept"/>, taking out only what
    /// the toolkit consumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Peeked rather than dequeued, which is the whole of the design. Taking an event out and putting
    /// the unwanted ones back would reorder the queue and hand this method AppKit's job of deciding
    /// which window may have them; leaving them alone means the session still sees the same queue it
    /// always did, in the same order, and the only events that vanish are the ones the toolkit
    /// swallowed on purpose.
    /// </para>
    /// <para>
    /// The mask is what makes looking at the head of the queue enough. Asking for any event at all
    /// answers with whatever happens to be first, and the first thing before a press is the pointer
    /// moving to where it is about to press — so a peek that stopped there never saw the press behind
    /// it, and the session dispatched both. Asking only for the four event types the toolkit can
    /// consume skips past everything else without disturbing it, which is the same thing an
    /// <c>NSButton</c>'s own tracking loop does when it waits for a release.
    /// </para>
    /// </remarks>
    private static void InterceptPending(nint app, nint mode)
    {
        var next = CocoaRuntime.sel_registerName("nextEventMatchingMask:untilDate:inMode:dequeue:");
        var distantPast = CocoaRuntime.SendToClass("NSDate", "distantPast");

        for (var i = 0; i < 16; ++i)
        {
            var pending = CocoaRuntime.SendEvent(app, next, _Interceptable, distantPast, mode, false);
            if (pending == 0 || !Intercept(pending))
                break;

            CocoaRuntime.SendEvent(app, next, _Interceptable, distantPast, mode, true);
        }
    }

    /// <summary>
    /// The events <see cref="Intercept"/> can consume, as an <c>NSEventMask</c>: a left, right or
    /// other mouse press, and a key going down.
    /// </summary>
    /// <remarks>A mask bit is one shifted by the event's own type, which is how AppKit spells this.</remarks>
    private const nint _Interceptable = (1 << 1) | (1 << 3) | (1 << 10) | (1 << 25);
}
