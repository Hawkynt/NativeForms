using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>
/// The ordered set of items a strip (or a drop-down) hosts. Mutations and item-state changes raise
/// <see cref="Changed"/>, which the hosting strip turns into a repaint — the collection itself never
/// touches a surface, so the same item tree serves menu bars, drop-downs, toolbars and status bars.
/// </summary>
public sealed class ToolStripItemCollection : IReadOnlyList<ToolStripItem>
{
    private readonly List<ToolStripItem> _items = [];

    /// <summary>Raised when items are added/removed or an item's visual state changes.</summary>
    internal event EventHandler? Changed;

    /// <summary>The number of items, including invisible ones.</summary>
    public int Count => _items.Count;

    /// <summary>The item at <paramref name="index"/>.</summary>
    public ToolStripItem this[int index] => _items[index];

    /// <summary>Appends an item.</summary>
    public void Add(ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        item.Owner = this;
        this.NotifyItemChanged();
    }

    /// <summary>Appends several items with a single change notification.</summary>
    public void AddRange(params ToolStripItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            _items.Add(item);
            item.Owner = this;
        }

        this.NotifyItemChanged();
    }

    /// <summary>Inserts an item at <paramref name="index"/>.</summary>
    public void Insert(int index, ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        item.Owner = this;
        this.NotifyItemChanged();
    }

    /// <summary>Removes an item; a no-op when it is not present.</summary>
    public void Remove(ToolStripItem item)
    {
        if (!_items.Remove(item))
            return;

        item.Owner = null;
        this.NotifyItemChanged();
    }

    /// <summary>Removes the item at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        _items[index].Owner = null;
        _items.RemoveAt(index);
        this.NotifyItemChanged();
    }

    /// <summary>Removes all items.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;

        foreach (var item in _items)
            item.Owner = null;

        _items.Clear();
        this.NotifyItemChanged();
    }

    /// <summary>The index of <paramref name="item"/>, or -1 when it is not present.</summary>
    public int IndexOf(ToolStripItem item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public IEnumerator<ToolStripItem> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>Bubbles a structural or item-state change to the hosting strip.</summary>
    internal void NotifyItemChanged() => this.Changed?.Invoke(this, EventArgs.Empty);
}
