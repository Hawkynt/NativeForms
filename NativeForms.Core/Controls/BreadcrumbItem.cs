using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>One segment of a <see cref="Breadcrumb"/> — a path element with an optional icon.</summary>
public sealed class BreadcrumbItem
{
    /// <summary>Creates an untitled segment.</summary>
    public BreadcrumbItem() { }

    /// <summary>Creates a segment with the given caption.</summary>
    public BreadcrumbItem(string text) => this.Text = text;

    /// <summary>The owning breadcrumb, or <see langword="null"/> while detached.</summary>
    internal Breadcrumb? Owner { get; set; }

    /// <summary>The segment caption.</summary>
    public string Text
    {
        get => field ?? string.Empty;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Owner?.OnItemsChanged();
        }
    } = string.Empty;

    /// <summary>Arbitrary caller data — a folder path, a node, an id.</summary>
    public object? Tag { get; set; }

    /// <summary>Index of this segment's icon in the owning <see cref="Breadcrumb.ImageList"/>, or -1.</summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Owner?.OnItemsChanged();
        }
    } = -1;

    /// <summary>Key of this segment's icon, used when <see cref="ImageIndex"/> is unset.</summary>
    public string? ImageKey
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Owner?.OnItemsChanged();
        }
    }

    /// <summary>The concrete icon index, resolving <see cref="ImageKey"/> against <paramref name="images"/>.</summary>
    internal int ResolveImageIndex(ImageList? images) => ImageList.ResolveIndex(images, this.ImageIndex, this.ImageKey);
}

/// <summary>Identifies the segment a <see cref="Breadcrumb"/> event is about.</summary>
public sealed class BreadcrumbItemEventArgs(BreadcrumbItem item, int index) : EventArgs
{
    /// <summary>The clicked segment.</summary>
    public BreadcrumbItem Item { get; } = item;

    /// <summary>The segment's index within <see cref="Breadcrumb.Items"/>.</summary>
    public int Index { get; } = index;
}

/// <summary>Carries the text a <see cref="Breadcrumb"/> edit field committed.</summary>
public sealed class BreadcrumbPathEventArgs(string path) : EventArgs
{
    /// <summary>The full path the user entered, before parsing into segments.</summary>
    public string Path { get; } = path;
}

/// <summary>The ordered set of segments of a <see cref="Breadcrumb"/>, left to right.</summary>
public sealed class BreadcrumbItemCollection : IReadOnlyList<BreadcrumbItem>
{
    private readonly Breadcrumb _owner;
    private readonly List<BreadcrumbItem> _items = [];

    internal BreadcrumbItemCollection(Breadcrumb owner) => _owner = owner;

    /// <summary>The number of segments.</summary>
    public int Count => _items.Count;

    /// <summary>The segment at the given index.</summary>
    public BreadcrumbItem this[int index] => _items[index];

    /// <summary>The index of the segment, or -1 when it is not part of this breadcrumb.</summary>
    public int IndexOf(BreadcrumbItem item) => _items.IndexOf(item);

    /// <summary>Appends a segment.</summary>
    public BreadcrumbItem Add(BreadcrumbItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Owner = _owner;
        _items.Add(item);
        _owner.OnItemsChanged();
        return item;
    }

    /// <summary>Appends a segment with the given caption and returns it.</summary>
    public BreadcrumbItem Add(string text) => this.Add(new BreadcrumbItem(text));

    /// <summary>Appends several captions in order.</summary>
    public void AddRange(params string[] captions)
    {
        ArgumentNullException.ThrowIfNull(captions);
        foreach (var caption in captions)
            this.Add(caption);
    }

    /// <summary>Removes a segment; returns whether it was present.</summary>
    public bool Remove(BreadcrumbItem item)
    {
        if (!_items.Remove(item))
            return false;

        item.Owner = null;
        _owner.OnItemsChanged();
        return true;
    }

    /// <summary>Removes every segment after the given index — the "navigate up to here" trim.</summary>
    public void TrimAfter(int index)
    {
        if (index < 0 || index >= _items.Count - 1)
            return;

        for (var i = _items.Count - 1; i > index; --i)
        {
            _items[i].Owner = null;
            _items.RemoveAt(i);
        }

        _owner.OnItemsChanged();
    }

    /// <summary>Removes every segment.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;

        foreach (var item in _items)
            item.Owner = null;

        _items.Clear();
        _owner.OnItemsChanged();
    }

    /// <inheritdoc/>
    public IEnumerator<BreadcrumbItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
