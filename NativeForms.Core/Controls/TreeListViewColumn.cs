namespace Hawkynt.NativeForms;

/// <summary>
/// A single column in a <see cref="TreeListView"/>'s <see cref="TreeListView.Columns"/> collection: a
/// regular <see cref="ColumnHeader"/> (caption, width, alignment) plus the reflection-free
/// <see cref="TextSelector"/> that produces the cell text for a node. The first column is the tree
/// column — it renders the hierarchy and the node's own <see cref="TreeNode.Text"/>, so its selector
/// is ignored.
/// </summary>
public sealed class TreeListViewColumn : ColumnHeader
{
    /// <summary>Creates an empty column of default width.</summary>
    public TreeListViewColumn() { }

    /// <summary>Creates a column with the given caption.</summary>
    /// <param name="text">The header caption.</param>
    public TreeListViewColumn(string text) : base(text) { }

    /// <summary>Creates a column with the given caption and width.</summary>
    /// <param name="text">The header caption.</param>
    /// <param name="width">The column width in pixels.</param>
    public TreeListViewColumn(string text, int width) : base(text, width) { }

    /// <summary>Creates a column with caption, width and cell-text selector.</summary>
    /// <param name="text">The header caption.</param>
    /// <param name="width">The column width in pixels.</param>
    /// <param name="textSelector">Maps a node to this column's cell text.</param>
    public TreeListViewColumn(string text, int width, Func<TreeNode, string> textSelector) : base(text, width)
        => this.TextSelector = textSelector;

    /// <summary>
    /// Maps a node to this column's cell text; <see langword="null"/> (or a <see langword="null"/>
    /// result) renders an empty cell. Ignored for the tree column (index 0).
    /// </summary>
    public Func<TreeNode, string>? TextSelector
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.OnChanged();
        }
    }

    /// <summary>
    /// Whether the column is pinned to the left edge, staying put while
    /// <see cref="TreeListView.HorizontalOffset"/> scrolls the rest underneath it.
    /// </summary>
    /// <remarks>
    /// The frozen columns are the leading run of the list: freezing the third column without the two
    /// before it would leave a pinned column with a hole beside it, so everything up to the last
    /// frozen one is pinned. A table whose every column is frozen simply does not scroll.
    /// </remarks>
    public bool Frozen
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.OnChanged();
        }
    }
}
