namespace Hawkynt.NativeForms;

/// <summary>
/// A latching ribbon button — the Bold/Italic kind. Clicking flips <see cref="Checked"/> and the
/// ribbon paints the item held down; <see cref="ToolStripItem.Command"/> still executes, so a
/// view-model can observe the toggle the same way it observes a push button.
/// </summary>
public class RibbonToggleButton : RibbonItem
{
    /// <summary>Creates an empty toggle button.</summary>
    public RibbonToggleButton() { }

    /// <summary>Creates a large toggle button with the given caption.</summary>
    public RibbonToggleButton(string text) => this.Text = text;

    /// <summary>Creates a toggle button with the given caption and size.</summary>
    public RibbonToggleButton(string text, RibbonItemSize size)
    {
        this.Text = text;
        this.ItemSize = size;
    }

    /// <summary>Whether the button is latched down.</summary>
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

    /// <summary>Raised after <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override void OnClick(EventArgs e)
    {
        this.Checked = !this.Checked;
        base.OnClick(e);
    }
}
