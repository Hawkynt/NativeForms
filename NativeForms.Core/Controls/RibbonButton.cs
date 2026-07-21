namespace Hawkynt.NativeForms;

/// <summary>
/// A ribbon push button: icon plus caption, <see cref="RibbonItem.ItemSize"/> deciding between the
/// large single-column form and the small stacked one. Commands wire up exactly like on a toolbar
/// button — <see cref="ToolStripItem.Command"/> executes on click and its guard drives the enabled
/// state.
/// </summary>
public class RibbonButton : RibbonItem
{
    /// <summary>Creates an empty ribbon button.</summary>
    public RibbonButton() { }

    /// <summary>Creates a large ribbon button with the given caption.</summary>
    public RibbonButton(string text) => this.Text = text;

    /// <summary>Creates a ribbon button with the given caption and size.</summary>
    public RibbonButton(string text, RibbonItemSize size)
    {
        this.Text = text;
        this.ItemSize = size;
    }
}
