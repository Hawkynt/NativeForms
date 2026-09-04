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
internal static class TextTrim {
  /// <summary>The character appended to a shortened string.</summary>
  internal const string Ellipsis = "…";

  /// <summary>Cluster counts above this are walked on the heap rather than the stack.</summary>
  private const int StackLimit = 256;

  /// <summary>Slots the memo starts with. Powers of two throughout, so the slot is a mask.</summary>
  private const int InitialSlots = 64;

  /// <summary>
  /// The most slots the memo will grow to before it starts over. A frame draws at most as many
  /// shortened labels as it has cells, so this is a viewport many times over; past it the working
  /// set is a scroll through a long list rather than a frame, and holding on to every name that
  /// ever went by would be a leak.
  /// </summary>
  private const int MaxSlots = 4096;

  /// <summary>
  /// The result for a given (text, width, font). Shortening allocates a string, and a repaint must
  /// allocate nothing at all once warm (see the paint-allocation guarantee) — so the second and
  /// every later frame answers from here. Struct entries, so filling a slot allocates nothing
  /// either.
  /// </summary>
  /// <remarks>
  /// Open addressing rather than one entry per slot, and it is the guarantee above that forces the
  /// choice: with a direct-mapped memo two names that land on the same slot evict each other on
  /// every single frame, for the life of the process, and which names those are is decided by the
  /// process-wide string hash seed — so the same list allocates on one run and not on the next.
  /// Probing instead keeps both, and the table grows when it fills, so a whole frame's worth of
  /// labels is answered from the memo however they hash. Painting is a UI-thread activity, which is
  /// what makes an unsynchronized memo sound; a torn read costs a recomputation, never a wrong
  /// answer, because the result is published before the key that admits it.
  /// </remarks>
  private static Memo[] _cache = new Memo[InitialSlots];

  /// <summary>Entries currently in <see cref="_cache"/>; it grows before it is half full.</summary>
  private static int _live;

  private struct Memo {
    internal string? Text;
    internal int Width;
    internal Font Font;
    internal bool Middle;
    internal string Result;
  }

  /// <summary>
  /// <paramref name="text"/> if it already fits in <paramref name="maxWidth"/>, otherwise as many
  /// whole grapheme clusters as fit alongside a trailing ellipsis. Returns an empty string when not
  /// even the ellipsis fits.
  /// </summary>
  internal static string ToWidth(IGraphics g, string text, Font font, int maxWidth)
      => Trim(g, text, font, maxWidth, middle: false);

  /// <summary>
  /// As <see cref="ToWidth"/>, but drops the middle and keeps both ends.
  /// </summary>
  /// <remarks>
  /// For a name whose distinguishing part is at the end, a trailing ellipsis is no better than the
  /// clipping it replaces: a column of volumes called "ArchinstallVolumeGroup-root",
  /// "…-home" and "…-swap" cuts to the same "Archinstall…" three times over, and the reader is left
  /// picking between three identical rows. Keeping both ends spends the same width on the half of
  /// the string that actually differs. Use it for names and paths; a sentence still reads better
  /// trimmed from the end.
  /// </remarks>
  internal static string ToWidthMiddle(IGraphics g, string text, Font font, int maxWidth)
      => Trim(g, text, font, maxWidth, middle: true);

  private static string Trim(IGraphics g, string text, Font font, int maxWidth, bool middle) {
    if (string.IsNullOrEmpty(text) || maxWidth <= 0)
      return string.Empty;

    // The overwhelmingly common case is a label that fits, and it costs exactly one measurement
    // and no allocation — the string that came in is the string that goes out.
    if (g.MeasureText(text, font).Width <= maxWidth)
      return text;

    var cache = _cache;
    var mask = cache.Length - 1;
    for (var slot = Slot(text, maxWidth, middle) & mask; ; slot = (slot + 1) & mask) {
      ref var memo = ref cache[slot];
      if (memo.Text is null)
        break; // the probe run ended without the key, so it is not in the table

      if (memo.Width == maxWidth && memo.Font == font && memo.Middle == middle && memo.Text == text)
        return memo.Result;
    }

    var trimmed = Shorten(g, text, font, maxWidth, middle);
    Remember(text, font, maxWidth, middle, trimmed);
    return trimmed;
  }

