namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// An opaque, backend-owned bitmap — the handle a control holds for an icon it draws (list items,
/// combo entries, tree nodes, toolbar buttons). Created decoder-free from 32-bit ARGB pixels via
/// <c>IPlatformBackend.CreateImage</c>, so the core needs no image-loading dependency.
/// </summary>
public interface IImage : IDisposable
{
    /// <summary>The bitmap width in pixels.</summary>
    int Width { get; }

    /// <summary>The bitmap height in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// A greyed-out copy of this image for a disabled control, or <see langword="null"/> when there is
    /// none. A control draws this instead of computing the grey itself, so the disabled look lives with
    /// the image. Rendering backends realise it lazily from their own pixels; the headless and
    /// benchmark fakes have no pixels and return <see langword="null"/>.
    /// </summary>
    IImage? DisabledImage => null;
}
