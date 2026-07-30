using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Real input on macOS: an <c>NSEvent</c> built by hand, put into the application's own event queue,
/// and pulled out of it again by the same call the toolkit's loop pulls with.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the Win32 <c>SendInput</c> path and, like it, deliberately not a synthetic
/// dispatch into a view. Calling <c>mouseDown:</c> directly would prove a method runs and say nothing
/// about hit-testing, first responder, window ordering or the event routing in between — which is most
/// of what actually breaks.
/// </para>
/// <para>
/// It used to be <c>CGEvent</c>, posted at the HID tap so the window server delivered it like a
/// person's. That is the truer gesture and it is unusable here: macOS gates synthetic input behind the
/// Accessibility permission, a hosted runner grants none, and a post from an untrusted process is
/// accepted and silently dropped. Worse, <c>AXIsProcessTrusted</c> answers yes on that runner, so the
/// probe could not even tell a skip from a failure — every run reported zero clicks and zero
/// keystrokes with no way to say which.
/// </para>
/// <para>
/// <c>[NSApp postEvent:atStart:]</c> needs no grant, because it never leaves the process: the event
/// goes into the application's own queue, which is the queue <c>CocoaBackend.Run</c> drains with
/// <c>nextEventMatchingMask:</c>. What that gives up is the window server and the process boundary.
/// What it keeps is everything the check is about — the event is dispatched with <c>sendEvent:</c>,
/// so the window hit-tests it, the view under the point receives it, the first responder takes the
/// key, and the toolkit hears about it or does not.
/// </para>
/// <para>
/// The ten-argument key constructor is the one declaration here worth reading twice. On Apple's
/// AArch64 ABI the receiver, the selector and six of its arguments fill the integer registers, the
/// point and the timestamp go in the floating ones, and the key code — the last argument and a
/// <c>short</c> — lands on the stack packed to its own size rather than in a slot of its own. A
/// signature that merely looks right reads the wrong bytes and answers something plausible, so
/// <see cref="Route"/> builds one event and reads its key code and characters back before anything
/// trusts the path.
/// </para>
/// </remarks>
internal static partial class ShootInputMac
{
    private const string _ObjC = "/usr/lib/libobjc.A.dylib";
    private const string _CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string _CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // NSEventType
    private const nint _LeftMouseDown = 1;
    private const nint _LeftMouseUp = 2;
    private const nint _MouseMoved = 5;
    private const nint _KeyDown = 10;
    private const nint _KeyUp = 11;

    /// <summary>NSEventModifierFlagShift.</summary>
    private const nint _Shift = 1 << 17;

