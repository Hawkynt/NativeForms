namespace Hawkynt.NativeForms;

/// <summary>
/// A boolean that can also defer to a grid-level default, like its Windows Forms namesake —
/// <see cref="DataGridViewColumn.Resizable"/> uses it to override (or inherit)
/// <see cref="DataGridView.AllowUserToResizeColumns"/> per column.
/// </summary>
public enum DataGridViewTriState
{
    /// <summary>Defer to the grid's setting. The default.</summary>
    NotSet,

    /// <summary>Explicitly enabled, regardless of the grid.</summary>
    True,

    /// <summary>Explicitly disabled, regardless of the grid.</summary>
    False,
}
