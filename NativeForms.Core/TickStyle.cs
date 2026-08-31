namespace Hawkynt.NativeForms;

/// <summary>Specifies where a <see cref="TrackBar"/> displays its tick marks.</summary>
public enum TickStyle
{
    /// <summary>No tick marks are displayed.</summary>
    None,

    /// <summary>Ticks are displayed above a horizontal track or to the left of a vertical track.</summary>
    TopLeft,

    /// <summary>Ticks are displayed below a horizontal track or to the right of a vertical track.</summary>
    BottomRight,

    /// <summary>Ticks are displayed on both sides of the track.</summary>
    Both,
}
