namespace Hawkynt.NativeForms;

/// <summary>The edge a <see cref="FlowLayoutPanel"/> flows its children from.</summary>
public enum FlowDirection
{
    /// <summary>Children flow rightward from the left edge, wrapping into rows below.</summary>
    LeftToRight,

    /// <summary>Children flow downward from the top edge, wrapping into columns to the right.</summary>
    TopDown,

    /// <summary>Children flow leftward from the right edge, wrapping into rows below.</summary>
    RightToLeft,

    /// <summary>Children flow upward from the bottom edge, wrapping into columns to the right.</summary>
    BottomUp,
}
