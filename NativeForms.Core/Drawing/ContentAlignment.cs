namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Alignment of content within a rectangle — the nine anchor points, matching the layout of
/// <c>System.Drawing.ContentAlignment</c>.
/// </summary>
public enum ContentAlignment
{
    /// <summary>Top edge, left aligned.</summary>
    TopLeft,

    /// <summary>Top edge, centered horizontally.</summary>
    TopCenter,

    /// <summary>Top edge, right aligned.</summary>
    TopRight,

    /// <summary>Vertically centered, left aligned.</summary>
    MiddleLeft,

    /// <summary>Centered both ways.</summary>
    MiddleCenter,

    /// <summary>Vertically centered, right aligned.</summary>
    MiddleRight,

    /// <summary>Bottom edge, left aligned.</summary>
    BottomLeft,

    /// <summary>Bottom edge, centered horizontally.</summary>
    BottomCenter,

    /// <summary>Bottom edge, right aligned.</summary>
    BottomRight,
}
