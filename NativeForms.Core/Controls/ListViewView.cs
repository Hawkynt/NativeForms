namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="ListView"/> arranges its items, matching <c>System.Windows.Forms.View</c>.
/// </summary>
public enum ListViewView
{
    /// <summary>Multi-column grid with an optional header row.</summary>
    Details,

    /// <summary>Single-column vertical list of item text and icon.</summary>
    List,

    /// <summary>A grid of cells: the large icon centered above the label.</summary>
    LargeIcon,

    /// <summary>Small icon beside the label, cells flowing left-to-right in rows.</summary>
    SmallIcon,

    /// <summary>Large icon at the left of a text block: the label above the first sub-item.</summary>
    Tile,
}
