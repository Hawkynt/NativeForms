namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="PictureBox"/> fits its image into the client area, matching the semantics of
/// <c>System.Windows.Forms.PictureBoxSizeMode</c>.
/// </summary>
public enum PictureBoxSizeMode
{
    /// <summary>The image sits in the top-left corner at native size and is clipped if larger.</summary>
    Normal,

    /// <summary>The image is stretched to fill the client area, ignoring its aspect ratio.</summary>
    StretchImage,

    /// <summary>The image is centered at native size and is clipped if larger.</summary>
    CenterImage,

    /// <summary>The image is scaled to the largest size that fits, keeping its aspect ratio.</summary>
    Zoom,
}
