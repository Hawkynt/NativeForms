namespace Hawkynt.NativeForms;

/// <summary>
/// The container edges a control is bound to (see <see cref="Control.Anchor"/>). When the container
/// resizes, every anchored edge keeps its distance to the matching edge of the container's
/// <see cref="Control.DisplayRectangle"/>: a single anchor translates the control, opposing anchors
/// stretch it, and <see cref="None"/> lets it drift by half the resize delta — the Windows Forms
/// resize model.
/// </summary>
[Flags]
public enum AnchorStyles
{
    /// <summary>No edge is bound; the control keeps its position relative to the container's center.</summary>
    None = 0,

    /// <summary>The top edge keeps its distance to the container's top.</summary>
    Top = 1,

    /// <summary>The bottom edge keeps its distance to the container's bottom.</summary>
    Bottom = 2,

    /// <summary>The left edge keeps its distance to the container's left.</summary>
    Left = 4,

    /// <summary>The right edge keeps its distance to the container's right.</summary>
    Right = 8,
}
