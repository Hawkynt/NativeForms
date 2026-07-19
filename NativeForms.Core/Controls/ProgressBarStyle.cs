namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="ProgressBar"/> presents progress.</summary>
public enum ProgressBarStyle
{
    /// <summary>A determinate accent fill proportional to <see cref="ProgressBar.Value"/>.</summary>
    Blocks,

    /// <summary>An indeterminate accent segment sweeping the track, animated by a timer.</summary>
    Marquee,
}
