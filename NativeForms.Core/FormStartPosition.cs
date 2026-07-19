namespace Hawkynt.NativeForms;

/// <summary>
/// Where a <see cref="Form"/> is placed when it is first shown. Applied once, at
/// <see cref="Application.Run(Form)"/>/<see cref="Form.ShowDialog"/> time, by computing the final
/// <see cref="Control.Bounds"/> in the core before the window is realized — the peers never see the
/// policy, only the resulting rectangle.
/// </summary>
public enum FormStartPosition
{
    /// <summary>The form appears exactly at its <see cref="Control.Bounds"/>.</summary>
    Manual,

    /// <summary>The form is centered on the primary screen.</summary>
    CenterScreen,

    /// <summary>
    /// The form is centered within its owner's bounds; shown without an owner it falls back to
    /// <see cref="CenterScreen"/>.
    /// </summary>
    CenterParent,
}
