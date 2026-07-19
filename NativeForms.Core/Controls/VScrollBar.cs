namespace Hawkynt.NativeForms;

/// <summary>A vertical <see cref="ScrollBar"/>: arrows at the top and bottom ends, the thumb
/// travels along the y-axis.</summary>
public class VScrollBar : ScrollBar
{
    /// <inheritdoc/>
    private protected override bool IsVertical => true;
}
