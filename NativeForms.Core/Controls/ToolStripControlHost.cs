namespace Hawkynt.NativeForms;

/// <summary>
/// A toolbar item that hosts a real <see cref="Control"/> — a combo box, a date/time picker, a colour
/// button, a check box — among the buttons, the toolbar counterpart of WinForms
/// <c>ToolStripControlHost</c>. The <see cref="ToolStrip"/> parents the control into its own
/// <see cref="Control.Controls"/>, positions it from the item layout, and hides its peer while the
/// item is pushed into the overflow.
/// </summary>
public class ToolStripControlHost : ToolStripItem
{
    /// <summary>Creates a host item for the given control.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public ToolStripControlHost(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        // A toolbar row is a fixed, deliberately short strip, and several desktops will not draw a combo
        // box or a slider that small — the widget keeps its own minimum and is clipped. Hosted controls
        // therefore stay on the owner-drawn painter, which honours whatever height the bar hands it
        // (PRD §12). An application that wants the platform widget can opt back in per control.
        control.UseNativeWidget ??= false;
        this.Control = control;
    }

    /// <summary>The hosted control.</summary>
    public Control Control { get; }

    /// <summary>The pixel width the toolbar reserves for the item, its inner padding included.</summary>
    public int HostWidth
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = 120;

    /// <summary>
    /// Whether the last layout pass placed the control on screen. The toolbar's child-peer veto reads
    /// it, so a hosted control pushed into the overflow keeps its own <see cref="Control.Visible"/>
    /// flag while its peer stays hidden.
    /// </summary>
    internal bool Placed { get; set; }
}
