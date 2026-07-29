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
    [LibraryImport(_ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass_raw(string name);

    /// <summary>
    /// Looks an Objective-C class up, having made sure the framework that defines it is loaded.
    /// </summary>
    internal static nint objc_getClass(string name)
    {
        if (!_appKit.IsValueCreated)
            _ = _appKit.Value;

        return objc_getClass_raw(name);
    }

    /// <summary>The class an object actually is, rather than the one it was declared as.</summary>
    [LibraryImport(_ObjC)]
    internal static partial nint object_getClass(nint instance);

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

    /// <summary>Asks a yes/no question about an object, such as whether it answers a selector.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SendBool(nint receiver, nint selector, nint argument);

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

    /// <summary>A rectangle in Cocoa's coordinates: doubles, origin at the bottom left.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;

        public CGRect(double x, double y, double width, double height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }
    }

    /// <summary>Sends a message taking a rectangle — four doubles, which arm64 passes in registers.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendRect(nint receiver, nint selector, CGRect frame, nint styleMask, nint backing, [MarshalAs(UnmanagedType.U1)] bool defer);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendRectVoid(nint receiver, nint selector, CGRect frame, [MarshalAs(UnmanagedType.U1)] bool display);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, double argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint first, nint second);

    /// <summary>Pulls the next event from the application's queue.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendEvent(nint receiver, nint selector, nint mask, nint until, nint mode, [MarshalAs(UnmanagedType.U1)] bool dequeue);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendRectInit(nint receiver, nint selector, CGRect frame);

    /// <summary>Builds a tracking area: a rectangle, its option flags, its owner and its user info.</summary>
    /// <remarks>
    /// The rectangle is four doubles, which AArch64 hands over in the floating registers as one
    /// aggregate — so it is declared as the struct it is rather than as loose doubles, and the three
    /// pointer-sized arguments after it land in the integer registers where the selector expects them.
    /// </remarks>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendTrackingArea(nint receiver, nint selector, CGRect rect, nint options, nint owner, nint userInfo);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendRectVoidOnly(nint receiver, nint selector, CGRect frame);

    /// <summary>Claims a rectangle of a view for a pointer shape: four doubles, then one object.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendCursorRect(nint receiver, nint selector, CGRect rect, nint cursor);

    /// <summary>A point in Cocoa's coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint
    {
        public double X, Y;
    }

    /// <summary>A size in Cocoa's coordinates: two doubles, which every ABI here passes in registers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGSize(double width, double height)
    {
        public double Width = width, Height = height;
    }

    /// <summary>Sends a message taking a size.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, CGSize size);

    /// <summary>Reads a point-valued property — two doubles, which AArch64 returns in registers.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial CGPoint SendPoint(nint receiver, nint selector);

    /// <summary>Converts a point between views.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial CGPoint SendConvert(nint receiver, nint selector, CGPoint point, nint fromView);

    /// <summary>Converts a point through a one-argument message, such as a window's to screen space.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial CGPoint SendPoint(nint receiver, nint selector, CGPoint point);

    /// <summary>Reads an integer-valued property.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendInteger(nint receiver, nint selector);

    /// <summary>Asks an integer question about a point, such as which row a table has there.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendIntegerAt(nint receiver, nint selector, CGPoint point);

    /// <summary>Sends a message taking an object and a flag, such as a selection that does not extend.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint first, [MarshalAs(UnmanagedType.U1)] bool second);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial CGRect SendRectArm(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectIntel(out CGRect result, nint receiver, nint selector);

    /// <summary>
    /// Reads a rectangle-valued property, through whichever entry point this architecture requires.
    /// </summary>
    /// <remarks>
    /// A <c>CGRect</c> is four doubles and therefore a homogeneous float aggregate of exactly four
    /// members, which AArch64 hands back in the floating registers — so the ordinary send is correct
    /// there. The System V ABI has no such rule for a 32-byte struct and returns it through hidden
    /// storage, which is what <c>objc_msgSend_stret</c> exists for. Choosing by architecture rather
    /// than assuming is the same care the <c>double</c> return already takes.
    /// </remarks>
    internal static CGRect SendRect(nint receiver, nint selector)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return SendRectArm(receiver, selector);

        SendRectIntel(out var wide, receiver, selector);
        return wide;
    }

    /// <summary>Reads a short-valued property, such as a key code.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial ushort SendUShort(nint receiver, nint selector);

    /// <summary>A range: location and length, as AppKit counts characters.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NSRange
    {
        public nint Location, Length;
    }

    /// <summary>Reads a range-valued property — two integers, returned in registers.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial NSRange SendRange(nint receiver, nint selector);

    /// <summary>Sends a message taking a range.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendRangeVoid(nint receiver, nint selector, NSRange range);

    /// <summary>Reads one element of an ordered collection.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendIndex(nint receiver, nint selector, nint index);

    /// <summary>Sends a two-object message that answers an object.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector, nint first, nint second);

    /// <summary>Sends a three-object message that answers an object.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector, nint first, nint second, nint third);

    /// <summary>Sends an object-and-measure message that answers an object, such as resizing a font.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector, nint first, double second);

    /// <summary>Builds an object from a rectangle and a flag, such as a pop-up that is not a pull-down.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendRectInit(nint receiver, nint selector, CGRect frame, [MarshalAs(UnmanagedType.U1)] bool flag);

    /// <summary>Sends a four-object message, such as registering for one notification from one sender.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint first, nint second, nint third, nint fourth);

    /// <summary>Sends a message taking a flag and answering an object, such as a panel made on demand.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendBoolArgument(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool argument);

    /// <summary>Reads an integer answer to a one-object question, such as a modal session's state.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendInteger(nint receiver, nint selector, nint argument);

    /// <summary>Sends a message taking one measure and answering an object, such as an item of a length.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendLength(nint receiver, nint selector, double length);

    /// <summary>Sends a measure-and-object message, such as blending one colour part of the way to another.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendPointer(nint receiver, nint selector, double first, nint second);

    /// <summary>Builds a colour from its four components.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendColor(nint receiver, nint selector, double red, double green, double blue, double alpha);

    /// <summary>Applies one attribute over a range of an attributed string.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendAttribute(nint receiver, nint selector, nint name, nint value, NSRange range);

    /// <summary>Removes one attribute over a range of an attributed string.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(nint receiver, nint selector, nint name, NSRange range);

    /// <summary>Reads one attribute at a character index; the effective range is not asked for.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendAttributeAt(nint receiver, nint selector, nint name, nint index, nint effectiveRange);

    /// <summary>Sends a message taking a range and answering one, such as widening to paragraphs.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial NSRange SendRange(nint receiver, nint selector, NSRange range);

    /// <summary>Sends a message taking a range and an object, such as writing a range out as RTF.</summary>
    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint SendRangeObject(nint receiver, nint selector, NSRange range, nint argument);

    /// <summary>The address a framework's exported data symbol points at — how a named constant is read.</summary>
    /// <remarks>
    /// The attribute keys AppKit applies to an attributed string are <c>NSString</c> globals, not
    /// values a header could inline. The symbol is the variable, so the string is one dereference past
    /// it — the same shape CoreText's font-attribute key is read with.
    /// </remarks>
    internal static nint Constant(string name)
        => NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out var library)
            && NativeLibrary.TryGetExport(library, name, out var symbol)
            ? Marshal.ReadIntPtr(symbol)
            : 0;

    /// <summary>Allocates an instance of a class, ready for an <c>init…</c> message.</summary>
    internal static nint Allocate(string className)
    {
        var target = objc_getClass(className);
        return target == 0 ? 0 : SendPointer(target, sel_registerName("alloc"));
    }

    /// <summary>Wraps a managed string as an autoreleased <c>NSString</c>.</summary>
    internal static nint NSString(string text)
    {
        var core = CocoaNative.CreateString(text);
        return core; // an NSString and a CFStringRef are the same object, bridged for free
    }

    /// <summary>
    /// Whether an object would answer a selector, which is the only safe way to offer one to an object
    /// whose exact class is not known. An unrecognized selector aborts the process rather than raising
    /// something catchable, so asking first is not defensive — it is the difference between a widget
    /// that ignores a property and an application that stops.
    /// </summary>
    internal static bool Responds(nint receiver, string selector)
        => receiver != 0
        && SendBool(receiver, sel_registerName("respondsToSelector:"), sel_registerName(selector));

    /// <summary>
    /// A colour as an sRGB <c>NSColor</c>, or zero for the empty colour, which is how the core says
    /// "leave the platform's own".
    /// </summary>
    /// <remarks>
    /// sRGB rather than the calibrated or device space: a colour the toolkit states in bytes is an
    /// sRGB colour, and handing those bytes to a different space would draw a shade nobody asked for
    /// and would also raise when read back, since a colour only answers for components in its own space.
    /// </remarks>
    internal static nint NSColorOf(System.Drawing.Color color)
    {
        if (color.IsEmpty)
            return 0;

        var colours = objc_getClass("NSColor");
        return colours == 0
            ? 0
            : SendColor(
                colours,
                sel_registerName("colorWithSRGBRed:green:blue:alpha:"),
                color.R / 255.0,
                color.G / 255.0,
                color.B / 255.0,
                color.A / 255.0);
    }

    /// <summary>
    /// The toolkit's font as an <c>NSFont</c>, falling back to the system font at the same size when
    /// the family is not installed.
    /// </summary>
    /// <remarks>
    /// Bold and italic are not part of a font's name on this platform, they are traits, and only
    /// <c>NSFontManager</c> knows which installed face carries them — asking for "Helvetica Bold" by
    /// name works for the handful of families that ship one under that name and answers nothing for the
    /// rest. A family with no bold face at all comes back unconverted rather than synthesised, which is
    /// AppKit's own answer and better than a smeared one.
    /// </remarks>
    internal static nint NSFontOf(Drawing.Font font)
    {
        var fonts = objc_getClass("NSFont");
        if (fonts == 0)
            return 0;

        var family = NSString(font.Family);
        var named = family == 0
            ? 0
            : SendPointer(fonts, sel_registerName("fontWithName:size:"), family, font.SizeInPoints);

        if (family != 0)
            CocoaNative.CFRelease(family);

        if (named == 0)
            named = SendLength(fonts, sel_registerName("systemFontOfSize:"), font.SizeInPoints);

        return named == 0 ? 0 : WithTraits(named, font.Style);
    }

    /// <summary>NSFontTraitMask: italic is bit 0, bold bit 1 — the pair the rich text box converts with.</summary>
    private static nint WithTraits(nint font, Drawing.FontStyle style)
    {
        var mask = ((style & Drawing.FontStyle.Italic) != 0 ? 1 : 0) | ((style & Drawing.FontStyle.Bold) != 0 ? 2 : 0);
        if (mask == 0)
            return font;

        var manager = SendToClass("NSFontManager", "sharedFontManager");
        if (manager == 0)
            return font;

        var converted = SendPointer(manager, sel_registerName("convertFont:toHaveTrait:"), font, mask);
        return converted == 0 ? font : converted;
    }

    /// <summary>Builds a class at run time, which is how a view gets a <c>drawRect:</c> to call back into.</summary>
    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [LibraryImport(_ObjC)]
    internal static partial void objc_registerClassPair(nint cls);

    /// <summary>Attaches a method to a runtime class; <paramref name="types"/> is the encoded signature.</summary>
    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool class_addMethod(nint cls, nint selector, nint implementation, string types);

    /// <summary>
    /// Whether Objective-C messaging is usable, having first made sure AppKit is in the process.
    /// </summary>
    /// <remarks>
    /// A framework's classes do not exist until something loads it. Nothing here links AppKit — every
    /// other import is CoreFoundation, CoreGraphics or CoreText — so <c>objc_getClass("NSWindow")</c>
    /// answered zero, every peer quietly did nothing, and the run loop returned at once because there
    /// was no <c>NSApplication</c> to ask for events. No error anywhere: an application that builds its
    /// whole interface and then exits without drawing, which is exactly what the probe reported.
    /// </remarks>
    internal static bool Available => _appKit.Value;

    private static readonly Lazy<bool> _appKit = new(LoadAppKit);

    private static bool LoadAppKit()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        // AppKit pulls in Foundation, so one load covers both.
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
        return objc_getClass_raw("NSApplication") != 0;
    }
}
