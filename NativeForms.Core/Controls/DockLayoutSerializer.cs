using System.Drawing;
using System.Text;

namespace Hawkynt.NativeForms;

/// <summary>
/// Reads and writes a <see cref="DockPanel"/>'s arrangement as a compact, reflection-free string — a
/// hand-rolled <c>Write</c>/<c>Parse</c> pair in the spirit of <see cref="Text.RtfSerializer"/>, never
/// <c>System.Text.Json</c>. The grammar is a bracketed tree of <c>S(orient,ratio,child,child)</c> split
/// nodes and <c>G(isDoc,active,key,…)</c> tab groups, followed by the auto-hide and floating sections;
/// pane keys are percent-escaped so they can hold any character. The reader is tolerant: an
/// unresolvable key is skipped and a malformed token collapses to nothing, so an older or partial
/// layout still loads.
/// </summary>
internal static class DockLayoutSerializer
{
    private const string _Header = "NFDOCK1|";

    // --- Write ------------------------------------------------------------------------------------

    internal static string Save(DockPanel panel)
    {
        var sb = new StringBuilder(_Header);
        WriteNode(sb, panel.RootNode);
        sb.Append('|');
        WriteAutoHide(sb, panel.AutoHideContents);
        sb.Append('|');
        WriteFloating(sb, panel.FloatingEntries());
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, DockNode? node)
    {
        switch (node)
        {
            case DockTabGroupNode group:
                sb.Append("G(").Append(group.IsDocument ? '1' : '0').Append(',').Append(group.ActiveIndex);
                for (var i = 0; i < group.Contents.Count; ++i)
                    sb.Append(',').Append(Escape(group.Contents[i].Key));
                sb.Append(')');
                break;
            case DockSplitNode split:
                sb.Append("S(")
                    .Append(split.Orientation == Orientation.Vertical ? 'V' : 'H').Append(',')
                    .Append(Math.Clamp((int)Math.Round(split.Ratio * 1000), 0, 1000)).Append(',');
                WriteNode(sb, split.First);
                sb.Append(',');
                WriteNode(sb, split.Second);
                sb.Append(')');
                break;
            default:
                sb.Append('-');
                break;
        }
    }

    private static void WriteAutoHide(StringBuilder sb, IReadOnlyList<DockContent> panes)
    {
        sb.Append("A(");
        for (var i = 0; i < panes.Count; ++i)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(EdgeLetter(panes[i].DockEdge)).Append(':').Append(Escape(panes[i].Key));
        }

