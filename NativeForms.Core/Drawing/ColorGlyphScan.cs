namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Answers the one question a text renderer needs before it decides how hard to work: does this string
/// contain anything a monochrome glyph rasterizer would get wrong?
/// </summary>
/// <remarks>
/// Colour glyphs are the exception, not the rule — nearly every string a UI draws is plain text, and that
/// path must stay exactly as cheap as it was. So this is a scan over the raw UTF-16, allocating nothing
/// and stopping at the first hit, rather than anything resembling grapheme segmentation. It is
/// deliberately conservative in one direction only: a false positive costs a slower path that still
/// renders correctly, while a false negative renders a coloured glyph in black. It therefore errs toward
/// saying yes.
/// </remarks>
internal static class ColorGlyphScan {
  /// <summary>
  /// Whether <paramref name="text"/> contains a character that may need a colour glyph.
  /// </summary>
  /// <remarks>
  /// The ranges are the ones that actually carry colour in the shipped system fonts:
  /// <list type="bullet">
  ///   <item>anything above the BMP in the emoji planes, which arrives as a surrogate pair;</item>
  ///   <item>U+FE0F, the variation selector that asks for the emoji presentation of an otherwise
  ///     textual character — <c>❤</c> versus <c>❤️</c>;</item>
  ///   <item>the BMP pictograph blocks (dingbats, misc symbols, arrows-with-emoji-forms) and the
  ///     keycap combiner, which pick up colour in an emoji font;</item>
  ///   <item>the regional indicators that pair into flags.</item>
  /// </list>
  /// </remarks>
  public static bool MayContainColorGlyphs(ReadOnlySpan<char> text) {
    foreach (var c in text) {
      // A high surrogate means a supplementary-plane character; every colour emoji lives there,
      // and the non-emoji supplementary planes are rare enough that paying the slower path for
      // them costs nothing in practice.
      if (char.IsHighSurrogate(c))
        return true;

      if (c is >= '\u2190' and <= '\u2BFF'   // arrows, misc technical, dingbats, symbols
          or '\uFE0F'                          // variation selector 16 — "render as emoji"
          or '\u20E3'                          // combining enclosing keycap
          or >= '\u3297' and <= '\u3299'      // circled ideographs with emoji forms
          or '\u00A9' or '\u00AE')            // (c) and (r), both emoji-presented
        return true;
    }

    return false;
  }
}
