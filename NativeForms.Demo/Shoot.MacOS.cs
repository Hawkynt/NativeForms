using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Writes a PNG of the gallery's window on macOS by asking AppKit to cache the view's drawing into a
/// bitmap.
/// </summary>
/// <remarks>
/// <para>
/// In-process for the reason the other two capture paths give: a CI runner has no desktop session to
/// point a screenshot tool at. <c>cacheDisplayInRect:toBitmapImageRep:</c> asks the view to draw
/// itself, which is the AppKit equivalent of <c>gtk_widget_draw</c> and, like it, sidesteps the
/// display server entirely — so what lands in the file is what the toolkit painted rather than
/// whatever was stacked on a desktop.
/// </para>
/// <para>
/// The rep can encode itself as PNG, so nothing here writes an image format. That is worth taking:
/// AppKit's encoder is right about colour space and premultiplication in a way a hand-rolled writer
/// would have to be taught.
/// </para>
/// </remarks>
internal static unsafe partial class ShootMacOS
{
    private const string _ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSBitmapImageFileTypePNG.</summary>
    private const nint _Png = 4;

    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    /// <summary>The class an object actually is, which is how a promotion is read back off a live tree.</summary>
    [LibraryImport(_ObjC)]
    private static partial nint object_getClass(nint instance);

