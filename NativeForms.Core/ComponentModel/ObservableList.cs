using System.Collections;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>The kind of change an <see cref="ObservableList{T}"/> reports.</summary>
public enum ListChangeType
{
    /// <summary>An item was inserted at the index.</summary>
    Added,

    /// <summary>An item was removed from the index.</summary>
    Removed,

    /// <summary>The item at the index was replaced.</summary>
    Replaced,

    /// <summary>An item moved from <see cref="ListChangedEventArgs.OldIndex"/> to <see cref="ListChangedEventArgs.Index"/>.</summary>
    Moved,

    /// <summary>The whole list changed (clear/bulk).</summary>
    Reset,
}

/// <summary>Describes a change to an <see cref="ObservableList{T}"/>.</summary>
public sealed class ListChangedEventArgs : EventArgs
{
    /// <summary>A single-index change (add/remove/replace/reset).</summary>
    public ListChangedEventArgs(ListChangeType changeType, int index)
    {
        this.ChangeType = changeType;
        this.Index = index;
        this.OldIndex = -1;
    }

    /// <summary>A <see cref="ListChangeType.Moved"/> change, carrying both source and destination.</summary>
    public ListChangedEventArgs(ListChangeType changeType, int oldIndex, int newIndex)
    {
        this.ChangeType = changeType;
        this.Index = newIndex;
        this.OldIndex = oldIndex;
    }

    /// <summary>What happened.</summary>
    public ListChangeType ChangeType { get; }

    /// <summary>The affected (or destination) index, or -1 for <see cref="ListChangeType.Reset"/>.</summary>
    public int Index { get; }

    /// <summary>The source index for a <see cref="ListChangeType.Moved"/> change; -1 otherwise.</summary>
    public int OldIndex { get; }
}

/// <summary>A read-only view of an <see cref="ObservableList{T}"/> that still reports its changes —
/// handed to consumers that should observe but not mutate the list.</summary>
/// <typeparam name="T">The element type.</typeparam>
public interface IReadOnlyObservableList<out T> : IReadOnlyList<T>
{
    /// <summary>Raised after every structural change.</summary>
    event EventHandler<ListChangedEventArgs>? ListChanged;
}

/// <summary>
/// A lightweight observable list — the reflection-free replacement for <c>BindingList&lt;T&gt;</c> that
/// list controls bind to. Raises granular <see cref="ListChanged"/> notifications so a bound control
/// repaints only what changed instead of rebuilding.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ObservableList<T> : IList<T>, IReadOnlyList<T>, IReadOnlyObservableList<T>
{
    private readonly List<T> _items;

    /// <summary>Creates an empty list.</summary>
    public ObservableList() => _items = [];

    /// <summary>Creates a list pre-filled from a sequence.</summary>
    public ObservableList(IEnumerable<T> items) => _items = [.. items];

    /// <summary>Raised after every structural change.</summary>
    public event EventHandler<ListChangedEventArgs>? ListChanged;

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            _items[index] = value;
            this.OnListChanged(ListChangeType.Replaced, index);
        }
    }

    /// <inheritdoc/>
    public void Add(T item)
    {
        _items.Add(item);
        this.OnListChanged(ListChangeType.Added, _items.Count - 1);
    }

    /// <summary>Adds several items, raising one notification per item.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
            this.Add(item);
    }

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        this.OnListChanged(ListChangeType.Added, index);
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
            return false;

        this.RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        this.OnListChanged(ListChangeType.Removed, index);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _items.Clear();
        this.OnListChanged(ListChangeType.Reset, -1);
    }

    /// <summary>
    /// Moves the item at <paramref name="oldIndex"/> to <paramref name="newIndex"/>, raising a single
    /// <see cref="ListChangeType.Moved"/> notification so a bound control can reorder in place rather
    /// than rebuild. A move to the same index is a no-op.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the list.</exception>
    public void Move(int oldIndex, int newIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(oldIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(oldIndex, _items.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _items.Count);
        if (oldIndex == newIndex)
            return;

        var item = _items[oldIndex];
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        this.ListChanged?.Invoke(this, new ListChangedEventArgs(ListChangeType.Moved, oldIndex, newIndex));
    }

    /// <summary>
    /// Reorders the items in place by the given comparison — stably, so equal items keep their
    /// relative order — raising a single <see cref="ListChangeType.Reset"/> notification.
    /// </summary>
    /// <param name="comparison">Orders any two items.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public void Sort(Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var count = _items.Count;
        if (count < 2)
            return;

        // Sort an index map with the original position as tie-break, then permute once — this is
        // what makes the (inherently unstable) Array.Sort behave stably.
        var map = new int[count];
        for (var i = 0; i < count; ++i)
            map[i] = i;

        var items = _items;
        Array.Sort(map, (a, b) =>
        {
            var result = comparison(items[a], items[b]);
            return result != 0 ? result : a - b;
        });

        var sorted = new T[count];
        for (var i = 0; i < count; ++i)
            sorted[i] = items[map[i]];

        for (var i = 0; i < count; ++i)
            items[i] = sorted[i];

        this.OnListChanged(ListChangeType.Reset, -1);
    }

    /// <inheritdoc/>
    public bool Contains(T item) => _items.Contains(item);

    /// <inheritdoc/>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private void OnListChanged(ListChangeType changeType, int index)
        => this.ListChanged?.Invoke(this, new ListChangedEventArgs(changeType, index));
}
