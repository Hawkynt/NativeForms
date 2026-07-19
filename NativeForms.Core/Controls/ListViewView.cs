namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="ListView"/> arranges its items, matching <c>System.Windows.Forms.View</c>. Only
/// <see cref="Details"/> and <see cref="List"/> are painted today; the icon and tile layouts are
/// reserved for a later milestone and currently fall back to the <see cref="List"/> layout.
/// </summary>
public enum ListViewView
{
    /// <summary>Multi-column grid with an optional header row.</summary>
    Details,

    /// <summary>Single-column vertical list of item text and icon.</summary>
    List,

    /// <summary>Large icons above wrapped labels. TODO: not yet implemented.</summary>
    LargeIcon,

    /// <summary>Small icons beside labels. TODO: not yet implemented.</summary>
    SmallIcon,

    /// <summary>Large icon with a block of item text. TODO: not yet implemented.</summary>
    Tile,
}
