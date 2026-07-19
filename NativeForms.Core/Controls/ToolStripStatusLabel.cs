namespace Hawkynt.NativeForms;

/// <summary>
/// A status-bar panel: caption plus optional icon. With <see cref="Spring"/> set the panel absorbs
/// whatever width the fixed panels leave over — several springs share the leftover equally — which
/// is how the classic "message area | details | clock" status-bar layout is built.
/// </summary>
public class ToolStripStatusLabel : ToolStripItem
{
    /// <summary>Creates an empty status label.</summary>
    public ToolStripStatusLabel() { }

    /// <summary>Creates a status label with the given caption.</summary>
    public ToolStripStatusLabel(string text) => this.Text = text;

    /// <summary>Whether this panel stretches to absorb the width the fixed panels leave unused.</summary>
    public bool Spring
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    }
}
