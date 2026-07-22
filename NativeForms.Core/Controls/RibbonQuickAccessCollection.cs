using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>
/// The Quick Access Toolbar of a <see cref="Ribbon"/>: a short row of icon-only command buttons
/// painted at the right of the tab strip, always reachable regardless of the selected tab. Each entry
/// is an ordinary <see cref="RibbonButton"/>, so its <see cref="ToolStripItem.Click"/>/
/// <see cref="ToolStripItem.Command"/>, enabled state and icon (<see cref="ToolStripItem.Image"/> or
/// <c>ImageList</c> + <c>ImageIndex</c>/<c>ImageKey</c>) wire up exactly as anywhere else.
/// </summary>
public sealed class RibbonQuickAccessCollection : IReadOnlyList<RibbonButton>
{
    private readonly Ribbon _owner;
    private readonly List<RibbonButton> _items = [];

    internal RibbonQuickAccessCollection(Ribbon owner) => _owner = owner;

    /// <summary>The number of quick-access buttons.</summary>
    public int Count => _items.Count;

    /// <summary>The button at the given index.</summary>
    public RibbonButton this[int index] => _items[index];

    /// <summary>The index of the button, or -1 when it is not in this toolbar.</summary>
    public int IndexOf(RibbonButton item) => _items.IndexOf(item);

    /// <summary>Appends a quick-access button.</summary>
    public void Add(RibbonButton item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        _owner.OnQuickAccessChanged();
    }

    /// <summary>Appends several quick-access buttons in order.</summary>
    public void AddRange(params RibbonButton[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
            this.Add(item);
    }

    /// <summary>Removes a button; returns whether it was present.</summary>
    public bool Remove(RibbonButton item)
    {
        if (!_items.Remove(item))
            return false;

        _owner.OnQuickAccessChanged();
        return true;
    }

    /// <summary>Removes every quick-access button.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        _owner.OnQuickAccessChanged();
    }

    /// <inheritdoc/>
    public IEnumerator<RibbonButton> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
