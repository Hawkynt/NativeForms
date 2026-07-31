namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="Button"/> presents its face.</summary>
/// <remarks>
/// The first two are what the platform draws and keep the native widget; the last two are not
/// something any platform button offers, so they are the promotion gate (PRD §12) and are painted in
/// the desktop's own colours instead.
/// </remarks>
public enum FlatStyle
{
    /// <summary>The platform's ordinary push button. The default.</summary>
    Standard,

    /// <summary>The platform's push button, drawn entirely by the OS — the same widget as
    /// <see cref="Standard"/> here, since this toolkit never draws over one.</summary>
    System,

    /// <summary>A flat face: the fill and the caption, with no frame until the pointer is on it.</summary>
    Flat,

    /// <summary>Flat at rest, raising into the full button frame under the pointer.</summary>
    Popup,
}
