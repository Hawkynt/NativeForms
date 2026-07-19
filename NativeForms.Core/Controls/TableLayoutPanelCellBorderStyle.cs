namespace Hawkynt.NativeForms;

/// <summary>The grid lines a <see cref="TableLayoutPanel"/> paints between its cells.</summary>
public enum TableLayoutPanelCellBorderStyle
{
    /// <summary>No grid lines; cells touch.</summary>
    None,

    /// <summary>A single themed line around every cell; each line adds one pixel to the grid.</summary>
    Single,
}
