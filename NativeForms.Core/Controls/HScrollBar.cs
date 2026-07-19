namespace Hawkynt.NativeForms;

/// <summary>A horizontal <see cref="ScrollBar"/>: arrows at the left and right ends, the thumb
/// travels along the x-axis.</summary>
public class HScrollBar : ScrollBar
{
    /// <inheritdoc/>
    private protected override bool IsVertical => false;
}
