using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The hit-testing predicates owner-drawn input handlers keep re-deriving. Pure geometry, no
/// allocation — safe on the mouse path.
/// </summary>
internal static class HitTest
{
    /// <summary>Whether <paramref name="location"/> lies inside the control's client rectangle —
    /// the "did the release happen over me?" test every click-to-activate control performs.</summary>
    public static bool ClientContains(Control control, Point location)
        => location.X >= 0 && location.Y >= 0 && location.X < control.Width && location.Y < control.Height;
}
