namespace Hawkynt.NativeForms;

/// <summary>
/// A ribbon item that hosts a real <see cref="Control"/> — the combo box, text box or track bar an
/// Office group puts among its buttons. The ribbon parents the control into its own
/// <see cref="Control.Controls"/>, positions it from the group layout and hides its peer while the
/// owning tab is not selected, the ribbon is minimized or the group has collapsed into a drop-down.
/// </summary>
public class RibbonHostItem : RibbonItem
{
    /// <summary>Creates a host item for the given control, one stacked row tall by default.</summary>
    public RibbonHostItem(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        this.Control = control;
        this.ItemSize = RibbonItemSize.Small;
    }

    /// <summary>The hosted control.</summary>
    public Control Control { get; }

    /// <summary>The pixel width the ribbon gives the hosted control.</summary>
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
    /// Whether the last layout pass actually placed this control on screen. The ribbon's child-peer
    /// veto reads it, so a hosted control inside an unselected tab or a collapsed group keeps its own
    /// <see cref="Control.Visible"/> flag while its peer stays hidden.
    /// </summary>
    internal bool Placed { get; set; }
}
