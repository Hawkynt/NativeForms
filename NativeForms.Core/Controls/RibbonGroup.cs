using System.Collections;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// One labelled box inside a <see cref="RibbonTab"/>: a frame around its items with the group caption
/// running along the bottom edge. When the ribbon runs out of width the group collapses into a single
/// drop-down button that opens the very same <see cref="Items"/> as a popup menu, exactly as Office
/// does.
/// </summary>
public class RibbonGroup
{
    private int _cachedCaptionWidth = -1;

    /// <summary>Creates an untitled group.</summary>
    public RibbonGroup() => this.Items = this.CreateItems();

    /// <summary>Creates a group with the given caption.</summary>
    public RibbonGroup(string text)
        : this() => this.Text = text;

    /// <summary>The tab currently holding this group, or <see langword="null"/> while detached.</summary>
    internal RibbonTab? Owner { get; set; }

    /// <summary>The items in the group, laid out left to right in columns.</summary>
    public ToolStripItemCollection Items { get; }

    /// <summary>The caption painted along the group's bottom edge.</summary>
    public string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _cachedCaptionWidth = -1;
            this.NotifyOwner();
        }
    } = string.Empty;

    /// <summary>The index of the icon the collapsed drop-down button shows, or -1 for none.</summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = -1;

    /// <summary>The key of this group's icon in the owning <see cref="Ribbon.ImageList"/>, used when
    /// <see cref="ImageIndex"/> is unset (&lt; 0). The index takes precedence when both are set.</summary>
    public string? ImageKey
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    }

    /// <summary>Whether the group is currently folded into its drop-down button because the ribbon
    /// ran out of width. Recomputed on every layout pass.</summary>
    public bool IsCollapsed { get; internal set; }

    /// <summary>Arbitrary caller-owned data attached to the group.</summary>
    public object? Tag { get; set; }

    /// <summary>
    /// The group's laid-out rectangle inside the owning <see cref="Ribbon"/>, as of the last layout
    /// pass — empty while the ribbon is minimized or the group's tab is not selected. Readable
    /// because aiming at a group (a tooltip, a UI-automation click) is otherwise guesswork; only the
    /// ribbon assigns it.
    /// </summary>
    public System.Drawing.Rectangle Bounds { get; internal set; }

    /// <summary>Wires the item collection so a caption, icon or size change reaches the ribbon and
    /// voids the measurement caches the layout is built on.</summary>
    private ToolStripItemCollection CreateItems()
    {
        var items = new ToolStripItemCollection();
        items.Changed += this.OnItemsChanged;
        return items;
    }

    private void OnItemsChanged(object? sender, EventArgs e)
    {
        for (var i = 0; i < this.Items.Count; ++i)
            if (this.Items[i] is RibbonItem item)
                item.InvalidateMeasurement();

        this.NotifyOwner();
    }

    /// <summary>The measured caption width, cached against the theme font. An unrealized measurement
    /// is not cached — see <see cref="RibbonItem.TextWidth"/> for why.</summary>
    internal int CaptionWidth(IPlatformBackend? backend, Font font)
    {
        if (_cachedCaptionWidth >= 0)
            return _cachedCaptionWidth;

        if (this.Text.Length == 0)
            return _cachedCaptionWidth = 0;

        return backend is null ? 0 : _cachedCaptionWidth = backend.MeasureText(this.Text, font).Width;
    }

    /// <summary>Drops the caption and item measurements, so the next layout measures them again.</summary>
    internal void InvalidateMeasurements()
    {
        _cachedCaptionWidth = -1;
        for (var i = 0; i < this.Items.Count; ++i)
            if (this.Items[i] is RibbonItem item)
                item.InvalidateMeasurement();
    }

    /// <summary>Tells the owning tab — and through it the ribbon — that this group changed.</summary>
    private protected void NotifyOwner()
    {
        _cachedCaptionWidth = -1;
        this.Owner?.NotifyGroupChanged();
    }
}

/// <summary>The ordered set of groups owned by a <see cref="RibbonTab"/>.</summary>
public sealed class RibbonGroupCollection : IReadOnlyList<RibbonGroup>
{
    private readonly RibbonTab _owner;
    private readonly List<RibbonGroup> _groups = [];

    internal RibbonGroupCollection(RibbonTab owner) => _owner = owner;

    /// <summary>The number of groups.</summary>
    public int Count => _groups.Count;

    /// <summary>The group at the given index.</summary>
    public RibbonGroup this[int index] => _groups[index];

    /// <summary>The index of the group, or -1 when it is not part of this tab.</summary>
    public int IndexOf(RibbonGroup group) => _groups.IndexOf(group);

    /// <summary>Appends a group.</summary>
    public void Add(RibbonGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _groups.Add(group);
        group.Owner = _owner;
        _owner.NotifyGroupChanged();
    }

    /// <summary>Appends several groups in order.</summary>
    public void AddRange(params RibbonGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        foreach (var group in groups)
        {
            _groups.Add(group);
            group.Owner = _owner;
        }

        _owner.NotifyGroupChanged();
    }

    /// <summary>Removes a group; returns whether it was present.</summary>
    public bool Remove(RibbonGroup group)
    {
        if (!_groups.Remove(group))
            return false;

        group.Owner = null;
        _owner.NotifyGroupChanged();
        return true;
    }

    /// <summary>Removes every group.</summary>
    public void Clear()
    {
        if (_groups.Count == 0)
            return;

        foreach (var group in _groups)
            group.Owner = null;

        _groups.Clear();
        _owner.NotifyGroupChanged();
    }

    /// <inheritdoc/>
    public IEnumerator<RibbonGroup> GetEnumerator() => _groups.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
