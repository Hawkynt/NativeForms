namespace Hawkynt.NativeForms;

/// <summary>
/// A strip item that can carry a drop-down of child items: the shared base of
/// <see cref="ToolStripMenuItem"/>, <see cref="ToolStripDropDownButton"/> and
/// <see cref="ToolStripSplitButton"/>, mirroring the Windows Forms hierarchy. The child collection
/// bubbles its changes through this item so the hosting strip repaints (and an open drop-down
/// refreshes) no matter how deep the change happened.
/// </summary>
public abstract class ToolStripDropDownItem : ToolStripItem
{
    private ToolStripItemCollection? _dropDownItems;

    /// <summary>The child items shown when this item's drop-down opens. Lazily created, so leaf
    /// items never pay for an empty collection.</summary>
    public ToolStripItemCollection DropDownItems
    {
        get
        {
            var items = _dropDownItems;
            if (items is null)
            {
                _dropDownItems = items = new();
                items.Changed += (_, _) => this.NotifyOwner();
            }

            return items;
        }
    }

    /// <summary>Whether a drop-down would show anything — the seam the paint code uses to decide on
    /// the submenu arrow without materializing an empty collection.</summary>
    public bool HasDropDownItems => _dropDownItems is { Count: > 0 };
}
