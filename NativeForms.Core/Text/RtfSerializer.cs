using System.Drawing;
using System.Text;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Text;

/// <summary>
/// Writes and reads the RTF subset NativeForms speaks: character styles (bold/italic/underline/
/// strikeout), text color, font size, paragraph alignment and bullets over plain text runs. The
/// writer's output is plain standard RTF — WordPad and Word open it — and the reader is a tolerant
/// subset parser: control words outside the subset are skipped, so it also digests the richer
/// documents a native Win32 rich edit produces, keeping whatever falls inside the subset.
/// </summary>
/// <remarks>
/// Bullets use the classic word-processor compatibility encoding: a <c>{\pntext …}</c> fallback
/// group plus a <c>{\*\pn\pnlvlblt …}</c> destination, which is also what the Win32 rich edit
/// emits — so bullet paragraphs survive the round trip on every backend. Fonts are not part of the
/// subset: everything maps to the single default font, matching the control surface (there is no
/// per-selection font family yet).
/// </remarks>
public static class RtfSerializer
{
    /// <summary>Character formatting carried through the reader's group stack.</summary>
    private struct CharState
    {
        /// <summary>Active style flags.</summary>
        public FontStyle Style;

        /// <summary>Index into the color table; 0 is the automatic (default) color.</summary>
        public int ColorIndex;

        /// <summary>Font size in points; 0 means default.</summary>
        public float FontSize;

        /// <summary>How many fallback characters follow a <c>\u</c> escape (<c>\uc</c>).</summary>
        public int UnicodeSkip;
    }

    /// <summary>Serializes <paramref name="document"/> into an RTF string.</summary>
    public static string Write(RichDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var colors = new List<Color>();
        foreach (var paragraph in document.Paragraphs)
            foreach (var run in paragraph.Runs)
                if (!run.Color.IsEmpty && !colors.Contains(run.Color))
                    colors.Add(run.Color);

        var result = new StringBuilder();
        result.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}");
        if (colors.Count != 0)
        {
            result.Append(@"{\colortbl ;");
            foreach (var color in colors)
                result.Append(@"\red").Append(color.R).Append(@"\green").Append(color.G).Append(@"\blue").Append(color.B).Append(';');

            result.Append('}');
        }

        for (var p = 0; p < document.Paragraphs.Count; ++p)
        {
            if (p != 0)
                result.Append(@"\par");

            var paragraph = document.Paragraphs[p];
            result.Append(@"\pard");
            result.Append(HorizontalComponent(paragraph.Alignment) switch
            {
                ContentAlignment.TopCenter => @"\qc ",
                ContentAlignment.TopRight => @"\qr ",
                _ => @"\ql ",
            });

            if (paragraph.Bullet)
                result.Append(@"{\pntext\'b7\tab}{\*\pn\pnlvlblt\pnf0{\pntxtb\'b7}}");

            foreach (var run in paragraph.Runs)
                WriteRun(result, run, colors);
        }

