using System.Drawing;
using System.Windows.Input;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A standalone two-zone button, the control-sized sibling of <see cref="ToolStripSplitButton"/>:
/// the main zone clicks like a plain button — raising <see cref="Control.Click"/> and executing
/// <see cref="Command"/> — while the separated arrow zone opens the
/// <see cref="DropDownButtonBase.DropDownItems"/> drop-down below the control. Enter and Space run
/// the main action; Down (plain or with Alt) opens the menu.
/// </summary>
public class SplitButton : DropDownButtonBase
{
    /// <summary>
    /// The MVVM command the main zone invokes. Its <see cref="ICommand.CanExecute"/> gates the main
    /// action at click time — a view-model guard silently swallows the click — while the arrow zone
    /// and the drop-down stay available.
    /// </summary>
    public ICommand? Command { get; set; }

    /// <summary>The main (button) zone: everything left of the arrow zone.</summary>
    private Rectangle MainZone => new(0, 0, Math.Max(0, this.Width - ArrowZoneWidth), this.Height);

    /// <summary>Runs the main action as a main-zone click would: raises <see cref="Control.Click"/>
    /// and executes <see cref="Command"/>. A no-op while disabled or while the command declines.</summary>
    public void PerformMainClick()
    {
        if (!this.Enabled)
            return;

        var command = this.Command;
        if (command is not null && !command.CanExecute(null))
            return;

        this.OnClick(EventArgs.Empty);
        command?.Execute(null);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!this.Enabled || e.Button != MouseButtons.Left)
            return;

        this.Focus();
        if (e.X >= this.Width - ArrowZoneWidth && new Rectangle(0, 0, this.Width, this.Height).Contains(e.Location))
            this.ShowDropDown();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (this.Enabled && e.Button == MouseButtons.Left && this.MainZone.Contains(e.Location))
            this.PerformMainClick();
    }

    /// <summary>Enter runs the main action, so it stays out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter && this.Enabled;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !this.Enabled || e.KeyCode is not (Keys.Enter or Keys.Space))
            return;

        this.PerformMainClick();
        e.Handled = true;
    }

    /// <inheritdoc/>
    private protected override void PaintArrowZone(IGraphics g, ITheme theme, Color color)
    {
        var separatorX = this.Width - ArrowZoneWidth;
        g.DrawLine(theme.Border, separatorX, 3, separatorX, this.Height - 4);
        base.PaintArrowZone(g, theme, color);
    }
}
