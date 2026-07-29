using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// Objective-C messaging: the seam through which the AppKit half of this backend will be driven.
/// </summary>
/// <remarks>
/// <para>
/// AppKit has no C surface, so <c>NSApplication</c>, <c>NSWindow</c> and <c>NSView</c> can only be
/// reached by sending messages. <c>objc_msgSend</c> is declared once per return shape rather than once
/// per call: the function is variadic in C but must be called through a signature matching the
/// selector exactly, and a mismatched one does not fail — it reads the wrong registers and returns
/// plausible nonsense, which is the worst kind of bug to inherit.
/// </para>
/// <para>
/// Floating-point returns need their own entry point on Intel (<c>objc_msgSend_fpret</c>) and not on
/// Apple silicon, where the ordinary one is correct. Both are declared and the right one is chosen by
/// architecture rather than assumed, because the runner is arm64 and most machines in the wild are
/// too, which would leave the Intel path untested and wrong.
/// </para>
/// <para>
/// Everything is <c>[LibraryImport]</c> with blittable arguments, so §2's AOT rules hold: no
/// marshalled delegates, no reflection, and every callback that AppKit eventually needs will be an
/// <c>[UnmanagedCallersOnly]</c> static passed as a function pointer.
/// </para>
/// </remarks>
internal static partial class CocoaRuntime
{
    private const string _ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>Looks a class up by name; zero when the framework defining it is not loaded.</summary>
    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint objc_getClass(string name);

    /// <summary>Interns a selector, which is how a method is named at run time.</summary>
    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint sel_registerName(string name);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector, nint argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SendBool(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial double SendDoubleArm(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend_fpret")]
    private static partial double SendDoubleIntel(nint receiver, nint selector);

    /// <summary>
    /// Sends a message returning a <c>double</c>, through whichever entry point this architecture
    /// requires.
    /// </summary>
    internal static double SendDouble(nint receiver, nint selector)
        => RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? SendDoubleIntel(receiver, selector)
            : SendDoubleArm(receiver, selector);

    /// <summary>Sends a parameterless message by selector name to a class found by name.</summary>
    internal static nint SendToClass(string className, string selector)
    {
        var target = objc_getClass(className);
        return target == 0 ? 0 : SendPointer(target, sel_registerName(selector));
    }

    /// <summary>Whether Objective-C messaging is usable at all — the answer is no off macOS.</summary>
    internal static bool Available => OperatingSystem.IsMacOS() && objc_getClass("NSObject") != 0;
}
