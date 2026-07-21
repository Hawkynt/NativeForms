namespace Hawkynt.NativeForms;

/// <summary>Identifies the pane an <see cref="Accordion"/> event is about.</summary>
public sealed class AccordionPaneEventArgs(AccordionPane pane, int index) : EventArgs
{
    /// <summary>The pane the event concerns.</summary>
    public AccordionPane Pane { get; } = pane;

    /// <summary>The pane's index within <see cref="Accordion.Panes"/>.</summary>
    public int Index { get; } = index;
}

/// <summary>
/// Identifies the pane an <see cref="Accordion"/> is about to expand, and lets a handler veto it by
/// setting <see cref="System.ComponentModel.CancelEventArgs.Cancel"/> — the pane then stays closed
/// and no other pane is collapsed on its behalf.
/// </summary>
public sealed class AccordionPaneCancelEventArgs(AccordionPane pane, int index)
    : System.ComponentModel.CancelEventArgs
{
    /// <summary>The pane about to expand.</summary>
    public AccordionPane Pane { get; } = pane;

    /// <summary>The pane's index within <see cref="Accordion.Panes"/>.</summary>
    public int Index { get; } = index;
}
