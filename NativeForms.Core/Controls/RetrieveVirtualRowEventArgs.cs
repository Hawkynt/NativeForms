namespace Hawkynt.NativeForms;

/// <summary>Requests the row item at <see cref="RowIndex"/> while a <see cref="DataGridView"/> is in
/// <see cref="DataGridView.VirtualMode"/>; the handler assigns <see cref="Item"/>.</summary>
public sealed class RetrieveVirtualRowEventArgs(int rowIndex) : EventArgs
{
    /// <summary>The zero-based model index of the row being fetched.</summary>
    public int RowIndex { get; } = rowIndex;

    /// <summary>Set by the handler to the object the row's cells read through the column selectors.</summary>
    public object? Item { get; set; }

    /// <summary>Set by the handler (in the unknown-size mode, <see cref="DataGridView.VirtualRowCount"/>
    /// = -1) when <see cref="RowIndex"/> is past the end, so the grid stops probing and fixes its extent.
    /// Leaving <see cref="Item"/> null has the same effect.</summary>
    public bool EndOfRows { get; set; }
}
