using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The one reading of the Windows Forms mnemonic convention: a single <c>&amp;</c> marks the character
/// after it, <c>&amp;&amp;</c> is a literal ampersand, and a trailing <c>&amp;</c> marks nothing.
/// </summary>
/// <remarks>
/// Shared because a caption is read three times over — once to find the key the form has to answer to,
/// once to strip the mark-up before it is drawn, and once to place the underline — and three copies of
/// the escape rule is three chances for them to disagree about <c>&amp;&amp;</c>.
///
/// A caption with no ampersand in it, which is nearly every one, is handed straight back: the scan is
/// the whole cost, and nothing is allocated on the paint path for the common case.
/// </remarks>
internal static class Mnemonics {
  /// <summary>The uppercased marked character, or <c>'\0'</c> when the caption marks none.</summary>
  public static char CharOf(string text) {
    var index = MarkAt(text);
    return index < 0 ? '\0' : char.ToUpperInvariant(text[index + 1]);
  }

  /// <summary>
  /// The caption with its mark-up removed: every <c>&amp;&amp;</c> collapsed to one ampersand and the
  /// marking <c>&amp;</c> dropped. This is the string that is drawn and the string that is measured —
  /// measuring the raw one reserves room for a glyph nobody ever sees.
  /// </summary>
  public static string Strip(string text) {
    if (text.IndexOf('&') < 0)
      return text;

    var builder = new System.Text.StringBuilder(text.Length);
    for (var i = 0; i < text.Length; ++i) {
      if (text[i] != '&') {
        builder.Append(text[i]);
        continue;
      }

      // A trailing '&' marks nothing and simply disappears. '&&' collapses to one ampersand and
      // both characters are consumed. A marking '&' drops on its own — the character it marks is
      // appended by the next turn of the loop, which is the whole difference between stripping the
      // mark-up and eating the letter behind it.
      if (i + 1 >= text.Length)
        break;

      if (text[i + 1] != '&')
        continue;

      builder.Append('&');
      ++i;
    }

    return builder.ToString();
  }

  /// <summary>
  /// Where the marked character lands in the <see cref="Strip"/>ped caption, or -1 when there is
  /// none. The index is into the stripped string because that is the one being drawn.
  /// </summary>
  public static int IndexOf(string text) {
    var stripped = 0;
    for (var i = 0; i < text.Length; ++i) {
      if (text[i] != '&') {
        ++stripped;
        continue;
      }

      if (i + 1 >= text.Length)
        return -1;

      if (text[i + 1] == '&') {
        ++stripped; // the literal ampersand this pair collapses to
        ++i;
        continue;
      }

      return stripped;
    }

    return -1;
  }

  /// <summary>
  /// Underlines the marked character of a caption that has already been drawn into
  /// <paramref name="bounds"/> under <paramref name="alignment"/>. A no-op when the caption marks
  /// nothing, which is the common case and costs one scan.
  /// </summary>
  /// <remarks>
  /// The underline is placed by measuring the run before the marked character rather than by asking
  /// the backend where a glyph landed, which not every text engine here can answer. Shared for the
  /// same reason as the rest of this type: two surfaces placing the same line from the same rule is
  /// one rule, and two copies of it drift.
  /// </remarks>
  public static void Underline(
      IGraphics g,
      string text,
      string caption,
      Font font,
      Color color,
      Rectangle bounds,
      ContentAlignment alignment) {
    var index = IndexOf(text);
    if (index < 0 || index >= caption.Length)
      return;

    var size = g.MeasureText(caption, font);
    var prefix = index > 0 ? g.MeasureText(caption[..index], font).Width : 0;
    var width = g.MeasureText(caption.Substring(index, 1), font).Width;
    var origin = ContentLayout.Anchor(bounds, size, alignment);
    var y = origin.Y + size.Height - 1;
    g.DrawLine(color, origin.X + prefix, y, origin.X + prefix + width - 1, y);
  }

  /// <summary>The index of the marking <c>&amp;</c> in the raw caption, or -1 when there is none.</summary>
  private static int MarkAt(string text) {
    for (var i = 0; i < text.Length - 1; ++i) {
      if (text[i] != '&')
        continue;

      if (text[i + 1] == '&') {
        ++i;
        continue;
      }

      return i;
    }

    return -1;
  }
}
