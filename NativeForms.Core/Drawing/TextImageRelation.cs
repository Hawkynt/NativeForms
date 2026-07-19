namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// How an image and a text block share one content rectangle, matching the semantics of
/// <c>System.Windows.Forms.TextImageRelation</c>. Combined with a <see cref="ContentAlignment"/> that
/// anchors the pair as a whole, this fully describes icon+text placement for every control that
/// renders both.
/// </summary>
public enum TextImageRelation
{
    /// <summary>Image and text occupy the same area, each anchored independently.</summary>
    Overlay,

    /// <summary>The image sits to the left of the text.</summary>
    ImageBeforeText,

    /// <summary>The text sits to the left of the image.</summary>
    TextBeforeImage,

    /// <summary>The image sits above the text.</summary>
    ImageAboveText,

    /// <summary>The text sits above the image.</summary>
    TextAboveImage,
}
