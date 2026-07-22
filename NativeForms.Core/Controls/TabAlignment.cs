namespace Hawkynt.NativeForms;

/// <summary>Which edge a <see cref="TabControl"/> paints its header strip along.</summary>
public enum TabAlignment
{
    /// <summary>The strip runs along the top edge (the default).</summary>
    Top,

    /// <summary>The strip runs along the bottom edge.</summary>
    Bottom,

    /// <summary>The strip runs down the left edge, tabs stacked as themed rows.</summary>
    Left,

    /// <summary>The strip runs down the right edge, tabs stacked as themed rows.</summary>
    Right,
}
