using System.Drawing;
using System.Linq;

namespace Hawkynt.NativeForms.Demo;

internal enum WidgetDock { None, Left, Top, Right, Bottom, Fill }

[System.Flags]
internal enum WidgetEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>A model the property grid inspects through delegate get/set (never reflection).</summary>
internal sealed class WidgetModel
{
    public string Name = "Save button";
    public bool Enabled = true;
    public bool? Visible = null;
    public int Width = 120;
    public WidgetDock Dock = WidgetDock.Top;
    public WidgetEdges Anchor = WidgetEdges.Left | WidgetEdges.Top;
    public System.DateOnly Created = new(2026, 7, 26);
    public System.TimeOnly Reminder = new(9, 30);
    public string Align = "MiddleCenter";
    public Color Accent = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
}

internal sealed partial class MainForm
{
    /// <summary>The Editors page (§7.10): a reflection-free <see cref="PropertyGrid"/> built with the
    /// strongly-typed row builder and hand-built spatial pickers. (The <c>[GridEditable]</c> source generator
    /// that emits these rows from attributes is exercised in the test project.)</summary>
    private TabPage BuildEditorsPage()
    {
        var page = new TabPage("Editors") { ImageIndex = _IconPurple };
        var model = new WidgetModel();

        var grid = new PropertyGrid { Bounds = new(16, 36, 380, 380) };

        // The strongly-typed builder infers the editor, formatting and parsing from the field type.
        grid.AddRow("Name", () => model.Name, v => model.Name = v, category: "Appearance", description: "The caption shown on the widget.");
        grid.AddRow("Accent", () => model.Accent, v => model.Accent = v, category: "Appearance", description: "The widget's accent colour.");
        grid.AddRow("Enabled", () => model.Enabled, v => model.Enabled = v, category: "Behavior", description: "Whether the widget responds to input.");
        grid.AddRow("Visible", () => model.Visible, v => model.Visible = v, category: "Behavior", description: "A three-state flag (True / False / inherit).");
        grid.AddRow("Created", () => model.Created, v => model.Created = v, category: "Behavior", description: "The creation date (calendar drop-down).");
        grid.AddRow("Reminder", () => model.Reminder, v => model.Reminder = v, category: "Behavior", description: "A time-of-day (clock picker).");
        grid.AddRow("Width", () => model.Width, v => model.Width = v, category: "Layout", description: "The widget width in pixels (0–400).", minimum: 0, maximum: 400);
        grid.AddGridEnumRow("Dock", () => model.Dock, v => model.Dock = v,
            new[] { "", "Top", "", "Left", "Fill", "Right", "None", "Bottom", "" },
            category: "Layout", description: "Which edge the widget docks to (spatial 3×3 flyout).");
        grid.AddFlagsEnumRow("Anchor", () => model.Anchor, v => model.Anchor = v, category: "Layout", description: "The edges the widget anchors to (checkbox flyout).");
        grid.AddRow(new PropertyGridRow("Align", () => model.Align, v => model.Align = v)
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Align,
            Description = "Where the caption sits in its cell (3×3 picker).",
        });
        grid.PropertyValueChanged += (_, e) => this.SetStatus($"PropertyGrid: {e.Row.Name} = {e.NewValue}.");

        var code = new CodeTextBox { Bounds = new(420, 36, 580, 380), TabWidth = 4 };
        code.Tokenizer = TokenizeCSharp;
        code.CompletionProvider = prefix => new[]
            {
                "public", "private", "protected", "internal", "static", "void", "int", "string", "var",
                "return", "class", "struct", "interface", "namespace", "using", "new", "if", "else", "for",
                "foreach", "while", "switch", "Console", "Convert", "Contains",
            }
            .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(s, prefix, StringComparison.Ordinal))
            .ToArray();
        code.Text = string.Join('\n',
            "// a tiny sample",
            "public int Add(int a, int b)",
            "{",
            "    var name = \"sum\";",
            "    return a + b; // 42",
            "}");
        code.TextChanged += (_, _) => this.SetStatus($"CodeTextBox: line {code.CaretLine + 1}, col {code.CaretColumn + 1}.");

        page.Controls.AddRange(
            Caption("PropertyGrid (typed inline editors)", 16, 12, 398),
            grid,
            Caption("CodeTextBox (gutter · current-line · delegate tokenizer)", 420, 12, 560),
            code);

        return page;
    }

    private static readonly System.Collections.Generic.HashSet<string> _csKeywords =
        new(System.StringComparer.Ordinal) { "public", "private", "int", "var", "return", "void", "static", "class", "new", "if", "else", "for", "while" };

    /// <summary>A deliberately small C#-flavoured tokenizer for the demo: line comments, string literals,
    /// numbers and a handful of keywords.</summary>
    private static System.Collections.Generic.IReadOnlyList<CodeToken> TokenizeCSharp(string line)
    {
        var tokens = new System.Collections.Generic.List<CodeToken>();
        var comment = line.IndexOf("//", System.StringComparison.Ordinal);
        var limit = comment < 0 ? line.Length : comment;

        var i = 0;
        while (i < limit)
        {
            var c = line[i];
            if (c == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0)
                    end = limit - 1;

                tokens.Add(new CodeToken(i, end - i + 1, CodeTokenKind.String));
                i = end + 1;
            }
            else if (char.IsDigit(c))
            {
                var start = i;
                while (i < limit && (char.IsLetterOrDigit(line[i]) || line[i] == '.'))
                    ++i;

                tokens.Add(new CodeToken(start, i - start, CodeTokenKind.Number));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < limit && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    ++i;

                if (_csKeywords.Contains(line[start..i]))
                    tokens.Add(new CodeToken(start, i - start, CodeTokenKind.Keyword));
            }
            else
                ++i;
        }

        if (comment >= 0)
            tokens.Add(new CodeToken(comment, line.Length - comment, CodeTokenKind.Comment));

        return tokens;
    }
}
