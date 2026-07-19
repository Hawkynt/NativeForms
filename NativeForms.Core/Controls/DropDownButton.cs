namespace Hawkynt.NativeForms;

/// <summary>
/// A standalone button whose whole surface opens a drop-down of its
/// <see cref="DropDownButtonBase.DropDownItems"/> below the control — the control-sized sibling of
/// <see cref="ToolStripDropDownButton"/>; the trailing arrow zone just makes the affordance visible.
/// Down, Enter and Space open the menu as well.
/// </summary>
public class DropDownButton : DropDownButtonBase
{
    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!this.Enabled || e.Button != MouseButtons.Left)
            return;

        this.Focus();
        this.ShowDropDown();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !this.Enabled || e.KeyCode is not (Keys.Enter or Keys.Space))
            return;

        this.ShowDropDown();
        e.Handled = true;
    }
}
