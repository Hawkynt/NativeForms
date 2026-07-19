using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A single column in a <see cref="ListView"/>'s <see cref="ListView.Columns"/> collection (Details
/// view). Carries the header caption, the pixel width of the column and how its cell text is aligned.
/// </summary>
public sealed class ColumnHeader
{
    /// <summary>Creates an empty column of default width.</summary>
    public ColumnHeader() { }

    /// <summary>Creates a column with the given caption.</summary>
    /// <param name="text">The header caption.</param>
    public ColumnHeader(string text) => this.Text = text;

    /// <summary>Creates a column with the given caption and width.</summary>
    /// <param name="text">The header caption.</param>
    /// <param name="width">The column width in pixels.</param>
    public ColumnHeader(string text, int width)
    {
        this.Text = text;
        this.Width = width;
    }

    /// <summary>The header caption.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The column width in pixels. Defaults to 120.</summary>
    public int Width { get; set; } = 120;

    /// <summary>How the header and cell text are aligned. Defaults to <see cref="ContentAlignment.MiddleLeft"/>.</summary>
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleLeft;
}
