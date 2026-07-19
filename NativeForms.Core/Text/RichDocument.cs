using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Text;

/// <summary>
/// A run of characters sharing one character format — the atom of the rich-text model. A default
/// <see cref="Color"/> (<see cref="Color.Empty"/>) and a <see cref="FontSize"/> of 0 mean "inherit
/// the control's defaults", so plain text costs nothing to describe.
/// </summary>
public readonly struct RichTextRun(string text, FontStyle style = FontStyle.Regular, Color color = default, float fontSize = 0f)
{
    /// <summary>The run's characters (never a line break — paragraphs model those).</summary>
    public string Text { get; } = text ?? string.Empty;

    /// <summary>The style flags (bold/italic/underline/strikeout) of every character in the run.</summary>
    public FontStyle Style { get; } = style;

    /// <summary>The text color, or <see cref="Color.Empty"/> for the default.</summary>
    public Color Color { get; } = color;

    /// <summary>The size in points, or 0 for the default.</summary>
    public float FontSize { get; } = fontSize;

    /// <summary>Whether this run carries the same formatting as <paramref name="other"/>.</summary>
    public bool HasSameFormat(in RichTextRun other)
        => this.Style == other.Style && this.Color == other.Color && this.FontSize.Equals(other.FontSize);
}

/// <summary>A paragraph: a sequence of formatted runs plus the per-paragraph properties.</summary>
public sealed class RichParagraph
{
    /// <summary>The formatted runs the paragraph consists of, in order.</summary>
    public List<RichTextRun> Runs { get; } = [];

    /// <summary>
    /// The paragraph alignment. Only the horizontal component is meaningful (left/center/right);
    /// the vertical component is ignored, mirroring how the peers interpret it.
    /// </summary>
    public ContentAlignment Alignment { get; set; } = ContentAlignment.TopLeft;

    /// <summary>Whether the paragraph is a bulleted list item.</summary>
    public bool Bullet { get; set; }

    /// <summary>The paragraph's characters without formatting.</summary>
    public string ToPlainText()
    {
        var length = 0;
        for (var i = 0; i < this.Runs.Count; ++i)
            length += this.Runs[i].Text.Length;

        var result = new System.Text.StringBuilder(length);
        for (var i = 0; i < this.Runs.Count; ++i)
            result.Append(this.Runs[i].Text);

        return result.ToString();
    }
}

/// <summary>
/// The platform-neutral rich-text document: an ordered list of paragraphs of formatted runs. It is
/// the pivot every backend shares — the core RTF reader/writer serializes it, the headless peer
/// records it, and the GTK peer rebuilds its tag soup from it where the platform has no native RTF.
/// </summary>
public sealed class RichDocument
{
    /// <summary>The document's paragraphs, in order. An empty document renders as empty text.</summary>
    public List<RichParagraph> Paragraphs { get; } = [];

    /// <summary>Builds an unformatted document from plain text, splitting paragraphs on line breaks.</summary>
    public static RichDocument FromPlainText(string text)
    {
        var document = new RichDocument();
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new RichParagraph();
            if (line.Length != 0)
                paragraph.Runs.Add(new(line));

            document.Paragraphs.Add(paragraph);
        }

        return document;
    }

    /// <summary>The document's characters without formatting, paragraphs joined by <c>'\n'</c>.</summary>
    public string ToPlainText()
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < this.Paragraphs.Count; ++i)
        {
            if (i != 0)
                result.Append('\n');

            result.Append(this.Paragraphs[i].ToPlainText());
        }

        return result.ToString();
    }
}
