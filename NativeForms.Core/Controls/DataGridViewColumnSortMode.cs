namespace Hawkynt.NativeForms;

/// <summary>Whether and how a <see cref="DataGridViewColumn"/> participates in sorting.</summary>
public enum DataGridViewColumnSortMode
{
    /// <summary>Clicking the header does nothing. The default.</summary>
    NotSortable,

    /// <summary>Clicking the header toggles ascending/descending, ordering rows by
    /// <see cref="DataGridViewColumn.SortComparison"/> when set, otherwise by comparing the values the
    /// column's <see cref="DataGridViewColumn.ValueSelector"/> produces.</summary>
    Automatic,
}
