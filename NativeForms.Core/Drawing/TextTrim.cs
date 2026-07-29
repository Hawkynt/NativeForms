using System.Globalization;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Shortens a label to the width it has, ending it in an ellipsis so the reader can tell that it was
/// shortened at all.
/// </summary>
/// <remarks>
/// <para>
/// Clipping is what a control gets for free, and it is wrong twice over: a centred label loses its
/// beginning as well as its end, and nothing on screen says the name is not the whole name. Two files
/// whose names differ only past the cut become indistinguishable, which for a file manager is the
/// whole point of the column.
/// </para>
/// <para>
/// Where to cut is measured, not counted. An emoji is one user-perceived character but two UTF-16
/// ones, and it draws several times the width of a Latin letter — through a different rasterizer
/// again, since colour glyphs go to DirectWrite on Windows rather than the GDI text call. Counting
/// characters would cut a name far too early or far too late; the search here asks
/// <see cref="IGraphics.MeasureText"/>, which is the same renderer that will paint the string, so the
/// answer is the width that will actually be drawn.
/// </para>
/// <para>
/// The cut itself lands on a grapheme cluster boundary. Cutting on a UTF-16 boundary would split a
/// surrogate pair into two unpaired halves that render as replacement characters, and would break an
/// emoji whose single glyph is several code points — a flag is two regional indicators, a profession
/// is a zero-width-joiner sequence, a skin tone is a base plus a modifier. Cutting between the parts
/// leaves a different emoji, or two.
/// </para>
/// </remarks>
internal static class TextTrim
{
    /// <summary>The character appended to a shortened string.</summary>
    internal const string Ellipsis = "…";

    /// <summary>Cluster counts above this are walked on the heap rather than the stack.</summary>
    private const int StackLimit = 256;

    /// <summary>
    /// <paramref name="text"/> if it already fits in <paramref name="maxWidth"/>, otherwise as many
    /// whole grapheme clusters as fit alongside a trailing ellipsis. Returns an empty string when not
    /// even the ellipsis fits.
    /// </summary>
    internal static string ToWidth(IGraphics g, string text, Font font, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            return string.Empty;

        // The overwhelmingly common case is a label that fits, and it costs exactly one measurement.
        if (g.MeasureText(text, font).Width <= maxWidth)
            return text;

        if (g.MeasureText(Ellipsis, font).Width > maxWidth)
            return string.Empty;

        // Offsets one past each grapheme cluster, so boundaries[i] is the length of the prefix made
        // of the first i+1 clusters. A cluster is at least one char, so text.Length is enough room.
        var rented = text.Length > StackLimit ? new int[text.Length] : null;
        Span<int> boundaries = rented ?? stackalloc int[StackLimit];
        var count = 0;
        var offset = 0;
        while (offset < text.Length)
        {
            offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            boundaries[count++] = offset;
        }

        // Binary search for the most clusters that still fit with the ellipsis. `low` is the largest
        // count known to fit (0 = ellipsis alone), `high` the smallest known not to.
        var low = 0;
        var high = count; // the whole string does not fit, established above
        while (high - low > 1)
        {
            var mid = low + ((high - low) / 2);
            if (Fits(g, text, font, boundaries[mid - 1], maxWidth))
                low = mid;
            else
                high = mid;
        }

        return low == 0 ? Ellipsis : string.Concat(text.AsSpan(0, boundaries[low - 1]), Ellipsis);
    }

    /// <summary>Whether the first <paramref name="length"/> chars plus an ellipsis fit.</summary>
    private static bool Fits(IGraphics g, string text, Font font, int length, int maxWidth)
        => g.MeasureText(string.Concat(text.AsSpan(0, length), Ellipsis), font).Width <= maxWidth;
}
