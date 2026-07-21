namespace Hawkynt.NativeForms;

/// <summary>
/// One pane of an <see cref="Accordion"/>: a <see cref="Panel"/> whose <see cref="Control.Text"/> is
/// the header caption and whose children are the pane body. The owning accordion draws the header,
/// owns the pane's bounds and vetoes its child peers while the pane is collapsed — a pane is never
/// positioned by the Anchor/Dock engine.
/// </summary>
public class AccordionPane : Panel
{
    /// <summary>Creates an untitled pane.</summary>
    public AccordionPane() { }

    /// <summary>Creates a pane with the given header caption.</summary>
    public AccordionPane(string text) => this.Text = text;

    /// <summary>
    /// The pane's own expansion flag. The <see cref="Accordion"/> is the only writer once the pane is
    /// parented — going through <see cref="Expanded"/> is what applies the expand mode, raises the
    /// events and re-lays the stack out.
    /// </summary>
    internal bool IsExpanded { get; set; }

    /// <summary>
    /// Whether the pane body is shown. Assigning routes through the owning <see cref="Accordion"/>,
    /// so <see cref="AccordionExpandMode.Single"/> still collapses the siblings and
    /// <see cref="Accordion.PaneExpanding"/> can still veto. A detached pane just records the flag.
    /// </summary>
    public bool Expanded
    {
        get => this.IsExpanded;
        set
        {
            if (this.Parent is Accordion accordion)
            {
                accordion.SetPaneExpanded(this, value);
                return;
            }

            this.IsExpanded = value;
        }
    }

    /// <summary>
    /// The index of this pane's icon in the owning <see cref="Accordion.ImageList"/>, or -1 for no
    /// icon. The icon is painted between the toggle glyph and the caption.
    /// </summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            (this.Parent as Accordion)?.Invalidate();
        }
    } = -1;

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        (this.Parent as Accordion)?.Invalidate();
    }
}
