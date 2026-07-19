using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>
/// The sizing rule for one <see cref="TableLayoutPanel"/> track, specialized as
/// <see cref="ColumnStyle"/> and <see cref="RowStyle"/>. Editing a style that belongs to a live
/// panel re-lays the grid out immediately.
/// </summary>
public abstract class TableLayoutStyle
{
    private protected TableLayoutStyle(SizeType sizeType, float size)
    {
        this.SizeType = sizeType;
        this.Size = size;
    }

    /// <summary>The panel this style currently sizes a track of, or <see langword="null"/>.</summary>
    internal TableLayoutPanel? Owner { get; set; }

    /// <summary>How the track obtains its size.</summary>
    public SizeType SizeType
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Owner?.PerformLayout();
        }
    }

    /// <summary>The pixel size (<see cref="SizeType.Absolute"/>) or percent weight (<see cref="SizeType.Percent"/>).</summary>
    internal float Size
    {
        get => field;
        set
        {
            value = Math.Max(0, value);
            if (field == value)
                return;

            field = value;
            this.Owner?.PerformLayout();
        }
    }
}

/// <summary>The sizing rule for one <see cref="TableLayoutPanel"/> column.</summary>
public sealed class ColumnStyle : TableLayoutStyle
{
    /// <summary>Creates an auto-sized column style.</summary>
    public ColumnStyle() : base(SizeType.AutoSize, 0) { }

    /// <summary>Creates a column style of the given kind with a zero size.</summary>
    public ColumnStyle(SizeType sizeType) : base(sizeType, 0) { }

    /// <summary>Creates a column style of the given kind and size.</summary>
    public ColumnStyle(SizeType sizeType, float width) : base(sizeType, width) { }

    /// <summary>The column's pixel width or percent weight, per <see cref="TableLayoutStyle.SizeType"/>.</summary>
    public float Width
    {
        get => this.Size;
        set => this.Size = value;
    }
}

/// <summary>The sizing rule for one <see cref="TableLayoutPanel"/> row.</summary>
public sealed class RowStyle : TableLayoutStyle
{
    /// <summary>Creates an auto-sized row style.</summary>
    public RowStyle() : base(SizeType.AutoSize, 0) { }

    /// <summary>Creates a row style of the given kind with a zero size.</summary>
    public RowStyle(SizeType sizeType) : base(sizeType, 0) { }

    /// <summary>Creates a row style of the given kind and size.</summary>
    public RowStyle(SizeType sizeType, float height) : base(sizeType, height) { }

    /// <summary>The row's pixel height or percent weight, per <see cref="TableLayoutStyle.SizeType"/>.</summary>
    public float Height
    {
        get => this.Size;
        set => this.Size = value;
    }
}

/// <summary>
/// The ordered styles of a <see cref="TableLayoutPanel"/> axis. Tracks beyond the styled ones share
/// the remaining space equally; every mutation re-lays the owning grid out.
/// </summary>
public sealed class TableLayoutStyleCollection<TStyle> : IReadOnlyList<TStyle>
    where TStyle : TableLayoutStyle
{
    private readonly TableLayoutPanel _owner;
    private readonly List<TStyle> _items = [];

    internal TableLayoutStyleCollection(TableLayoutPanel owner) => _owner = owner;

    /// <summary>The number of styles.</summary>
    public int Count => _items.Count;

    /// <summary>The style at the given track index.</summary>
    public TStyle this[int index] => _items[index];

    /// <summary>Appends a style for the next track.</summary>
    public void Add(TStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.Owner = _owner;
        _items.Add(style);
        _owner.PerformLayout();
    }

    /// <summary>Removes the style at the given track index.</summary>
    public void RemoveAt(int index)
    {
        _items[index].Owner = null;
        _items.RemoveAt(index);
        _owner.PerformLayout();
    }

    /// <summary>Removes every style.</summary>
    public void Clear()
    {
        foreach (var style in _items)
            style.Owner = null;

        _items.Clear();
        _owner.PerformLayout();
    }

    /// <inheritdoc/>
    public IEnumerator<TStyle> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