  /// <summary>The slot a key starts probing from; masked by the caller, which knows the size.</summary>
  private static int Slot(string text, int maxWidth, bool middle)
      => text.GetHashCode() ^ (maxWidth * 397) ^ (middle ? 0x5F5F : 0);

  /// <summary>Files a freshly shortened result, growing or emptying the table first if it is full
  /// enough that probe runs would get long.</summary>
  private static void Remember(string text, Font font, int maxWidth, bool middle, string trimmed) {
    if ((_live + 1) * 2 > _cache.Length) {
      if (_cache.Length < MaxSlots) {
        var grown = new Memo[_cache.Length * 2];
        foreach (var entry in _cache)
          if (entry.Text is not null)
            Place(grown, entry.Text, entry.Font, entry.Width, entry.Middle, entry.Result);

        _cache = grown;
      } else {
        // Past the cap the working set is no longer a frame, so there is nothing to preserve.
        Array.Clear(_cache);
        _live = 0;
      }
    }

    Place(_cache, text, font, maxWidth, middle, trimmed);
    ++_live;
  }

  /// <summary>Writes an entry into the first free slot of its probe run.</summary>
  private static void Place(Memo[] cache, string text, Font font, int maxWidth, bool middle, string trimmed) {
    var mask = cache.Length - 1;
    var slot = Slot(text, maxWidth, middle) & mask;
    while (cache[slot].Text is not null)
      slot = (slot + 1) & mask;

    ref var memo = ref cache[slot];
    memo.Width = maxWidth;
    memo.Font = font;
    memo.Middle = middle;
    memo.Result = trimmed;
    memo.Text = text; // last: the key is what admits the entry, so publish it once it is whole
  }

  /// <summary>Does the actual search. Only ever reached on a memo miss.</summary>
  private static string Shorten(IGraphics g, string text, Font font, int maxWidth, bool middle) {
    if (g.MeasureText(Ellipsis, font).Width > maxWidth)
      return string.Empty;

    // Offsets one past each grapheme cluster, so boundaries[i] is the length of the prefix made
    // of the first i+1 clusters. A cluster is at least one char, so text.Length is enough room.
    var rented = text.Length > StackLimit ? new int[text.Length] : null;
    Span<int> boundaries = rented ?? stackalloc int[StackLimit];
    var count = 0;
    var offset = 0;
    while (offset < text.Length) {
      offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
      boundaries[count++] = offset;
    }

    // Binary search for the most clusters that still fit with the ellipsis. `low` is the largest
    // count known to fit (0 = ellipsis alone), `high` the smallest known not to. Keeping more
    // clusters never makes the string narrower, either end of it, so the predicate is monotone
    // and the search is sound for both shapes.
    var low = 0;
    var high = count; // the whole string does not fit, established above
    while (high - low > 1) {
      var mid = low + ((high - low) / 2);
      if (g.MeasureText(Keep(text, boundaries, count, mid, middle), font).Width <= maxWidth)
        low = mid;
      else
        high = mid;
    }

    return low == 0 ? Ellipsis : Keep(text, boundaries, count, low, middle);
  }

  /// <summary>
  /// The candidate made of <paramref name="keep"/> whole clusters and the ellipsis — taken from the
  /// front, or split between the two ends when <paramref name="middle"/> is set.
  /// </summary>
  private static string Keep(string text, ReadOnlySpan<int> boundaries, int count, int keep, bool middle) {
    if (!middle)
      return string.Concat(text.AsSpan(0, boundaries[keep - 1]), Ellipsis);

    // The odd cluster goes to the front, where a name's stem is.
    var head = (keep + 1) / 2;
    var tail = keep - head;
    var tailStart = tail == 0 ? text.Length : boundaries[count - tail - 1];
    return string.Concat(text.AsSpan(0, boundaries[head - 1]), Ellipsis, text.AsSpan(tailStart));
  }
}