        sb.Append(')');
    }

    private static void WriteFloating(StringBuilder sb, IReadOnlyList<(DockContent Content, Rectangle Bounds)> entries)
    {
        sb.Append("F(");
        for (var i = 0; i < entries.Count; ++i)
        {
            if (i > 0)
                sb.Append(',');
            var (content, b) = entries[i];
            sb.Append(Escape(content.Key)).Append('@')
                .Append(b.X).Append('@').Append(b.Y).Append('@').Append(b.Width).Append('@').Append(b.Height);
        }

        sb.Append(')');
    }

    // --- Read -------------------------------------------------------------------------------------

    internal static void Load(DockPanel panel, string layout, Func<string, DockContent?> resolve)
    {
        panel.ResetForLoad();
        if (!layout.StartsWith(_Header, StringComparison.Ordinal))
            return;

        var body = layout.Substring(_Header.Length);
        var parts = body.Split('|');
        var treeStr = parts.Length > 0 ? parts[0] : string.Empty;
        var ahStr = parts.Length > 1 ? parts[1] : string.Empty;
        var flStr = parts.Length > 2 ? parts[2] : string.Empty;

        DockContent? Get(string key)
        {
            var content = resolve(key);
            if (content is not null)
                panel.EnsureOwnedForLoad(content);
            return content;
        }

        var i = 0;
        var root = ParseNode(treeStr, ref i, Get);
        panel.ApplyLoadedTree(root);

        ParseAutoHide(ahStr, Get, panel);
        ParseFloating(flStr, Get, panel);
        panel.FinishLoad();
    }

    private static DockNode? ParseNode(string s, ref int i, Func<string, DockContent?> get)
    {
        if (i >= s.Length)
            return null;

        switch (s[i])
        {
            case '-':
                ++i;
                return null;
            case 'G':
                return ParseGroup(s, ref i, get);
            case 'S':
                return ParseSplit(s, ref i, get);
            default:
                ++i;
                return null;
        }
    }

    private static DockNode? ParseGroup(string s, ref int i, Func<string, DockContent?> get)
    {
        // "G(" already at i.
        i += 2;
        var end = s.IndexOf(')', i);
        if (end < 0)
        {
            i = s.Length;
            return null;
        }

        var fields = s.Substring(i, end - i).Split(',');
        i = end + 1;

        var group = new DockTabGroupNode { IsDocument = fields.Length > 0 && fields[0] == "1" };
        var active = fields.Length > 1 && int.TryParse(fields[1], out var a) ? a : 0;
        for (var f = 2; f < fields.Length; ++f)
        {
            if (get(Unescape(fields[f])) is { } content)
                group.Contents.Add(content);
        }

        if (group.Contents.Count == 0)
            return null;

        group.ActiveIndex = Math.Clamp(active, 0, group.Contents.Count - 1);
        return group;
    }

    private static DockNode? ParseSplit(string s, ref int i, Func<string, DockContent?> get)
    {
        // "S(" at i.
        i += 2;
        var orientation = i < s.Length && s[i] == 'H' ? Orientation.Horizontal : Orientation.Vertical;
        ++i; // orient letter
        if (i < s.Length && s[i] == ',')
            ++i;

        var ratio = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            ratio = ratio * 10 + (s[i++] - '0');
        if (i < s.Length && s[i] == ',')
            ++i;

        var first = ParseNode(s, ref i, get);
        if (i < s.Length && s[i] == ',')
            ++i;
        var second = ParseNode(s, ref i, get);
        if (i < s.Length && s[i] == ')')
            ++i;

        // Collapse away a side that resolved to nothing.
        if (first is null)
            return second;
        if (second is null)
            return first;

        return new DockSplitNode(orientation, Math.Clamp(ratio, 0, 1000) / 1000.0, first, second);
    }

    private static void ParseAutoHide(string s, Func<string, DockContent?> get, DockPanel panel)
    {
        var inner = Inner(s, 'A');
        if (inner.Length == 0)
            return;

        foreach (var entry in inner.Split(','))
        {
            var colon = entry.IndexOf(':');
            if (colon <= 0)
                continue;
            var edge = LetterEdge(entry[0]);
            if (get(Unescape(entry.Substring(colon + 1))) is { } content)
                panel.AddAutoHideLoaded(content, edge);
        }
    }

    private static void ParseFloating(string s, Func<string, DockContent?> get, DockPanel panel)
    {
        var inner = Inner(s, 'F');
        if (inner.Length == 0)
            return;

        foreach (var entry in inner.Split(','))
        {
            var at = entry.Split('@');
            if (at.Length != 5)
                continue;
            if (get(Unescape(at[0])) is not { } content)
                continue;
            var bounds = new Rectangle(ParseInt(at[1]), ParseInt(at[2]), ParseInt(at[3]), ParseInt(at[4]));
            panel.AddFloatingLoaded(content, bounds);
        }
    }

    // --- Helpers ----------------------------------------------------------------------------------

    private static string Inner(string s, char tag)
    {
        if (s.Length < 3 || s[0] != tag || s[1] != '(' || s[^1] != ')')
            return string.Empty;
        return s.Substring(2, s.Length - 3);
    }

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

    private static char EdgeLetter(DockEdge edge) => edge switch
    {
        DockEdge.Left => 'L',
        DockEdge.Top => 'T',
        DockEdge.Right => 'R',
        _ => 'B',
    };

    private static DockEdge LetterEdge(char c) => c switch
    {
        'T' => DockEdge.Top,
        'R' => DockEdge.Right,
        'B' => DockEdge.Bottom,
        _ => DockEdge.Left,
    };

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder? sb = null;
        for (var i = 0; i < value.Length; ++i)
        {
            var c = value[i];
            var reserved = c is '%' or '(' or ')' or ',' or '|' or '@' or ':';
            if (reserved)
            {
                sb ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
                sb.Append('%').Append(((int)c).ToString("X2"));
            }
            else
                sb?.Append(c);
        }

        return sb?.ToString() ?? value;
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf('%') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; ++i)
        {
            if (value[i] == '%' && i + 2 < value.Length
                && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                sb.Append((char)((Uri.FromHex(value[i + 1]) << 4) + Uri.FromHex(value[i + 2])));
                i += 2;
            }
            else
                sb.Append(value[i]);
        }

        return sb.ToString();
    }
}
