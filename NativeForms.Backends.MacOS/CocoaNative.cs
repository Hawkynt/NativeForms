using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;

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
internal static partial class CocoaNative {
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

  /// <summary>
  /// A dictionary over constants and cached objects, with no key or value callbacks.
  /// </summary>
  /// <remarks>
  /// Null callbacks mean the dictionary neither retains nor releases what it holds and compares keys
  /// by pointer — which is exactly right for framework constants and for the fonts
  /// <see cref="CocoaFontCache"/> keeps for the process lifetime, and would be wrong for anything
  /// with a shorter life than the dictionary.
  /// </remarks>
  internal static nint CreateDictionary(nint[] keys, nint[] values)
      => CFDictionaryCreate(0, keys, values, keys.Length, 0, 0);

  [LibraryImport(_CoreFoundation)]
  private static partial nint CFAttributedStringCreate(nint allocator, nint text, nint attributes);

  // --- CoreGraphics ---------------------------------------------------------------------------

  [LibraryImport(_CoreGraphics)]
  internal static partial nint CGColorSpaceCreateDeviceRGB();

  [LibraryImport(_CoreGraphics)]
  internal static partial void CGColorSpaceRelease(nint space);

  /// <summary>
  /// A bitmap to draw into, or with a null <paramref name="data"/> one that allocates and owns its
  /// own pixels — which is what this backend wants, because then nothing managed has to stay pinned
  /// for as long as CoreGraphics might read it.
  /// </summary>
  [LibraryImport(_CoreGraphics)]
  internal static partial nint CGBitmapContextCreate(nint data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, nint space, uint bitmapInfo);

  [LibraryImport(_CoreGraphics)]
  internal static partial nint CGBitmapContextGetData(nint context);

  /// <summary>
  /// The stride the context actually allocated, which is not always the one it was asked for — a
  /// bitmap context may pad its rows for alignment, and writing tightly packed rows into a padded
  /// buffer shears the picture.
  /// </summary>
  [LibraryImport(_CoreGraphics)]
  internal static partial nint CGBitmapContextGetBytesPerRow(nint context);

  [LibraryImport(_CoreGraphics)]
  internal static partial nint CGBitmapContextCreateImage(nint context);

  [LibraryImport(_CoreGraphics)]
  internal static partial void CGContextRelease(nint context);

  [LibraryImport(_CoreGraphics)]
  internal static partial void CGImageRelease(nint image);

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
  [LibraryImport(_CoreGraphics)] internal static partial void CGContextFillPath(nint context);
  [LibraryImport(_CoreGraphics)] internal static partial void CGContextClosePath(nint context);
  [LibraryImport(_CoreGraphics)] internal static partial void CGContextAddArcToPoint(nint context, double x1, double y1, double x2, double y2, double radius);
  [LibraryImport(_CoreGraphics)] internal static partial void CGContextDrawImage(nint context, CocoaRuntime.CGRect rect, nint image);

  /// <summary>The context AppKit has made current for the view being drawn.</summary>
  internal static nint CurrentContext() {
    var graphicsContext = CocoaRuntime.SendToClass("NSGraphicsContext", "currentContext");
    return graphicsContext == 0
        ? 0
        : CocoaRuntime.SendPointer(graphicsContext, CocoaRuntime.sel_registerName("CGContext"));
  }

  // --- CoreText -------------------------------------------------------------------------------

  [LibraryImport(_CoreText)]
  internal static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

  /// <summary>
  /// Copies a font with symbolic traits — bold, italic — applied, or null when the family ships no
  /// such face. A size of zero keeps the one the font already has.
  /// </summary>
  [LibraryImport(_CoreText)]
  internal static partial nint CTFontCreateCopyWithSymbolicTraits(nint font, double size, nint matrix, uint traits, uint mask);

  /// <summary>Where the family puts an underline, measured up from the baseline, so normally negative.</summary>
  [LibraryImport(_CoreText)]
  internal static partial double CTFontGetUnderlinePosition(nint font);

  [LibraryImport(_CoreText)]
  internal static partial double CTFontGetUnderlineThickness(nint font);

  [LibraryImport(_CoreText)]
  internal static partial double CTFontGetXHeight(nint font);

  [LibraryImport(_CoreText)]
  private static partial nint CTLineCreateWithAttributedString(nint attributedString);

  [LibraryImport(_CoreText)]
  private static partial double CTLineGetTypographicBounds(nint line, out double ascent, out double descent, out double leading);

  internal const string CoreTextFramework = _CoreText;
  internal const string CoreFoundationFramework = _CoreFoundation;

  /// <summary>Reads one of a framework's exported constants — the symbol is the variable, not its value.</summary>
  internal static nint ResolveConstant(string framework, string name) {
    var library = NativeLibrary.Load(framework);
    return NativeLibrary.TryGetExport(library, name, out var symbol)
        ? Marshal.ReadIntPtr(symbol)
        : 0;
  }

  [LibraryImport(_CoreFoundation)]
  private static partial nint CFStringGetLength(nint text);

  [LibraryImport(_CoreFoundation)]
  [return: MarshalAs(UnmanagedType.U1)]
  private static partial bool CFStringGetCString(nint text, Span<byte> buffer, nint size, uint encoding);

