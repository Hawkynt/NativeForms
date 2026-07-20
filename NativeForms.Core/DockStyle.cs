namespace Hawkynt.NativeForms;

/// <summary>
/// The container edge a control glues itself to (see <see cref="Control.Dock"/>). Docked siblings
/// claim their edges of the container's <see cref="Control.DisplayRectangle"/> in
/// <see cref="Control.Controls"/> order, each shrinking the rectangle left for the next;
/// <see cref="Fill"/> takes whatever remains. Docking and anchoring are mutually exclusive — the
/// property assigned last wins, exactly like Windows Forms.
/// </summary>
public enum DockStyle
{
    /// <summary>Not docked; the control is positioned by <see cref="Control.Bounds"/> and <see cref="Control.Anchor"/>.</summary>
    None = 0,

    /// <summary>Glued to the top edge, spanning the available width; the control keeps its height.</summary>
    Top = 1,

    /// <summary>Glued to the bottom edge, spanning the available width; the control keeps its height.</summary>
    Bottom = 2,

    /// <summary>Glued to the left edge, spanning the available height; the control keeps its width.</summary>
    Left = 3,

    /// <summary>Glued to the right edge, spanning the available height; the control keeps its width.</summary>
    Right = 4,

    /// <summary>Fills the rectangle left over after every edge-docked sibling took its share.</summary>
    Fill = 5,
}
