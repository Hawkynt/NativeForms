using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The mirroring math owner-drawn controls share when their resolved <see cref="RightToLeft"/> is
/// <see cref="RightToLeft.Yes"/>: geometry computed for left-to-right painting is flipped across the
/// control's vertical center line, and horizontal alignments swap sides. Pure geometry, adopted per
/// control in its paint/hit-test code — containers do not mirror child layout yet (PRD §8).
/// </summary>
internal static class RtlLayout
{
    /// <summary>Flips a client-space rectangle across the vertical center of a <paramref name="width"/>-wide control.</summary>
    public static Rectangle Mirror(Rectangle rect, int width) => new(width - rect.Right, rect.Y, rect.Width, rect.Height);

    /// <summary>Swaps the left and right columns of an alignment; centered values pass through.</summary>
    public static ContentAlignment Mirror(ContentAlignment alignment)
        => alignment switch
        {
            ContentAlignment.TopLeft => ContentAlignment.TopRight,
            ContentAlignment.TopRight => ContentAlignment.TopLeft,
            ContentAlignment.MiddleLeft => ContentAlignment.MiddleRight,
            ContentAlignment.MiddleRight => ContentAlignment.MiddleLeft,
            ContentAlignment.BottomLeft => ContentAlignment.BottomRight,
            ContentAlignment.BottomRight => ContentAlignment.BottomLeft,
            _ => alignment,
        };
}
