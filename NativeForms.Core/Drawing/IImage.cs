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
}