        result.Append('}');
        return result.ToString();
    }

    /// <summary>Parses <paramref name="rtf"/> into a document, ignoring everything outside the subset.</summary>
    public static RichDocument Parse(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);

        var document = new RichDocument();
        var colors = new List<Color>();
        var stack = new Stack<CharState>();
        var state = new CharState { UnicodeSkip = 1 };
        var paragraph = new RichParagraph();
        var text = new StringBuilder();
        var textState = state;

        // Closes the pending text buffer into a run carrying the format it was opened with.
        void Flush()
        {
            if (text.Length == 0)
                return;

            var color = textState.ColorIndex > 0 && textState.ColorIndex <= colors.Count
                ? colors[textState.ColorIndex - 1]
                : Color.Empty;
            paragraph.Runs.Add(new(text.ToString(), textState.Style, color, textState.FontSize));
            text.Clear();
        }

        void Append(char c)
        {
            if (text.Length != 0 && (textState.Style != state.Style || textState.ColorIndex != state.ColorIndex || !textState.FontSize.Equals(state.FontSize)))
                Flush();

            textState = state;
            text.Append(c);
        }

        var i = 0;
        while (i < rtf.Length)
        {
            var c = rtf[i];
            switch (c)
            {
                case '{':
                    stack.Push(state);
                    ++i;
                    break;

                case '}':
                    if (stack.Count != 0)
                        state = stack.Pop();

                    ++i;
                    break;

                case '\\':
                    i = ReadControl(rtf, i, ref state, stack, colors, Flush, Append, ref paragraph, document);
                    break;

                case '\r' or '\n':
                    ++i;
                    break;

                default:
                    Append(c);
                    ++i;
                    break;
            }
        }

        Flush();
        document.Paragraphs.Add(paragraph);
        return document;
    }

    /// <summary>Appends one run, wrapping it in a formatting group when it carries any formatting.</summary>
    private static void WriteRun(StringBuilder result, in RichTextRun run, List<Color> colors)
    {
        var formatted = run.Style != FontStyle.Regular || !run.Color.IsEmpty || run.FontSize != 0f;
        if (formatted)
        {
            result.Append('{');
            if ((run.Style & FontStyle.Bold) != 0)
                result.Append(@"\b");
            if ((run.Style & FontStyle.Italic) != 0)
                result.Append(@"\i");
            if ((run.Style & FontStyle.Underline) != 0)
                result.Append(@"\ul");
            if ((run.Style & FontStyle.Strikeout) != 0)
                result.Append(@"\strike");
            if (!run.Color.IsEmpty)
                result.Append(@"\cf").Append(colors.IndexOf(run.Color) + 1);
            if (run.FontSize != 0f)
                result.Append(@"\fs").Append((int)MathF.Round(run.FontSize * 2));

            result.Append(' ');
        }

        AppendEscaped(result, run.Text);
        if (formatted)
            result.Append('}');
    }

    /// <summary>Escapes RTF's reserved characters and emits non-ASCII as <c>\u</c> escapes.</summary>
    private static void AppendEscaped(StringBuilder result, string text)
    {
        foreach (var c in text)
            switch (c)
            {
                case '\\' or '{' or '}':
                    result.Append('\\').Append(c);
                    break;

                case '\t':
                    result.Append(@"\tab ");
                    break;

                case < ' ':
                    break;

                case > '\x7f':
                    result.Append(@"\u").Append((int)(short)c).Append('?');
                    break;

                default:
                    result.Append(c);
                    break;
            }
    }

    /// <summary>
    /// Consumes one control word or control symbol starting at the backslash at
    /// <paramref name="i"/>, updating the reader state, and returns the index after it.
    /// </summary>
    private static int ReadControl(
        string rtf,
        int i,
        ref CharState state,
        Stack<CharState> stack,
        List<Color> colors,
        Action flush,
        Action<char> append,
        ref RichParagraph paragraph,
        RichDocument document)
    {
        ++i; // the backslash
        if (i >= rtf.Length)
            return i;

        var c = rtf[i];

        // Control symbols: a single non-alphabetic character.
        if (!char.IsAsciiLetter(c))
        {
            switch (c)
            {
                case '\\' or '{' or '}':
                    append(c);
                    return i + 1;

                case '\'':
                    // \'hh — an 8-bit character as two hex digits (the writer only emits it for bullets).
                    if (i + 2 < rtf.Length
                        && int.TryParse(rtf.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var code))
                    {
                        append((char)code);
                        return i + 3;
                    }

                    return i + 1;

                case '~':
                    append(' ');
                    return i + 1;

                case '*':
                    // \* marks a destination group readers may skip — and this subset reader does,
                    // detecting the one destination it understands: the \pn bullet definition.
                    return SkipGroup(rtf, i + 1, stack, detectBullet: paragraph);

                default:
                    return i + 1;
            }
        }

        var start = i;
        while (i < rtf.Length && char.IsAsciiLetter(rtf[i]))
            ++i;

        var word = rtf[start..i];
        var parameter = 1;
        var hasParameter = false;
        if (i < rtf.Length && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
        {
            hasParameter = true;
            var negative = rtf[i] == '-';
            if (negative)
                ++i;

            parameter = 0;
            while (i < rtf.Length && char.IsAsciiDigit(rtf[i]))
                parameter = parameter * 10 + (rtf[i++] - '0');

            if (negative)
                parameter = -parameter;
        }

        // A single space after a control word is its delimiter, not text.
        if (i < rtf.Length && rtf[i] == ' ')
            ++i;

        switch (word)
        {
            case "b":
                state.Style = parameter != 0 ? state.Style | FontStyle.Bold : state.Style & ~FontStyle.Bold;
                break;

            case "i":
                state.Style = parameter != 0 ? state.Style | FontStyle.Italic : state.Style & ~FontStyle.Italic;
                break;

            case "ul":
                state.Style = parameter != 0 ? state.Style | FontStyle.Underline : state.Style & ~FontStyle.Underline;
                break;

            case "ulnone":
                state.Style &= ~FontStyle.Underline;
                break;

            case "strike":
                state.Style = parameter != 0 ? state.Style | FontStyle.Strikeout : state.Style & ~FontStyle.Strikeout;
                break;

            case "cf":
                state.ColorIndex = hasParameter ? parameter : 0;
                break;

            case "fs":
                state.FontSize = hasParameter ? parameter / 2f : 0f;
                break;

            case "uc":
                state.UnicodeSkip = hasParameter ? parameter : 1;
                break;

            case "u":
                append((char)(ushort)(short)parameter);
                for (var skip = state.UnicodeSkip; skip > 0 && i < rtf.Length; --skip)
                    i = rtf[i] == '\\' && i + 3 < rtf.Length && rtf[i + 1] == '\'' ? i + 4 : i + 1;
                break;

            case "ql":
                paragraph.Alignment = ContentAlignment.TopLeft;
                break;

            case "qc":
                paragraph.Alignment = ContentAlignment.TopCenter;
                break;

            case "qr":
                paragraph.Alignment = ContentAlignment.TopRight;
                break;

            case "par" or "line":
                flush();
                document.Paragraphs.Add(paragraph);
                var next = new RichParagraph { Alignment = paragraph.Alignment, Bullet = paragraph.Bullet };
                paragraph = next;
                break;

            case "pard":
                paragraph.Alignment = ContentAlignment.TopLeft;
                paragraph.Bullet = false;
                break;

            case "tab":
                append('\t');
                break;

            case "bullet":
                append('•');
                break;

            case "pntext":
                // The bullet fallback text — not document content; the group marks a bulleted paragraph.
                paragraph.Bullet = true;
                return SkipGroup(rtf, i, stack, detectBullet: null);

            case "fonttbl" or "stylesheet" or "info" or "pict":
                return SkipGroup(rtf, i, stack, detectBullet: null);

            case "colortbl":
                return ReadColorTable(rtf, i, stack, colors);
        }

        return i;
    }

    /// <summary>
    /// Skips to the end of the group the reader is currently inside (the matching unbalanced
    /// <c>'}'</c>), popping the state that group's <c>'{'</c> pushed. When
    /// <paramref name="detectBullet"/> is given, a <c>\pnlvlblt</c> inside the skipped content marks
    /// that paragraph as bulleted.
    /// </summary>
    private static int SkipGroup(string rtf, int i, Stack<CharState> stack, RichParagraph? detectBullet)
    {
        var depth = 0;
        var start = i;
        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '\\' && i + 1 < rtf.Length)
            {
                i += 2;
                continue;
            }

            if (c == '{')
                ++depth;
            else if (c == '}')
            {
                if (depth == 0)
                    break;

                --depth;
            }

            ++i;
        }

        if (detectBullet is not null && rtf.AsSpan(start, i - start).Contains("pnlvlblt", StringComparison.Ordinal))
            detectBullet.Bullet = true;

        if (stack.Count != 0)
            stack.Pop();

        return i + 1; // past the closing brace
    }

    /// <summary>Parses the color table's <c>\red…\green…\blue…;</c> entries up to the group's end.</summary>
    private static int ReadColorTable(string rtf, int i, Stack<CharState> stack, List<Color> colors)
    {
        int red = 0, green = 0, blue = 0;
        var sawComponent = false;
        var first = true;
        while (i < rtf.Length && rtf[i] != '}')
        {
            var c = rtf[i];
            if (c == '\\')
            {
                ++i;
                var start = i;
                while (i < rtf.Length && char.IsAsciiLetter(rtf[i]))
                    ++i;

                var word = rtf[start..i];
                var parameter = 0;
                while (i < rtf.Length && char.IsAsciiDigit(rtf[i]))
                    parameter = parameter * 10 + (rtf[i++] - '0');

                switch (word)
                {
                    case "red": red = parameter; sawComponent = true; break;
                    case "green": green = parameter; sawComponent = true; break;
                    case "blue": blue = parameter; sawComponent = true; break;
                }

                continue;
            }

            if (c == ';')
            {
                // The conventional empty first entry is the automatic color, not a table entry.
                if (sawComponent || !first)
                    colors.Add(sawComponent ? Color.FromArgb(red, green, blue) : Color.Empty);

                red = green = blue = 0;
                sawComponent = false;
                first = false;
            }

            ++i;
        }

        if (stack.Count != 0)
            stack.Pop();

        return i + 1; // past the closing brace
    }

    /// <summary>
    /// Collapses an alignment to its horizontal component, expressed on the top row — the shared
    /// interpretation of paragraph alignment across the serializer and the peers.
    /// </summary>
    public static ContentAlignment HorizontalComponent(ContentAlignment alignment) => alignment switch
    {
        ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => ContentAlignment.TopCenter,
        ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => ContentAlignment.TopRight,
        _ => ContentAlignment.TopLeft,
    };
}
