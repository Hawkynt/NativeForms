namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="TableLayoutPanel"/> column or row obtains its size.</summary>
public enum SizeType
{
    /// <summary>Sized to the largest child in the column or row, plus that child's margin.</summary>
    AutoSize,

    /// <summary>A fixed pixel size.</summary>
    Absolute,

    /// <summary>
    /// A share of the space left after absolute and auto-sized tracks, weighted by the style's
    /// value relative to the other percent tracks.
    /// </summary>
    Percent,
}
