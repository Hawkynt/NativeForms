namespace Hawkynt.NativeForms;

/// <summary>
/// A toolbar button whose whole surface opens a drop-down of its <see cref="ToolStripDropDownItem.DropDownItems"/>
/// through the shared menu engine; the trailing arrow zone just makes the affordance visible.
/// </summary>
public class ToolStripDropDownButton : ToolStripDropDownItem
{
    /// <summary>Creates an empty drop-down button.</summary>
    public ToolStripDropDownButton() { }

    /// <summary>Creates a drop-down button with the given caption.</summary>
    public ToolStripDropDownButton(string text) => this.Text = text;
}