    [LibraryImport(_ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(_ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector, nint first, [MarshalAs(UnmanagedType.U1)] bool second);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBool(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial double SendDoubleArm(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend_fpret")]
    private static partial double SendDoubleIntel(nint receiver, nint selector);

    /// <summary>Sends a message answering a <c>double</c>, through this architecture's entry point.</summary>
    private static double SendDouble(nint receiver, nint selector)
        => RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? SendDoubleIntel(receiver, selector)
            : SendDoubleArm(receiver, selector);

    /// <summary>
    /// The clock an <c>NSEvent</c> is stamped with: seconds since this machine started.
    /// </summary>
    /// <remarks>
    /// Every event this probe built used to carry a timestamp of zero, which is older than the process
    /// and older than every event before it. Routing does not care — a press is delivered by window
    /// number and location — but anything AppKit works out by comparing one event against the last one
    /// might, and the pointer's own history is exactly that sort of thing. So the events are stamped
    /// with the same clock the window server stamps real ones with, which costs a message send per
    /// event and removes one way for a synthetic gesture to be told from a real one.
    /// </remarks>
    private static double Timestamp()
    {
        var info = objc_getClass("NSProcessInfo");
        var process = info == 0 ? 0 : Send(info, sel_registerName("processInfo"));
        return process == 0 ? 0 : SendDouble(process, sel_registerName("systemUptime"));
    }

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial ushort SendUShort(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial ushort SendUShort(nint receiver, nint selector, nint index);

    /// <summary>A point in Cocoa's coordinates: two doubles, passed in the floating registers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X, Y;
    }

    /// <summary>Converts a point through a one-argument message, such as a window's from screen space.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial CGPoint SendPoint(nint receiver, nint selector, CGPoint point);

    /// <summary>Asks a point-taking message for an object, which is <c>hitTest:</c> and nothing else here.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendHitTest(nint receiver, nint selector, CGPoint point);

    [LibraryImport(_ObjC, EntryPoint = "object_getClass")]
    private static partial nint object_getClass(nint instance);

    [LibraryImport(_ObjC, EntryPoint = "class_getName")]
    private static partial nint class_getName(nint cls);

    /// <summary>Reads a point-valued property, such as where an event landed in its window.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial CGPoint SendPoint(nint receiver, nint selector);

    /// <summary>
    /// <c>+[NSEvent keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:
    /// charactersIgnoringModifiers:isARepeat:keyCode:]</c>, declared exactly.
    /// </summary>
    /// <remarks>
    /// Ten arguments, and the last one does not fit in a register — see the class remarks. The point
    /// is declared as the struct it is so AArch64 hands it over as one aggregate in the floating
    /// registers rather than as two loose doubles displacing everything after them.
    /// </remarks>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendKeyEvent(
        nint receiver,
        nint selector,
        nint type,
        CGPoint location,
        nint modifierFlags,
        double timestamp,
        nint windowNumber,
        nint context,
        nint characters,
        nint charactersIgnoringModifiers,
        [MarshalAs(UnmanagedType.U1)] bool isARepeat,
        ushort keyCode);

    /// <summary>
    /// <c>+[NSEvent mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:
    /// eventNumber:clickCount:pressure:]</c>, declared exactly.
    /// </summary>
    /// <remarks>
    /// The pressure is a <c>float</c> rather than a <c>double</c>, which is the whole of what is
    /// unusual here: it rides in the low half of a floating register, and declaring it wide would
    /// leave the callee reading a number nobody sent.
    /// </remarks>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendMouseEvent(
        nint receiver,
        nint selector,
        nint type,
        CGPoint location,
        nint modifierFlags,
        double timestamp,
        nint windowNumber,
        nint context,
        nint eventNumber,
        nint clickCount,
        float pressure);

    [LibraryImport(_CoreGraphics)]
    private static partial uint CGMainDisplayID();

    [LibraryImport(_CoreGraphics)]
    private static partial nint CGDisplayPixelsHigh(uint display);

    [LibraryImport(_CoreFoundation)]
    private static partial void CFRelease(nint handle);

    [LibraryImport(_CoreFoundation)]
    private static partial int CFRunLoopRunInMode(nint mode, double seconds, [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    [LibraryImport(_CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string name, uint encoding);

    /// <summary>kCFStringEncodingUTF8.</summary>
    private const uint _Utf8 = 0x08000100;

    /// <summary>Whether injected input can be delivered here at all.</summary>
    /// <remarks>
    /// One question now, and it is answerable: is there an <c>NSApplication</c> to post to. The old
    /// route asked <c>AXIsProcessTrusted</c>, which on a hosted runner answers yes while the window
    /// server drops the event anyway — a permission check that could not tell a skip from a failure.
    /// </remarks>
    public static bool Available => _ready.Value;

    private static readonly Lazy<bool> _ready = new(() =>
    {
        try
        {
            return OperatingSystem.IsMacOS() && SharedApplication() != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>The running application object, or zero.</summary>
    private static nint SharedApplication()
    {
        var application = objc_getClass("NSApplication");
        return application == 0 ? 0 : Send(application, sel_registerName("sharedApplication"));
    }

    /// <summary>
    /// Whether the two event constructors are declared correctly, said in terms of what came back.
    /// </summary>
    /// <remarks>
    /// A wrong <c>objc_msgSend</c> signature does not fail, it reads the wrong registers and answers
    /// something that looks like an event — so the ten-argument key constructor is checked against its
    /// own arguments before any run depends on it. A key code that reads back as the one that was
    /// asked for, and characters that read back as the string that was handed in, are the two facts
    /// that could not survive an argument landing in the wrong place.
    /// </remarks>
    public static string Route()
    {
        if (!Available)
            return "input route: no NSApplication in this process, so nothing can be posted";

        var key = KeyEvent(_KeyDown, KeyCodeOf('Z'), 'Z', WindowNumber());
        if (key == 0)
            return "input route: NSEvent refused to build a key event";

        var code = SendUShort(key, sel_registerName("keyCode"));
        var characters = Send(key, sel_registerName("characters"));
        var first = characters == 0 ? '\0' : (char)SendUShort(characters, sel_registerName("characterAtIndex:"), 0);

        var mouse = MouseEvent(_LeftMouseDown, new() { X = 12, Y = 34 }, WindowNumber());
        var landed = mouse == 0 ? default : SendPoint(mouse, sel_registerName("locationInWindow"));

        return $"input route: NSEvent keyEventWithType: answers key code {code} (asked for {KeyCodeOf('Z')}) "
            + $"and characters \"{first}\", mouseEventWithType: answers a location of "
            + $"{landed.X:0},{landed.Y:0} (asked for 12,34) — posted with postEvent:atStart: into the "
            + "application's own queue, which needs no Accessibility grant";
    }

    /// <summary>
    /// Brings the application and its window forward, so a posted key has a first responder to reach.
    /// </summary>
    /// <remarks>
    /// The counterpart of the Win32 injector's <c>Activate</c>. A key event is routed to the key
    /// window, and on a runner nothing competes for the focus — so nothing hurries the window server
    /// into giving this process one unless it is asked.
    /// </remarks>
    public static void Activate()
    {
        var app = SharedApplication();
        if (app == 0)
            return;

        SendVoid(app, sel_registerName("activateIgnoringOtherApps:"), true);
        if (TargetWindow() is var window and not 0)
            Send(window, sel_registerName("makeKeyAndOrderFront:"), 0);
    }

    /// <summary>
    /// The class of the view a press at this screen point would be delivered to, or a word saying why
    /// there is none.
    /// </summary>
    /// <remarks>
    /// The same question AppKit asks itself in <c>sendEvent:</c>, asked out loud. <c>hitTest:</c> takes
    /// its point in the receiver's <em>superview</em> coordinates, and a content view's superview is the
    /// window's own space — which is what <see cref="InWindow"/> already answers in, so the point an
    /// injected event carries is the point that is asked about, with no second conversion to get wrong.
    /// </remarks>
    public static string ViewAt(Point screen)
    {
        var window = TargetWindow();
        if (window == 0)
            return "no window";

        var content = Send(window, sel_registerName("contentView"));
        if (content == 0)
            return "no content view";

        var view = SendHitTest(content, sel_registerName("hitTest:"), InWindow(window, screen));
        if (view == 0)
            return "no view";

        var raw = class_getName(object_getClass(view));
        return raw == 0 ? "an unnamed class" : Marshal.PtrToStringUTF8(raw) ?? "an unnamed class";
    }

    /// <summary>
    /// The class of whatever currently holds the keyboard in the target window, or a word saying why
    /// there is nothing to name.
    /// </summary>
    /// <remarks>
    /// What a focus check has to be able to say when it fails. "The control does not report itself
    /// focused" reads the same whether the press landed somewhere else, whether the widget declined the
    /// keyboard, or whether it took it and the toolkit did not hear — and the responder's class
    /// separates the three: a field that is being typed in answers with the window's shared field
    /// editor rather than with itself, which is the one case a naive check would call a miss.
    /// </remarks>
    public static string FirstResponder()
    {
        var window = TargetWindow();
        if (window == 0)
            return "no window";

        var responder = Send(window, sel_registerName("firstResponder"));
        if (responder == 0)
            return "nothing";

        var raw = class_getName(object_getClass(responder));
        return raw == 0 ? "an unnamed class" : Marshal.PtrToStringUTF8(raw) ?? "an unnamed class";
    }

    /// <summary>
    /// Whether the application is active and whether the window a gesture is aimed at holds the key
    /// status.
    /// </summary>
    /// <remarks>
    /// The last thing on this route that could account for a moved event reaching nothing. AppKit
    /// hands a mouse-moved event to a window's first responder, and whether it does so for a window
    /// that is not key is not something a header states — so a report that the canvas held the
    /// keyboard and heard nothing anyway is only conclusive alongside this. A runner has nothing
    /// competing for the focus, which is precisely why it cannot be assumed either way.
    /// </remarks>
    public static string Activation()
    {
        var app = SharedApplication();
        if (app == 0)
            return "there is no application to ask";

        var active = SendBool(app, sel_registerName("isActive"));
        var key = Send(app, sel_registerName("keyWindow"));
        var target = TargetWindow();
        return $"the application is {(active ? "active" : "INACTIVE")} and the window is "
            + (key == 0 ? "not key, and nothing is" : key == target ? "the key one" : "not key, another is");
    }

    /// <summary>Clicks at a screen point, reporting whether the events were built and posted.</summary>
    /// <remarks>
    /// Aimed at whichever window is under the point rather than at the run's target window, because a
    /// posted event names the window it is going to and AppKit routes it there without consulting the
    /// geometry: a press aimed at a menu but addressed to the window behind it is delivered to the
    /// window behind it. That is invisible while nothing overlaps — every gallery page has exactly one
    /// window under every point — and it is the whole question the moment a popup is open.
    /// </remarks>
    public static bool Click(Point screen)
    {
        var window = WindowAt(screen);
        if (window == 0)
            return false;

        var at = InWindow(window, screen);
        var number = Send(window, sel_registerName("windowNumber"));

        // The whole gesture is queued before anything is dispatched, because a control that tracks the
        // pointer runs its own loop from mouseDown: and pulls the release out of the queue itself — a
        // promoted check box is an NSButton and does exactly that. Posting the release afterwards would
        // leave that loop waiting for an event nobody has sent yet.
        return Post(MouseEvent(_MouseMoved, at, number))
            && Post(MouseEvent(_LeftMouseDown, at, number))
            && Post(MouseEvent(_LeftMouseUp, at, number));
    }

    /// <summary>Moves the pointer to a screen point, reporting whether the event was built and posted.</summary>
    /// <remarks>
    /// The half of the gesture <see cref="Click"/> already sends ahead of a press, on its own — because
    /// hover is the one input route on this backend that was wired and never witnessed, and what a
    /// press proves about it is nothing: a press carries its own location and the view under it gets
    /// the event whatever the tracking areas think.
    /// </remarks>
    public static bool Move(Point screen)
    {
        var window = WindowAt(screen);
        return window != 0
            && Post(MouseEvent(_MouseMoved, InWindow(window, screen), Send(window, sel_registerName("windowNumber"))));
    }

    /// <summary>Types one character, reporting whether the events were built and posted.</summary>
    public static bool Type(char character) => Press(KeyCodeOf(character), character);

    /// <summary>
    /// Presses one key by the number this platform gives its <em>place</em>, reporting whether the
    /// events were built and posted.
    /// </summary>
    /// <remarks>
    /// The named keys are the ones worth posting this way. A Mac key code is a position rather than a
    /// letter, so <see cref="Type"/> has to look one up for a character it was handed — but Tab, the
    /// arrows and the function keys sit where they sit on every layout, which is exactly why the
    /// backend reads them off the key code and not off what they type.
    /// </remarks>
    public static bool Press(ushort keyCode, char character)
    {
        var number = WindowNumber();
        var down = KeyEvent(_KeyDown, keyCode, character, number);
        var up = KeyEvent(_KeyUp, keyCode, character, number);
        return down != 0 && up != 0 && Post(down) && Post(up);
    }

    /// <summary>
    /// Runs the pending run-loop sources, so whatever was just asked for — a pending frame, a queued
    /// callback — has happened before anything asks whether it did.
    /// </summary>
    /// <remarks>
    /// Deliberately not the event queue. This is what the shutter waits on, and dispatching input
    /// while a view is being asked to draw itself is how a capture ends up photographing a window
    /// mid-gesture; the injector's own settling is <see cref="Deliver"/>.
    /// </remarks>
    public static void Drain()
    {
        var mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", _Utf8);
        if (mode == 0)
            return;

        // A short bounded spin: long enough for the platform to catch up, short enough that a check
        // which is never going to pass does not hold the job open.
        for (var i = 0; i < 10; ++i)
            CFRunLoopRunInMode(mode, 0.02, false);

        CFRelease(mode);
    }

    /// <summary>Delivers whatever was just posted, then lets the run loop settle around it.</summary>
    /// <remarks>
    /// The queue has to be drained here rather than left to the toolkit's loop, because this runs
    /// <em>inside</em> that loop: the checks are driven from a timer tick, and a tick is work the loop
    /// posted to itself, so the loop is not fetching events for as long as the tick is running. So the
    /// loop is asked to take a turn — <see cref="Pump"/> — rather than having its fetch-and-dispatch
    /// pair copied, which is what keeps this from being a synthetic call into a view: the window still
    /// routes the event, and the toolkit still stands where it stands in front of it.
    /// </remarks>
    public static void Deliver()
    {
        Pump();
        Drain();
        Pump();
    }

    /// <summary>Runs everything waiting through one turn of the toolkit's own loop.</summary>
    /// <remarks>
    /// Asked of the backend rather than made here, and the difference is the point. Fetching with
    /// <c>nextEventMatchingMask:</c> and dispatching with <c>sendEvent:</c> is only the platform's half
    /// of a turn: the toolkit stands ahead of AppKit in <c>CocoaBackend.Intercept</c>, which is where a
    /// popup's light dismiss and the text box's key seam both live. A probe that made the pair itself
    /// was therefore posting keys that went straight to the editor without ever passing the code that
    /// names them, so the key table could not be witnessed however many keystrokes arrived.
    /// </remarks>
    private static void Pump()
    {
        if (Hawkynt.NativeForms.Backends.BackendRegistry.Resolve() is Hawkynt.NativeForms.Backends.MacOS.CocoaBackend cocoa)
            cocoa.PumpEvents(200);
    }

    /// <summary>
    /// The window an injected event is aimed at: the one being shown modally while a dialog is up,
    /// and otherwise the gallery.
    /// </summary>
    /// <remarks>
    /// Asked of <c>NSApp</c> rather than worked out, because a modal session is exactly what
    /// <c>modalWindow</c> reports and the alternative — picking the largest visible window — would
    /// answer with the gallery behind the dialog, which is the one window a press must not be aimed at
    /// while one is up.
    /// </remarks>
    private static nint TargetWindow()
    {
        var app = SharedApplication();
        var modal = app == 0 ? 0 : Send(app, sel_registerName("modalWindow"));
        return modal != 0 ? modal : ShootMacOS.GalleryWindow();
    }

    /// <summary>The target window's number, which is how an event says where it is going.</summary>
    private static nint WindowNumber()
    {
        var window = TargetWindow();
        return window == 0 ? 0 : Send(window, sel_registerName("windowNumber"));
    }

    /// <summary>
    /// The frontmost window of this application whose content covers a screen point, or the run's
    /// target window where none does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by hit-testing each window's content view rather than by comparing frames, which needs no
    /// struct-returning message send and no second opinion about where a window's chrome ends:
    /// <c>hitTest:</c> takes its point in the receiver's superview coordinates, and a content view's
    /// superview is the window's own space — exactly what <see cref="InWindow"/> already answers in.
    /// A point outside gets nil, which is the containment test.
    /// </para>
    /// <para>
    /// Front-most by <c>orderedIndex</c>, where zero is the window nearest the viewer, so a menu
    /// standing over the dialog that opened it takes the press rather than the dialog underneath.
    /// </para>
    /// </remarks>
    private static nint WindowAt(Point screen)
    {
        var app = SharedApplication();
        var windows = app == 0 ? 0 : Send(app, sel_registerName("windows"));
        var count = windows == 0 ? 0 : (int)Send(windows, sel_registerName("count"));
        var height = CGDisplayPixelsHigh(CGMainDisplayID());
        var onScreen = new CGPoint { X = screen.X, Y = height - screen.Y };

        var found = (nint)0;
        var frontmost = int.MaxValue;
        for (var i = 0; i < count; ++i)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            if (window == 0 || !SendBool(window, sel_registerName("isVisible")))
                continue;

            var content = Send(window, sel_registerName("contentView"));
            if (content == 0)
                continue;

            var local = SendPoint(window, sel_registerName("convertPointFromScreen:"), onScreen);
            if (SendHitTest(content, sel_registerName("hitTest:"), local) == 0)
                continue;

            var order = (int)Send(window, sel_registerName("orderedIndex"));
            if (order >= frontmost)
                continue;

            found = window;
            frontmost = order;
        }

        return found != 0 ? found : TargetWindow();
    }

    /// <summary>A toolkit screen point in a window's own coordinates, which is where an event lands.</summary>
    /// <remarks>
    /// Two flips in one step. The toolkit counts down from the top of the main display and Cocoa counts
    /// up from its bottom, and a window's base coordinates are neither — so the height is turned around
    /// first and <c>convertPointFromScreen:</c> does the rest.
    /// </remarks>
    private static CGPoint InWindow(nint window, Point screen)
    {
        var height = CGDisplayPixelsHigh(CGMainDisplayID());
        var onScreen = new CGPoint { X = screen.X, Y = height - screen.Y };
        return SendPoint(window, sel_registerName("convertPointFromScreen:"), onScreen);
    }

    /// <summary>Builds one key event, or zero.</summary>
    private static nint KeyEvent(nint type, ushort keyCode, char character, nint windowNumber)
    {
        var events = objc_getClass("NSEvent");
        if (events == 0)
            return 0;

        // Both spellings are handed over: AppKit's text input re-derives the character from the key
        // code and the layout for most routes and reads `characters` for the rest, so a key code that
        // did not match the string would insert whichever the field happened to consult.
        var shifted = char.IsUpper(character);
        var typed = CFStringCreateWithCString(0, character.ToString(), _Utf8);
        var unshifted = CFStringCreateWithCString(0, char.ToLowerInvariant(character).ToString(), _Utf8);
        if (typed == 0 || unshifted == 0)
            return 0;

        var built = SendKeyEvent(
            events,
            sel_registerName("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            type,
            default,
            shifted ? _Shift : 0,
            Timestamp(),
            windowNumber,
            0, // the graphics context argument is ignored on every OS that still has this selector
            typed,
            unshifted,
            false,
            keyCode);

        CFRelease(typed);
        CFRelease(unshifted);
        return built;
    }

    /// <summary>Builds one mouse event, or zero.</summary>
    private static nint MouseEvent(nint type, CGPoint at, nint windowNumber)
    {
        var events = objc_getClass("NSEvent");
        return events == 0
            ? 0
            : SendMouseEvent(
                events,
                sel_registerName("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
                type,
                at,
                0,
                Timestamp(),
                windowNumber,
                0,
                0,
                type == _MouseMoved ? 0 : 1,
                type == _LeftMouseDown ? 1f : 0f);
    }

    /// <summary>
    /// Where a character sits on the keyboard, as this platform numbers keys.
    /// </summary>
    /// <remarks>
    /// A Mac key code names a <em>place</em> rather than a letter — 0x00 is the key left of the home
    /// row, which is A on a US layout and Q on a French one — so this is the ANSI arrangement and
    /// nothing more. That is enough for a probe, which types the letters it chose itself; anything the
    /// table does not name falls back to the key code the character is carried with, since the string
    /// is handed over as well.
    /// </remarks>
    private static ushort KeyCodeOf(char character)
        => char.ToUpperInvariant(character) switch
        {
            'A' => 0x00, 'S' => 0x01, 'D' => 0x02, 'F' => 0x03, 'H' => 0x04, 'G' => 0x05,
            'Z' => 0x06, 'X' => 0x07, 'C' => 0x08, 'V' => 0x09, 'B' => 0x0B, 'Q' => 0x0C,
            'W' => 0x0D, 'E' => 0x0E, 'R' => 0x0F, 'Y' => 0x10, 'T' => 0x11, 'O' => 0x1F,
            'U' => 0x20, 'I' => 0x22, 'P' => 0x23, 'L' => 0x25, 'J' => 0x26, 'K' => 0x28,
            'N' => 0x2D, 'M' => 0x2E,
            '1' => 0x12, '2' => 0x13, '3' => 0x14, '4' => 0x15, '5' => 0x17, '6' => 0x16,
            '7' => 0x1A, '8' => 0x1C, '9' => 0x19, '0' => 0x1D,
            _ => 0,
        };

    /// <summary>Puts one event at the back of the application's queue, answering whether there was one.</summary>
    private static bool Post(nint theEvent)
    {
        var app = SharedApplication();
        if (theEvent == 0 || app == 0)
            return false;

        SendVoid(app, sel_registerName("postEvent:atStart:"), theEvent, false);
        return true;
    }
}
