namespace Hawkynt.NativeForms;

/// <summary>How many panes of an <see cref="Accordion"/> may be open at once.</summary>
public enum AccordionExpandMode
{
    /// <summary>Outlook-style: expanding a pane collapses every other one, so exactly one body is
    /// ever on screen. The default.</summary>
    Single,

    /// <summary>Every pane toggles independently; any number of them can be open, and the open ones
    /// share the height left over by the headers.</summary>
    Multiple,
}