  /// <summary>Reads a Core Foundation string into managed form.</summary>
  internal static string ReadString(nint text) {
    if (text == 0)
      return string.Empty;

    var length = (int)CFStringGetLength(text);
    if (length <= 0)
      return string.Empty;

    // UTF-8 needs up to four bytes per code unit, plus the terminator the C API writes.
    var buffer = new byte[(length * 4) + 1];
    const uint utf8 = 0x08000100;
    return CFStringGetCString(text, buffer, buffer.Length, utf8)
        ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length - 1)
        : string.Empty;
  }

  /// <summary>Releases a Core Foundation object unless it is null.</summary>
  private static void Release(nint handle) {
    if (handle != 0)
      CFRelease(handle);
  }

  [LibraryImport(_CoreText)]
  private static partial void CTLineDraw(nint line, nint context);

  [LibraryImport(_CoreGraphics)]
  internal static partial void CGContextSetTextPosition(nint context, double x, double y);

  /// <summary>
  /// A 2D affine transform: six doubles, and the reason this is a struct rather than six parameters.
  /// </summary>
  /// <remarks>
  /// AArch64 passes a homogeneous float aggregate in registers only up to four members. Six exceeds
  /// that, so the ABI passes it indirectly — and a declaration taking six loose doubles hands the
  /// callee six registers where it expects a pointer. It does not fail: it reads whatever the pointer
  /// register happened to hold and transforms the text by nonsense, which is how a text matrix turns
  /// glyphs into wedges.
  /// </remarks>
  [StructLayout(LayoutKind.Sequential)]
  internal struct CGAffineTransform {
    public double A, B, C, D, Tx, Ty;
  }

  [LibraryImport(_CoreGraphics)]
  internal static partial void CGContextSetTextMatrix(nint context, CGAffineTransform matrix);

  /// <summary>
  /// Draws one line of text at a baseline, returning whether it could.
  /// </summary>
  /// <remarks>
  /// The font and its attribute dictionary come from <see cref="CocoaFontCache"/>, so what this
  /// builds per call is only what depends on the text: the string, the attributed string and the
  /// line. The colour rides on the context's fill colour rather than on the string, which is what
  /// <c>kCTForegroundColorFromContextAttributeName</c> asks CoreText to honour — the alternative,
  /// a <c>CGColorRef</c> under <c>kCTForegroundColorAttributeName</c>, would make the cached
  /// dictionary vary by colour as well as by font, and would state the colour in a second colour
  /// space while every other primitive here states it with <c>CGContextSetRGBFillColor</c>.
  /// </remarks>
  internal static bool TryDrawText(nint context, string text, Font font, double x, double baseline, System.Drawing.Color color) {
    if (text.Length == 0)
      return false;

    var typeface = CocoaFontCache.FontFor(font);
    var attributes = CocoaFontCache.AttributesFor(typeface);
    if (attributes == 0)
      return false;

    var content = CreateString(text);
    nint attributed = 0;
    nint line = 0;

    try {
      if (content == 0)
        return false;

      attributed = CFAttributedStringCreate(0, content, attributes);
      if (attributed == 0)
        return false;

      line = CTLineCreateWithAttributedString(attributed);
      if (line == 0)
        return false;

      CGContextSetRGBFillColor(context, color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

      // The context is flipped so the toolkit's y grows downward; glyphs would come out mirrored
      // unless the text matrix flips back, which is the standard pairing for drawing text into a
      // flipped context rather than a special case.
      CGContextSetTextMatrix(context, new() { A = 1, D = -1 });
      CGContextSetTextPosition(context, x, baseline);
      CTLineDraw(line, context);

      if ((font.Style & (FontStyle.Underline | FontStyle.Strikeout)) != 0)
        DrawRules(context, line, typeface, font.Style, x, baseline);

      return true;
    } finally {
      Release(line);
      Release(attributed);
      Release(content);
    }
  }

  /// <summary>Draws the underline and the strikeout, which are rules rather than glyphs.</summary>
  /// <remarks>
  /// CoreText has a font attribute for the first and none at all for the second — strikethrough is
  /// AppKit's, not CoreText's, and <c>CTLineDraw</c> would ignore it. One of the two has to be drawn
  /// by hand, so both are: they then share a thickness, a colour and a length instead of the engine
  /// drawing one and this drawing the other slightly differently. The rules use the fill colour the
  /// glyphs were just drawn in, and the metrics come from the family so a rule sits where that
  /// family's designer put it.
  /// </remarks>
  private static void DrawRules(nint context, nint line, nint typeface, FontStyle style, double x, double baseline) {
    var width = CTLineGetTypographicBounds(line, out _, out _, out _);
    if (width <= 0)
      return;

    var thickness = Math.Max(1, CTFontGetUnderlineThickness(typeface));

    // Both offsets are measured up from the baseline and y grows downward here, hence the subtraction.
    if ((style & FontStyle.Underline) != 0)
      CGContextFillRect(context, new(x, baseline - CTFontGetUnderlinePosition(typeface), width, thickness));

    if ((style & FontStyle.Strikeout) != 0)
      CGContextFillRect(context, new(x, baseline - (CTFontGetXHeight(typeface) / 2), width, thickness));
  }

  /// <summary>Wraps a managed string as a CoreFoundation string the caller must release.</summary>
  internal static nint CreateString(string text) {
    var bytes = MemoryMarshal.AsBytes(text.AsSpan());
    return CFStringCreateWithBytes(0, bytes, bytes.Length, _Utf16, false);
  }

  /// <summary>
  /// Measures one line of text in a font, in points. Returns <see langword="false"/> when CoreText
  /// declines, so the caller can fall back rather than report a size of zero as a measurement.
  /// </summary>
  internal static bool TryMeasure(string text, Font font, out double width, out double height) {
    width = 0;
    height = 0;

    var attributes = CocoaFontCache.AttributesFor(CocoaFontCache.FontFor(font));
    if (attributes == 0)
      return false;

    var content = CreateString(text);
    nint attributed = 0;
    nint line = 0;

    try {
      if (content == 0)
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
    } finally {
      Release(line);
      Release(attributed);
      Release(content);
    }
  }
}
