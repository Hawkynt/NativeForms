using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A single row in a <see cref="ListView"/>. <see cref="Text"/> is the primary (first-column) label;
/// <see cref="SubItems"/> supplies the text for the remaining Details-view columns and <see cref="Image"/>
/// is the optional leading icon.
/// </summary>
public sealed class ListViewItem
{
    /// <summary>Creates an empty item.</summary>
    public ListViewItem() { }

    /// <summary>Creates an item with the given primary text.</summary>
    /// <param name="text">The first-column label.</param>
    public ListViewItem(string text) => this.Text = text;

    /// <summary>Creates an item with primary text and additional column texts.</summary>
    /// <param name="text">The first-column label.</param>
    /// <param name="subItems">The texts for the remaining columns.</param>
    public ListViewItem(string text, params string[] subItems)
    {
        this.Text = text;
        this.SubItems.AddRange(subItems);
    }

    /// <summary>The primary label, shown in the first column and in <see cref="ListViewView.List"/> view.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The texts for the additional Details-view columns, in column order.</summary>
    public List<string> SubItems { get; } = [];

    /// <summary>The optional leading icon; <see langword="null"/> for none.</summary>
    public IImage? Image { get; set; }

    /// <summary>Arbitrary caller data associated with the item.</summary>
    public object? Tag { get; set; }

    /// <summary>Whether the item is currently selected.</summary>
    public bool Selected { get; set; }
}
