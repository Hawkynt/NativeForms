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

    /// <summary>The whole list changed (clear/bulk).</summary>
    Reset,
}

/// <summary>Describes a change to an <see cref="ObservableList{T}"/>.</summary>
public sealed class ListChangedEventArgs(ListChangeType changeType, int index) : EventArgs
{
    /// <summary>What happened.</summary>
    public ListChangeType ChangeType { get; } = changeType;

    /// <summary>The affected index, or -1 for <see cref="ListChangeType.Reset"/>.</summary>
    public int Index { get; } = index;
}

/// <summary>
/// A lightweight observable list — the reflection-free replacement for <c>BindingList&lt;T&gt;</c> that
/// list controls bind to. Raises granular <see cref="ListChanged"/> notifications so a bound control
/// repaints only what changed instead of rebuilding.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ObservableList<T> : IList<T>, IReadOnlyList<T>
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
