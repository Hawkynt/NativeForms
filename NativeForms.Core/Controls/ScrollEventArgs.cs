namespace Hawkynt.NativeForms;

/// <summary>The user gesture behind a <see cref="ScrollBar.Scroll"/> notification, matching
/// <c>System.Windows.Forms.ScrollEventType</c>.</summary>
public enum ScrollEventType
{
    /// <summary>The value moved one <see cref="ScrollBar.SmallChange"/> toward the minimum.</summary>
    SmallDecrement,

    /// <summary>The value moved one <see cref="ScrollBar.SmallChange"/> toward the maximum.</summary>
    SmallIncrement,

    /// <summary>The value moved one <see cref="ScrollBar.LargeChange"/> toward the minimum.</summary>
    LargeDecrement,

    /// <summary>The value moved one <see cref="ScrollBar.LargeChange"/> toward the maximum.</summary>
    LargeIncrement,

    /// <summary>The thumb was dropped at a new position.</summary>
    ThumbPosition,

    /// <summary>The thumb is being dragged; one notification per position change.</summary>
    ThumbTrack,

    /// <summary>The value jumped to the minimum.</summary>
    First,

    /// <summary>The value jumped to the maximum.</summary>
    Last,

    /// <summary>The scroll gesture ended.</summary>
    EndScroll,
}

/// <summary>Describes a scroll gesture on a <see cref="ScrollBar"/>.</summary>
public sealed class ScrollEventArgs(ScrollEventType type, int newValue) : EventArgs
{
    /// <summary>The gesture that produced the notification.</summary>
    public ScrollEventType Type { get; } = type;

    /// <summary>The value the gesture scrolled to.</summary>
    public int NewValue { get; } = newValue;
}
