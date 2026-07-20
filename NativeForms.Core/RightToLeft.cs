namespace Hawkynt.NativeForms;

/// <summary>
/// The text-direction setting of a <see cref="Control"/>, matching
/// <c>System.Windows.Forms.RightToLeft</c>. The ambient default is <see cref="Inherit"/>, so setting
/// a form to <see cref="Yes"/> flips its whole tree unless a child overrides.
/// </summary>
public enum RightToLeft
{
    /// <summary>Content lays out left-to-right.</summary>
    No,

    /// <summary>Content lays out right-to-left; owner-drawn controls mirror their painting.</summary>
    Yes,

    /// <summary>The direction comes from the parent chain (left-to-right when nothing is set).</summary>
    Inherit,
}
