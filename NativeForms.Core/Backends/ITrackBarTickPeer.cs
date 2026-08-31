namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// Optional capability implemented by native <see cref="ITrackBarPeer"/> instances that can render
/// the managed track bar's tick marks faithfully.
/// </summary>
/// <remarks>
/// Keeping ticks in a separate capability preserves the existing peer ABI and lets a backend decline
/// configurations its platform widget cannot reproduce exactly. The core then uses its owner-drawn
/// implementation rather than silently dropping or approximating the requested marks.
/// </remarks>
public interface ITrackBarTickPeer
{
    /// <summary>Whether this peer can represent the requested tick layout exactly.</summary>
    bool SupportsTicks(int minimum, int maximum, int frequency, TickStyle style);

    /// <summary>Applies the requested tick layout.</summary>
    void SetTicks(int minimum, int maximum, int frequency, TickStyle style);
}
