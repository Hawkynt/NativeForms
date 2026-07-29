using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// Realizes <see cref="Font"/> descriptors into CoreText fonts, and each font into the attribute
/// dictionary a line is built from — one of each per distinct face, kept for the process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// Text is the busiest thing on the paint path and almost none of it changes between frames: a
/// control repaints with the font it already had. §4 forbids per-frame allocation there, and the font
/// and the dictionary are the parts that do not depend on the string, so they are built once and
/// looked up afterwards. What is left per call is the string, the attributed string and the line.
/// </para>
/// <para>
/// Both handles are shared and never released — a caller that released one would leave the cache
/// pointing at freed memory, and the dictionaries hold their font without retaining it (see
/// <see cref="CocoaNative.CreateDictionary"/>), which only holds because nothing here is ever let go
/// of. The set of entries is bounded by the fonts a theme and a handful of controls ask for.
/// </para>
/// </remarks>
internal static class CocoaFontCache
{
    /// <summary><c>kCTFontTraitItalic</c> and <c>kCTFontTraitBold</c> from <c>CTFontTraits.h</c>.</summary>
    private const uint _ItalicTrait = 1 << 0;
    private const uint _BoldTrait = 1 << 1;

    private static readonly nint _FontAttributeName = CocoaNative.ResolveConstant(CocoaNative.CoreTextFramework, "kCTFontAttributeName");
    private static readonly nint _ForegroundFromContextAttributeName = CocoaNative.ResolveConstant(CocoaNative.CoreTextFramework, "kCTForegroundColorFromContextAttributeName");
    private static readonly nint _True = CocoaNative.ResolveConstant(CocoaNative.CoreFoundationFramework, "kCFBooleanTrue");

    private static readonly Dictionary<Font, nint> _fonts = [];
    private static readonly Dictionary<nint, nint> _attributes = [];

    /// <summary>The shared <c>CTFontRef</c> for a descriptor (0 when CoreText declines).</summary>
    internal static nint FontFor(Font font)
    {
        // Underline and strikeout pick no face — they are rules drawn over one — so they are dropped
        // from the key rather than building the same face up to four times.
        var key = font.WithStyle(font.Style & (FontStyle.Bold | FontStyle.Italic));
        if (_fonts.TryGetValue(key, out var handle))
            return handle;

        handle = Create(key);
        if (handle != 0)
            _fonts[key] = handle;

        return handle;
    }

    /// <summary>
    /// Creates the face: the family by name, then the weight and the slant as symbolic traits.
    /// </summary>
    /// <remarks>
    /// A trait is not part of a name here. <c>CTFontCreateWithName</c> takes a family and a size and
    /// nothing else, so a font asked for in bold came back regular and every heading on this backend
    /// photographed at the same weight as its body text. Asking for "Helvetica Bold" by name instead
    /// would work for the few families that ship a face under that name and answer nothing for the
    /// rest — the trait copy asks the family for the face and is the same route the native widgets
    /// take through <c>NSFontManager</c>.
    /// </remarks>
    private static nint Create(Font font)
    {
        var name = CocoaNative.CreateString(font.Family);
        if (name == 0)
            return 0;

        var plain = CocoaNative.CTFontCreateWithName(name, font.SizeInPoints, 0);
        CocoaNative.CFRelease(name);
        if (plain == 0 || font.Style == FontStyle.Regular)
            return plain;

        var traits = ((font.Style & FontStyle.Bold) != 0 ? _BoldTrait : 0)
                   | ((font.Style & FontStyle.Italic) != 0 ? _ItalicTrait : 0);

        // Size zero keeps the size the font already carries. A family with no such face answers null,
        // and the plain face is a better answer than no text at all.
        var styled = CocoaNative.CTFontCreateCopyWithSymbolicTraits(plain, 0, 0, traits, traits);
        if (styled == 0)
            return plain;

        CocoaNative.CFRelease(plain);
        return styled;
    }

    /// <summary>The shared attribute dictionary that draws in a font and in the context's fill colour.</summary>
    internal static nint AttributesFor(nint font)
    {
        if (font == 0 || _FontAttributeName == 0)
            return 0;

        if (_attributes.TryGetValue(font, out var handle))
            return handle;

        // Without the second attribute CoreText fills glyphs with the string's own foreground colour,
        // which defaults to black however the context is set up — the reason owner-drawn text on this
        // backend arrived black on every page.
        handle = _ForegroundFromContextAttributeName != 0 && _True != 0
            ? CocoaNative.CreateDictionary([_FontAttributeName, _ForegroundFromContextAttributeName], [font, _True])
            : CocoaNative.CreateDictionary([_FontAttributeName], [font]);

        if (handle != 0)
            _attributes[font] = handle;

        return handle;
    }
}
