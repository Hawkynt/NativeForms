namespace Hawkynt.NativeForms;

/// <summary>
/// Identifies the cell a cancelable <see cref="DataGridView"/> event refers to
/// (<see cref="DataGridView.CellBeginEdit"/>); setting <see cref="Cancel"/> vetoes the operation.
/// Row indices refer to <see cref="DataGridView.Items"/> (the model order), so they stay stable while
/// the grid is sorted.
/// </summary>
public sealed class DataGridViewCellCancelEventArgs(int rowIndex, int columnIndex) : EventArgs
{
    /// <summary>The row's index into <see cref="DataGridView.Items"/>.</summary>
    public int RowIndex { get; } = rowIndex;

    /// <summary>The column's index into <see cref="DataGridView.Columns"/>.</summary>
    public int ColumnIndex { get; } = columnIndex;

    /// <summary>Set by a handler to veto the operation.</summary>
    public bool Cancel { get; set; }
}
