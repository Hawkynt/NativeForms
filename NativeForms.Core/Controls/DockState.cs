namespace Hawkynt.NativeForms;

/// <summary>
/// Where a <see cref="DockContent"/> lives inside its owning <see cref="DockPanel"/> — the
/// Visual-Studio docking states. The edge a docked or auto-hidden pane clings to is carried
/// separately by <see cref="DockContent.DockEdge"/>.
/// </summary>
public enum DockState
{
    /// <summary>Not shown anywhere: no caption, no tab, no edge strip. The pane keeps its children
    /// but their peers are vetoed, so it costs nothing until it is shown again.</summary>
    Hidden,

    /// <summary>Pinned to one of the four edges (see <see cref="DockContent.DockEdge"/>), sharing a
    /// tab well with any siblings docked to the same region and separated from the rest by a
    /// draggable splitter.</summary>
    Docked,

    /// <summary>In the central document tab well — the editor area a VS-style shell fills with open
    /// files. Several documents share one tab strip.</summary>
    Document,

    /// <summary>In its own top-level window, draggable and independent of the panel.</summary>
    Floating,

    /// <summary>Collapsed to a labelled strip on its edge; hovering or clicking the strip flies the
    /// pane out over the content, and it slides back when focus leaves.</summary>
    AutoHide,
}

/// <summary>The edge a docked or auto-hidden <see cref="DockContent"/> clings to.</summary>
public enum DockEdge
{
    /// <summary>The left edge.</summary>
    Left,

    /// <summary>The top edge.</summary>
    Top,

    /// <summary>The right edge.</summary>
    Right,

    /// <summary>The bottom edge.</summary>
    Bottom,
}

/// <summary>
/// A directional docking target offered by the drag overlay. The four edges split the region the
/// pointer is over (or the whole panel, for the outer guides); <see cref="Center"/> drops the pane
/// into the hovered group as a new tab.
/// </summary>
public enum DockGuide
{
    /// <summary>No guide is under the pointer.</summary>
    None,

    /// <summary>Dock to the left of the hovered region.</summary>
    Left,

    /// <summary>Dock above the hovered region.</summary>
    Top,

    /// <summary>Dock to the right of the hovered region.</summary>
    Right,

    /// <summary>Dock below the hovered region.</summary>
    Bottom,

    /// <summary>Drop into the hovered group as a new tab.</summary>
    Center,

    /// <summary>Dock against the panel's own left edge, spanning its full height.</summary>
    PanelLeft,

    /// <summary>Dock against the panel's own top edge, spanning its full width.</summary>
    PanelTop,

    /// <summary>Dock against the panel's own right edge, spanning its full height.</summary>
    PanelRight,

    /// <summary>Dock against the panel's own bottom edge, spanning its full width.</summary>
    PanelBottom,
}
