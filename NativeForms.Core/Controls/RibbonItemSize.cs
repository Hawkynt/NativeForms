namespace Hawkynt.NativeForms;

/// <summary>How much room a <see cref="RibbonItem"/> claims inside its <see cref="RibbonGroup"/>.</summary>
public enum RibbonItemSize
{
    /// <summary>A big icon above the caption, filling the group's full content height — the
    /// prominent, single-column form. The default.</summary>
    Large,

    /// <summary>A small icon beside the caption, one third of the content height, so three of them
    /// stack into one column. The stacking is what gives a ribbon its shape.</summary>
    Small,
}