    /// <summary>A class's name as a C string; read manually rather than marshalled, as every import here is.</summary>
    [LibraryImport(_ObjC)]
    private static partial nint class_getName(nint cls);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint first, nint second);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendRect(nint receiver, nint selector, Rect frame);

    /// <summary>Sends a message taking one measure and answering an object, such as an item of a length.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendLength(nint receiver, nint selector, double length);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendRect(nint receiver, nint selector, Rect frame, nint rep);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBool(nint receiver, nint selector);

    /// <summary>Asks a yes/no question taking one object, such as whether a view answers a selector.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBool(nint receiver, nint selector, nint argument);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public double X, Y, Width, Height;
    }

    /// <summary>
    /// Why the last capture produced nothing, so a failure in the artifact names the step that gave
    /// up rather than only the fact that one did.
    /// </summary>
    public static string? Diagnosis { get; private set; }

    /// <summary>Captures the application's gallery window, returning the size written or null.</summary>
    public static Size? Window(string path)
    {
        Diagnosis = null;
        var application = objc_getClass("NSApplication");
        if (application == 0)
            return Failed("AppKit is not loaded in this process");

        var app = Send(application, sel_registerName("sharedApplication"));
        if (app == 0)
            return Failed("NSApplication has no shared instance");

        var window = Gallery(app);
        if (window == 0)
            return Failed("the application owns no window to photograph");

        var view = Send(window, sel_registerName("contentView"));
        if (view == 0)
            return Failed("the window has no content view");

        // The view's own bounds, so the shot is the client area rather than the framed window.
        var size = SizeFromFrame(view);
        if (size.Width <= 0 || size.Height <= 0)
            return Failed($"the content view measures {size.Width}x{size.Height}");

        var bounds = new Rect { X = 0, Y = 0, Width = size.Width, Height = size.Height };

        // The shutter fires from a queued tick, so a pending frame may not have rendered when the
        // capture asks for it — caching an undrawn view yields an empty rep. So the whole attempt
        // repeats, each round letting the run loop breathe and the view finish laying out first, and
        // the first one that produces bytes wins.
        for (var attempt = 0; attempt < 5; ++attempt)
        {
            ShootInputMac.Drain();
            Send(view, sel_registerName("layoutSubtreeIfNeeded"));
            Send(view, sel_registerName("display"));

            var rep = SendRect(view, sel_registerName("bitmapImageRepForCachingDisplayInRect:"), bounds);
            if (rep == 0)
            {
                Failed("the view declined to make a bitmap rep for its own bounds");
                continue;
            }

            SendRect(view, sel_registerName("cacheDisplayInRect:toBitmapImageRep:"), bounds, rep);

            var data = Send(rep, sel_registerName("representationUsingType:properties:"), _Png, EmptyDictionary());
            if (data == 0)
            {
                Failed("the bitmap rep produced no PNG representation");
                continue;
            }

            var bytes = Send(data, sel_registerName("bytes"));
            var length = (int)Send(data, sel_registerName("length"));
            if (bytes == 0 || length <= 0)
            {
                Failed($"the PNG representation is {length} byte(s) long");
                continue;
            }

            using (var file = File.Create(path))
                file.Write(new ReadOnlySpan<byte>((void*)bytes, length));

            Diagnosis = null;
            return size;
        }

        return null;
    }

    /// <summary>
    /// What the running window says about its hover wiring, read back off AppKit rather than assumed.
    /// </summary>
    /// <remarks>
    /// Injected input is posted here and dropped — the window server does not deliver a synthetic
    /// event from a process the runner never granted Accessibility to — so no check on this job can
    /// watch a real pointer move highlight a control. What it can do is ask AppKit whether the two
    /// things a moved event needs are in place: the window generating them at all, and a tracking
    /// area on each canvas so the view under the pointer is the one that hears them rather than
    /// whichever view happens to hold the keyboard. That is short of proof of delivery and says so.
    /// </remarks>
    public static string HoverWiring()
    {
        var app = objc_getClass("NSApplication") is var application && application != 0
            ? Send(application, sel_registerName("sharedApplication"))
            : 0;
        var window = app == 0 ? 0 : Gallery(app);
        if (window == 0)
            return "hover: no window to ask";

        var accepts = SendBool(window, sel_registerName("acceptsMouseMovedEvents"));
        var content = Send(window, sel_registerName("contentView"));
        var tracked = 0;
        var views = 0;
        CountTrackedViews(content, ref views, ref tracked);

        return $"hover: the window {(accepts ? "accepts" : "DROPS")} moved events, "
            + $"{tracked} of {views} view(s) carry a tracking area";
    }

    /// <summary>
    /// What the running window is actually built from: every view under the content view, counted by
    /// the Objective-C class it really is.
    /// </summary>
    /// <remarks>
    /// A promotion is the one claim in this backend that a screenshot cannot settle. An
    /// <c>NSTableView</c> and the owner-drawn list it replaces are meant to look alike — that is the
    /// point of drawing the twin from the platform's own theme — so the picture is the same either way
    /// and only the class name differs. Reading it back off the live tree is therefore the difference
    /// between "PRD §12 says this is promoted" and "this window holds one".
    /// </remarks>
    public static string NativeWidgets()
    {
        var app = objc_getClass("NSApplication") is var application && application != 0
            ? Send(application, sel_registerName("sharedApplication"))
            : 0;
        var window = app == 0 ? 0 : Gallery(app);
        if (window == 0)
            return "native widgets: no window to ask";

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        CountClasses(Send(window, sel_registerName("contentView")), counts);
        if (counts.Count == 0)
            return "native widgets: the window has no views";

        var listed = counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}x{entry.Value}");

        return "native widgets: " + string.Join(", ", listed);
    }

    /// <summary>
    /// How many views report their link clicks to the toolkit, read back off the running window.
    /// </summary>
    /// <remarks>
    /// The same limit the hover line carries, for the same reason: a click on a link cannot be
    /// delivered on this job — the window server drops the injected pointer for want of an
    /// Accessibility grant — so what can be shown is that the text view holds the delegate AppKit will
    /// send <c>textView:clickedOnLink:atIndex:</c> to, not that it has sent one. That is short of proof
    /// and says so.
    /// </remarks>
    public static string LinkWiring()
    {
        var app = objc_getClass("NSApplication") is var application && application != 0
            ? Send(application, sel_registerName("sharedApplication"))
            : 0;
        var window = app == 0 ? 0 : Gallery(app);
        if (window == 0)
            return "link activation: no window to ask";

        var wired = 0;
        CountLinkDelegates(Send(window, sel_registerName("contentView")), ref wired);
        return $"link activation: {wired} view(s) carry the toolkit's link delegate";
    }

    /// <summary>Counts the views under one whose delegate is the toolkit's link target.</summary>
    private static void CountLinkDelegates(nint view, ref int wired)
    {
        if (view == 0)
            return;

        var ask = sel_registerName("delegate");
        if (SendBool(view, sel_registerName("respondsToSelector:"), ask)
            && Send(view, ask) is var target && target != 0
            && class_getName(object_getClass(target)) is var raw && raw != 0
            && Marshal.PtrToStringUTF8(raw) == "NativeFormsLinkTarget")
            ++wired;

        var children = Send(view, sel_registerName("subviews"));
        var count = children == 0 ? 0 : (int)Send(children, sel_registerName("count"));
        for (var i = 0; i < count; ++i)
            CountLinkDelegates(Send(children, sel_registerName("objectAtIndex:"), i), ref wired);
    }

    /// <summary>
    /// What the menu-bar item reports about itself, found among the application's own windows.
    /// </summary>
    /// <remarks>
    /// A status item is not in the gallery's window and cannot be — it lives in a window of its own
    /// that the system status bar puts in the menu bar — so it is looked for across everything the
    /// application owns rather than under the content view. What can be read back is that the button
    /// exists, that it was given an image, and that it was given a tooltip; a press on it is the same
    /// undeliverable gesture every other injected click is on this job.
    /// </remarks>
    public static string StatusItem()
    {
        var application = objc_getClass("NSApplication");
        var app = application == 0 ? 0 : Send(application, sel_registerName("sharedApplication"));
        var windows = app == 0 ? 0 : Send(app, sel_registerName("windows"));
        var count = windows == 0 ? 0 : (int)Send(windows, sel_registerName("count"));

        for (var i = 0; i < count; ++i)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            var button = window == 0 ? 0 : FindStatusButton(Send(window, sel_registerName("contentView")));
            if (button == 0)
                continue;

            var image = Send(button, sel_registerName("image"));
            var tip = Send(button, sel_registerName("toolTip"));
            return $"status item: present, image {(image != 0 ? "set" : "MISSING")}, tooltip {(tip != 0 ? "set" : "MISSING")}";
        }

        // No button in reach does not mean no item. Since the status bar started hosting its items out
        // of process, the button is a remote view and the application keeps only the host window — so
        // what is left to read is the window's level, and NSStatusWindowLevel is a level nothing else
        // this process creates would ask for.
        for (var i = 0; i < count; ++i)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            if (window == 0 || (int)Send(window, sel_registerName("level")) != _StatusWindowLevel)
                continue;

            var name = class_getName(object_getClass(window)) is var raw && raw != 0
                ? Marshal.PtrToStringUTF8(raw)
                : "?";

            return $"status item: present as {name} at NSStatusWindowLevel, hosted out of process "
                + "(its button is a remote view, so the image and tooltip cannot be read back here)";
        }

        // Nothing found in the window list settles nothing on its own, because a modern status item
        // may simply not put a window there. So the platform is asked the question directly: take one
        // more item and see whether it comes with a button. A button means status items work in this
        // session and the toolkit's is somewhere the window list does not reach; no button means this
        // session has no menu bar to put one in, and the difference is the whole answer.
        var bar = objc_getClass("NSStatusBar") is var barClass && barClass != 0
            ? Send(barClass, sel_registerName("systemStatusBar"))
            : 0;

        var spare = bar == 0 ? 0 : SendLength(bar, sel_registerName("statusItemWithLength:"), -1);
        var spareButton = spare == 0 ? 0 : Send(spare, sel_registerName("button"));
        if (bar != 0 && spare != 0)
            Send(bar, sel_registerName("removeStatusItem:"), spare);

        // The activation policy comes with it: a Prohibited process owns no part of the menu bar, so
        // an item handed to one has nowhere to be.
        var policy = app == 0 ? -1 : (int)Send(app, sel_registerName("activationPolicy"));

        return $"status item: not among the application's {count} window(s); a fresh NSStatusItem "
            + $"{(spareButton != 0 ? "does" : "does NOT")} come with a button here "
            + $"(NSStatusBar {(bar != 0 ? "resolves" : "does not resolve")}, activation policy {policy})";
    }

    /// <summary>NSStatusWindowLevel — where the menu bar's extras sit, and nothing else here does.</summary>
    private const int _StatusWindowLevel = 25;

    /// <summary>The first status-bar button at or under a view, or zero.</summary>
    private static nint FindStatusButton(nint view)
    {
        if (view == 0)
            return 0;

        var name = class_getName(object_getClass(view)) is var raw && raw != 0 ? Marshal.PtrToStringUTF8(raw) : null;
        if (name is not null && name.Contains("StatusBarButton", StringComparison.Ordinal))
            return view;

        var children = Send(view, sel_registerName("subviews"));
        var count = children == 0 ? 0 : (int)Send(children, sel_registerName("count"));
        for (var i = 0; i < count; ++i)
            if (FindStatusButton(Send(children, sel_registerName("objectAtIndex:"), i)) is var found && found != 0)
                return found;

        return 0;
    }

    /// <summary>
    /// Whether the shared panels the colour and font choosers run actually exist in this process.
    /// </summary>
    /// <remarks>
    /// This is the whole of what a runner can say about those two. Both are modeless panels that a
    /// person closes, and nobody is here to close one — a probe that opened either would hold the job
    /// until its timeout and report nothing at all. What it can rule out is the silent failure: a class
    /// that does not resolve makes the chooser answer null forever, which is indistinguishable from a
    /// user pressing Cancel every single time.
    /// </remarks>
    public static string Choosers()
    {
        var colour = objc_getClass("NSColorPanel") is var colourClass && colourClass != 0
            ? Send(colourClass, sel_registerName("sharedColorPanel"))
            : 0;

        var manager = objc_getClass("NSFontManager") is var managerClass && managerClass != 0
            ? Send(managerClass, sel_registerName("sharedFontManager"))
            : 0;

        return $"choosers: NSColorPanel {(colour != 0 ? "resolves" : "MISSING")}, "
            + $"NSFontManager {(manager != 0 ? "resolves" : "MISSING")} "
            + "(neither is run here: a modeless panel needs someone to close it)";
    }

    /// <summary>Tallies the class of every view under one, itself included.</summary>
    private static void CountClasses(nint view, Dictionary<string, int> counts)
    {
        if (view == 0)
            return;

        var name = class_getName(object_getClass(view)) is var raw && raw != 0
            ? Marshal.PtrToStringUTF8(raw)
            : null;

        if (!string.IsNullOrEmpty(name))
            counts[name] = counts.TryGetValue(name, out var seen) ? seen + 1 : 1;

        var children = Send(view, sel_registerName("subviews"));
        var count = children == 0 ? 0 : (int)Send(children, sel_registerName("count"));
        for (var i = 0; i < count; ++i)
            CountClasses(Send(children, sel_registerName("objectAtIndex:"), i), counts);
    }

    /// <summary>Counts the views under one, and how many of them track the pointer.</summary>
    private static void CountTrackedViews(nint view, ref int views, ref int tracked)
    {
        if (view == 0)
            return;

        ++views;
        var areas = Send(view, sel_registerName("trackingAreas"));
        if (areas != 0 && (int)Send(areas, sel_registerName("count")) > 0)
            ++tracked;

        var children = Send(view, sel_registerName("subviews"));
        var count = children == 0 ? 0 : (int)Send(children, sel_registerName("count"));
        for (var i = 0; i < count; ++i)
            CountTrackedViews(Send(children, sel_registerName("objectAtIndex:"), i), ref views, ref tracked);
    }

    /// <summary>Records why a capture gave up and answers null, so a caller can return in one line.</summary>
    private static Size? Failed(string reason)
    {
        Diagnosis = reason;
        return null;
    }

    /// <summary>
    /// The window to photograph, picked from every window the application owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <c>mainWindow</c>, and not <c>keyWindow</c>: both are nil while the application is
    /// inactive, and on a runner it is inactive for the first several seconds after
    /// <c>activateIgnoringOtherApps:</c> — nothing else is competing for the focus, so nothing hurries
    /// the window server along. That is the exact shape the shot counts had, and it is a suffix rather
    /// than a scatter: fourteen pages with no window to find, then the last two once activation
    /// finally landed. The window list does not depend on activation, so the capture no longer waits
    /// for a focus change that may never come.
    /// </para>
    /// <para>
    /// The largest visible one is the gallery. Choosing by size rather than by title keeps this from
    /// needing to read an <c>NSString</c> back, and it is not a guess: every other window this process
    /// owns is a popup, which is borderless, tiny beside the window it hangs off, and only visible
    /// while it is open.
    /// </para>
    /// </remarks>
    private static nint Gallery(nint app)
    {
        var windows = Send(app, sel_registerName("windows"));
        var count = windows == 0 ? 0 : (int)Send(windows, sel_registerName("count"));
        var largest = (nint)0;
        var largestArea = 0L;

        for (var i = 0; i < count; ++i)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            if (window == 0 || !SendBool(window, sel_registerName("isVisible")))
                continue;

            var size = SizeFromFrame(Send(window, sel_registerName("contentView")));
            var area = (long)size.Width * size.Height;
            if (area <= largestArea)
                continue;

            largest = window;
            largestArea = area;
        }

        if (largest != 0)
            return largest;

        // Nothing visible: ask the way that needs activation after all, rather than give up on a run
        // whose window is merely ordered out.
        var main = Send(app, sel_registerName("mainWindow"));
        return main != 0 ? main : Send(app, sel_registerName("keyWindow"));
    }

    /// <summary>An empty <c>NSDictionary</c> for the encoder's options.</summary>
    private static nint EmptyDictionary()
    {
        var dictionary = objc_getClass("NSDictionary");
        return dictionary == 0 ? 0 : Send(dictionary, sel_registerName("dictionary"));
    }

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendStructReturn(out Rect result, nint receiver, nint selector);

    /// <summary>Reads the view's frame, using the struct-return entry point where the ABI needs one.</summary>
    private static Size SizeFromFrame(nint view)
    {
        if (view == 0)
            return Size.Empty;

        // On arm64 a four-double struct comes back in registers, so the ordinary send is correct.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            var frame = SendFrame(view, sel_registerName("frame"));
            return new((int)frame.Width, (int)frame.Height);
        }

        SendStructReturn(out var wide, view, sel_registerName("frame"));
        return new((int)wide.Width, (int)wide.Height);
    }

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial Rect SendFrame(nint receiver, nint selector);
}
