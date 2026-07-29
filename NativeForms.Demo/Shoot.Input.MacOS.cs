using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Real OS-level input on macOS: the pointer and keyboard are driven through <c>CGEvent</c>, so the
/// events arrive from the window server exactly as a person's would.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the Win32 <c>SendInput</c> path and, like it, deliberately not a synthetic
/// dispatch into the view. Calling <c>mouseDown:</c> directly would prove a method runs and say nothing
/// about hit-testing, first responder, window ordering or the event routing in between — which is most
/// of what actually breaks.
/// </para>
/// <para>
/// macOS gates synthetic input behind the Accessibility permission, which a CI runner does not grant.
/// So every entry point reports whether it landed and the caller treats "could not inject" as a skip
/// rather than a pass; a check that silently tests nothing is worse than one that is absent.
/// </para>
/// </remarks>
internal static unsafe partial class ShootInputMac
{
    private const string _CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string _CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // CGEventType
    private const uint _LeftMouseDown = 1;
    private const uint _LeftMouseUp = 2;
    private const uint _MouseMoved = 5;

    /// <summary>kCGHIDEventTap — posted at the lowest level, so the whole system sees it.</summary>
    private const uint _HidTap = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X, Y;
    }

    [LibraryImport(_CoreGraphics)]
    private static partial nint CGEventCreateMouseEvent(nint source, uint type, CGPoint position, uint button);

    [LibraryImport(_CoreGraphics)]
    private static partial nint CGEventCreateKeyboardEvent(nint source, ushort keyCode, [MarshalAs(UnmanagedType.U1)] bool keyDown);

    // A UTF-16 buffer by pointer: a span would need runtime marshalling this assembly does not enable,
    // and the API wants nothing more than the address of the characters anyway.
    [LibraryImport(_CoreGraphics)]
    private static partial void CGEventKeyboardSetUnicodeString(nint theEvent, nint length, char* text);

    [LibraryImport(_CoreGraphics)]
    private static partial void CGEventPost(uint tap, nint theEvent);

    [LibraryImport(_CoreFoundation)]
    private static partial void CFRelease(nint handle);

    /// <summary>Whether this process is allowed to post synthetic events.</summary>
    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(_CoreFoundation)]
    private static partial int CFRunLoopRunInMode(nint mode, double seconds, [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    [LibraryImport(_CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string name, uint encoding);

    /// <summary>
    /// Whether injected input can be delivered here at all.
    /// </summary>
    /// <remarks>
    /// macOS accepts a posted CGEvent from an untrusted process and silently drops it, so "the post
    /// succeeded" says nothing about delivery — the first run of this reported a real click that never
    /// reached a check box, which was the permission missing rather than the toolkit failing. Asking
    /// the Accessibility API up front is the only way to tell a skip from a failure, and a check that
    /// cannot tell them apart is worse than no check.
    /// </remarks>
    public static bool Available => _trusted.Value;

    private static readonly Lazy<bool> _trusted = new(() =>
    {
        try
        {
            return AXIsProcessTrusted();
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

    /// <summary>Clicks at a screen point, reporting whether the event was created and posted.</summary>
    public static bool Click(Point screen)
    {
        var at = new CGPoint { X = screen.X, Y = screen.Y };
        return Post(CGEventCreateMouseEvent(0, _MouseMoved, at, 0))
            && Post(CGEventCreateMouseEvent(0, _LeftMouseDown, at, 0))
            && Post(CGEventCreateMouseEvent(0, _LeftMouseUp, at, 0));
    }

    /// <summary>Types one character, reporting whether the event was created and posted.</summary>
    public static bool Type(char character)
    {
        var down = CGEventCreateKeyboardEvent(0, 0, true);
        var up = CGEventCreateKeyboardEvent(0, 0, false);
        if (down == 0 || up == 0)
            return false;

        // The key code is nothing in particular; the character is carried as a Unicode payload, which
        // is what lets one path type anything without a keyboard-layout table.
        var text = stackalloc char[1];
        text[0] = character;
        CGEventKeyboardSetUnicodeString(down, 1, text);
        CGEventKeyboardSetUnicodeString(up, 1, text);
        return Post(down) && Post(up);
    }

    /// <summary>
    /// Runs the pending run-loop sources, so an injected event has been delivered and handled before
    /// anything asks whether it arrived.
    /// </summary>
    public static void Drain()
    {
        var mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
        if (mode == 0)
            return;

        // A short bounded spin: long enough for the window server to deliver, short enough that a
        // check which is never going to pass does not hold the job open.
        for (var i = 0; i < 10; ++i)
            CFRunLoopRunInMode(mode, 0.02, false);

        CFRelease(mode);
    }

    private static bool Post(nint theEvent)
    {
        if (theEvent == 0)
            return false;

        CGEventPost(_HidTap, theEvent);
        CFRelease(theEvent);
        return true;
    }
}
