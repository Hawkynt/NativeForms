namespace Hawkynt.NativeForms;

/// <summary>
/// A two-zone toolbar button: the main zone clicks like a plain <see cref="ToolStripButton"/> (and
/// runs the attached command), while the separate arrow zone opens the
/// <see cref="ToolStripDropDownItem.DropDownItems"/> drop-down through the shared menu engine.
/// </summary>
public class ToolStripSplitButton : ToolStripDropDownItem
{
    /// <summary>Creates an empty split button.</summary>
    public ToolStripSplitButton() { }

    /// <summary>Creates a split button with the given caption.</summary>
    public ToolStripSplitButton(string text) => this.Text = text;
}
