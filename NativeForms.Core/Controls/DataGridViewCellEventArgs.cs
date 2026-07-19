namespace Hawkynt.NativeForms;

/// <summary>
/// Identifies the cell a <see cref="DataGridView"/> click event refers to. Row indices refer to
/// <see cref="DataGridView.Items"/> (the model order), so they stay stable while the grid is sorted.
/// </summary>
public sealed class DataGridViewCellEventArgs(int rowIndex, int columnIndex, int contentIndex = -1) : EventArgs
{
    /// <summary>The row's index into <see cref="DataGridView.Items"/>.</summary>
    public int RowIndex { get; } = rowIndex;

    /// <summary>The column's index into <see cref="DataGridView.Columns"/>.</summary>
    public int ColumnIndex { get; } = columnIndex;

    /// <summary>The index of the clicked content element within the cell (the icon index in a
    /// <see cref="DataGridViewColumnKind.MultiImage"/> cell), or -1 when not applicable.</summary>
    public int ContentIndex { get; } = contentIndex;
}
