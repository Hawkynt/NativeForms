namespace Hawkynt.NativeForms;

/// <summary>
/// A toolbar button: an icon, an optional caption, and — with <see cref="CheckOnClick"/> — latching
/// toggle behavior. Commands wire up exactly like on a menu item: <see cref="ToolStripItem.Command"/>
/// executes on click and its guard drives the enabled state.
/// </summary>
public class ToolStripButton : ToolStripItem
{
    /// <summary>Creates an empty toolbar button.</summary>
    public ToolStripButton() { }

    /// <summary>Creates a toolbar button with the given caption.</summary>
    public ToolStripButton(string text) => this.Text = text;

    /// <summary>Whether the button is latched down (toggle state).</summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
            this.CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether clicking toggles <see cref="Checked"/> automatically.</summary>
    public bool CheckOnClick { get; set; }

    /// <summary>Raised after <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override void OnClick(EventArgs e)
    {
        if (this.CheckOnClick)
            this.Checked = !this.Checked;

        base.OnClick(e);
    }
}
