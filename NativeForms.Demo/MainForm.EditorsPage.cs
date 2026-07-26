using System.Drawing;
using System.Linq;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>A model the property grid inspects through delegate get/set (never reflection).</summary>
    private sealed class WidgetModel
    {
        public string Name = "Save button";
        public bool Enabled = true;
        public int Width = 120;
        public string Align = "Center";
        public string Accent = "#FF0078D4";
    }

    /// <summary>The Editors page (§7.10): a reflection-free <see cref="PropertyGrid"/> inspecting a model.</summary>
    private TabPage BuildEditorsPage()
    {
        var page = new TabPage("Editors") { ImageIndex = _IconPurple };
        var model = new WidgetModel();

        var grid = new PropertyGrid { Bounds = new(16, 36, 380, 380) };
        grid.AddRow(new PropertyGridRow("Name", () => model.Name, v => model.Name = v)
        {
            Category = "Appearance",
            Description = "The caption shown on the widget.",
        });
        grid.AddRow(new PropertyGridRow("Accent", () => model.Accent, v => model.Accent = v)
        {
            Category = "Appearance",
            Editor = PropertyGridEditor.Color,
            Description = "The widget's accent colour (hex RRGGBBAA).",
        });
        grid.AddRow(new PropertyGridRow("Enabled", () => model.Enabled ? "True" : "False", v => model.Enabled = v == "True")
        {
            Category = "Behavior",
            Editor = PropertyGridEditor.Boolean,
            Description = "Whether the widget responds to input.",
        });
        grid.AddRow(new PropertyGridRow("Width", () => model.Width.ToString(), v => { if (int.TryParse(v, out var w)) model.Width = w; })
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Number,
            Description = "The widget width in pixels.",
        });
        grid.AddRow(new PropertyGridRow("Align", () => model.Align, v => model.Align = v)
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Choice,
            Choices = new[] { "Left", "Center", "Right" },
            Description = "Horizontal alignment of the caption.",
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
