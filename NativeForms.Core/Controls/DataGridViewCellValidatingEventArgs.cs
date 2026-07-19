namespace Hawkynt.NativeForms;

/// <summary>
/// Carries the value a <see cref="DataGridView"/> edit is about to commit
/// (<see cref="DataGridView.CellValidating"/>): the edited text for a
/// <see cref="DataGridViewColumnKind.Text"/> cell, the <see cref="decimal"/> for a
/// <see cref="DataGridViewColumnKind.NumericUpDown"/> cell, the chosen item for a
/// <see cref="DataGridViewColumnKind.ComboBox"/> cell and the <see cref="System.DateTime"/> for a
/// <see cref="DataGridViewColumnKind.DateTime"/> cell. Setting <see cref="Cancel"/> vetoes the
/// commit and keeps the cell in edit mode. Row indices refer to <see cref="DataGridView.Items"/>
/// (the model order), so they stay stable while the grid is sorted.
/// </summary>
public sealed class DataGridViewCellValidatingEventArgs(int rowIndex, int columnIndex, object? proposedValue) : EventArgs
{
    /// <summary>The row's index into <see cref="DataGridView.Items"/>.</summary>
    public int RowIndex { get; } = rowIndex;

    /// <summary>The column's index into <see cref="DataGridView.Columns"/>.</summary>
    public int ColumnIndex { get; } = columnIndex;

    /// <summary>The value the edit wants to commit, typed by the column's kind.</summary>
    public object? ProposedValue { get; } = proposedValue;

    /// <summary>Set by a handler to veto the commit; the cell stays in edit mode.</summary>
    public bool Cancel { get; set; }
}
