using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The C entry points the backend needs from the macOS system frameworks.
/// </summary>
/// <remarks>
/// <para>
/// CoreGraphics, CoreText and CoreFoundation are plain C, so they are reachable through
/// <c>[LibraryImport]</c> exactly like Win32 and GTK — no <c>objc_msgSend</c>, no calling-convention
/// guesswork, and no marshalled delegates, which keeps §2's AOT rules intact for free. AppKit is
/// Objective-C and will need messaging when windows arrive; everything measured, drawn or asked about
/// the display can be done without it, which is why this half comes first.
/// </para>
/// <para>
/// Reference counting follows the Core Foundation naming rule: anything from a <c>Create</c> or
/// <c>Copy</c> function is owned here and released, anything from a <c>Get</c> is not.
/// </para>
/// </remarks>
internal static partial class CocoaNative
{
    private const string _CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string _CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string _CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";

    /// <summary>Value of <c>kCFStringEncodingUTF16</c> in the host's byte order.</summary>
    private const uint _Utf16 = 0x0100;

    // --- CoreFoundation -------------------------------------------------------------------------

    [LibraryImport(_CoreFoundation)]
    private static partial nint CFStringCreateWithBytes(nint allocator, ReadOnlySpan<byte> bytes, nint length, uint encoding, [MarshalAs(UnmanagedType.U1)] bool externalRepresentation);

    [LibraryImport(_CoreFoundation)]
    internal static partial void CFRelease(nint handle);

    [LibraryImport(_CoreFoundation)]
    private static partial nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);

    [LibraryImport(_CoreFoundation)]
    private static partial nint CFAttributedStringCreate(nint allocator, nint text, nint attributes);

    // --- CoreGraphics ---------------------------------------------------------------------------

    [LibraryImport(_CoreGraphics)]
    internal static partial uint CGMainDisplayID();

    [LibraryImport(_CoreGraphics)]
    internal static partial nint CGDisplayPixelsWide(uint display);

    [LibraryImport(_CoreGraphics)]
    internal static partial nint CGDisplayPixelsHigh(uint display);

    // --- CoreGraphics drawing -------------------------------------------------------------------

    [LibraryImport(_CoreGraphics)] internal static partial void CGContextSaveGState(nint context);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextRestoreGState(nint context);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextTranslateCTM(nint context, double tx, double ty);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextScaleCTM(nint context, double sx, double sy);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextSetRGBFillColor(nint context, double r, double g, double b, double a);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextSetRGBStrokeColor(nint context, double r, double g, double b, double a);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextSetLineWidth(nint context, double width);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextFillRect(nint context, CocoaRuntime.CGRect rect);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextStrokeRect(nint context, CocoaRuntime.CGRect rect);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextFillEllipseInRect(nint context, CocoaRuntime.CGRect rect);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextStrokeEllipseInRect(nint context, CocoaRuntime.CGRect rect);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextClipToRect(nint context, CocoaRuntime.CGRect rect);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextBeginPath(nint context);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextMoveToPoint(nint context, double x, double y);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextAddLineToPoint(nint context, double x, double y);
    [LibraryImport(_CoreGraphics)] internal static partial void CGContextStrokePath(nint context);

    /// <summary>The context AppKit has made current for the view being drawn.</summary>
    internal static nint CurrentContext()
    {
        var graphicsContext = CocoaRuntime.SendToClass("NSGraphicsContext", "currentContext");
        return graphicsContext == 0
            ? 0
            : CocoaRuntime.SendPointer(graphicsContext, CocoaRuntime.sel_registerName("CGContext"));
    }

    // --- CoreText -------------------------------------------------------------------------------

    [LibraryImport(_CoreText)]
    private static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

    [LibraryImport(_CoreText)]
    private static partial nint CTLineCreateWithAttributedString(nint attributedString);

    [LibraryImport(_CoreText)]
    private static partial double CTLineGetTypographicBounds(nint line, out double ascent, out double descent, out double leading);

    /// <summary>The <c>kCTFontAttributeName</c> key, resolved once from the framework's data symbol.</summary>
    private static readonly nint _FontAttributeName = ResolveFontAttributeName();

    private static nint ResolveFontAttributeName()
    {
        var library = NativeLibrary.Load(_CoreText);
        return NativeLibrary.TryGetExport(library, "kCTFontAttributeName", out var symbol)
            ? Marshal.ReadIntPtr(symbol)   // the symbol is the CFStringRef variable, not the string
            : 0;
    }

    /// <summary>Releases a Core Foundation object unless it is null.</summary>
    private static void Release(nint handle)
    {
        if (handle != 0)
            CFRelease(handle);
    }

    /// <summary>Wraps a managed string as a CoreFoundation string the caller must release.</summary>
    internal static nint CreateString(string text)
    {
        var bytes = MemoryMarshal.AsBytes(text.AsSpan());
        return CFStringCreateWithBytes(0, bytes, bytes.Length, _Utf16, false);
    }

    /// <summary>
    /// Measures one line of text in a font, in points. Returns <see langword="false"/> when CoreText
    /// declines, so the caller can fall back rather than report a size of zero as a measurement.
    /// </summary>
    internal static bool TryMeasure(string text, string family, double size, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (_FontAttributeName == 0)
            return false;

        var name = CreateString(family);
        var content = CreateString(text);
        var font = name == 0 ? 0 : CTFontCreateWithName(name, size, 0);
        nint attributes = 0;
        nint attributed = 0;
        nint line = 0;

        try
        {
            if (font == 0 || content == 0)
                return false;

            attributes = CFDictionaryCreate(0, [_FontAttributeName], [font], 1, 0, 0);
            if (attributes == 0)
                return false;

            attributed = CFAttributedStringCreate(0, content, attributes);
            if (attributed == 0)
                return false;

            line = CTLineCreateWithAttributedString(attributed);
            if (line == 0)
                return false;

            width = CTLineGetTypographicBounds(line, out var ascent, out var descent, out var leading);
            height = ascent + descent + leading;
            return true;
        }
        finally
        {
            Release(line);
            Release(attributed);
            Release(attributes);
            Release(font);
            Release(content);
            Release(name);
        }
    }
}
