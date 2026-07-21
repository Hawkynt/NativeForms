using System.Collections;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// One tab of a <see cref="Ribbon"/>: a caption in the strip along the top and the
/// <see cref="RibbonGroup"/>s shown side by side underneath while the tab is selected.
/// </summary>
public class RibbonTab
{
    private int _cachedTextWidth = -1;

    /// <summary>Creates an untitled tab.</summary>
    public RibbonTab() => this.Groups = new(this);

    /// <summary>Creates a tab with the given caption.</summary>
    public RibbonTab(string text)
        : this() => this.Text = text;

    /// <summary>The ribbon currently holding this tab, or <see langword="null"/> while detached.</summary>
    internal Ribbon? Owner { get; set; }

    /// <summary>The groups shown while this tab is selected, left to right.</summary>
    public RibbonGroupCollection Groups { get; }

    /// <summary>The caption in the tab strip.</summary>
    public string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _cachedTextWidth = -1;
            this.NotifyGroupChanged();
        }
    } = string.Empty;

    /// <summary>Arbitrary caller-owned data attached to the tab.</summary>
    public object? Tag { get; set; }

    /// <summary>The measured header width of the caption, cached against the theme font. An
    /// unrealized measurement is not cached — see <see cref="RibbonItem.TextWidth"/> for why.</summary>
    internal int TextWidth(IPlatformBackend? backend, Font font)
    {
        if (_cachedTextWidth >= 0)
            return _cachedTextWidth;

        if (this.Text.Length == 0)
            return _cachedTextWidth = 0;

        return backend is null ? 0 : _cachedTextWidth = backend.MeasureText(this.Text, font).Width;
    }

    /// <summary>Drops this tab's and every group's measurements.</summary>
    internal void InvalidateMeasurements()
    {
        _cachedTextWidth = -1;
        for (var i = 0; i < this.Groups.Count; ++i)
            this.Groups[i].InvalidateMeasurements();
    }

    /// <summary>Bubbles a group or item change to the owning ribbon.</summary>
    internal void NotifyGroupChanged()
    {
        _cachedTextWidth = -1;
        this.Owner?.NotifyTabChanged();
    }
}

/// <summary>The ordered set of tabs owned by a <see cref="Ribbon"/>.</summary>
public sealed class RibbonTabCollection : IReadOnlyList<RibbonTab>
{
    private readonly Ribbon _owner;
    private readonly List<RibbonTab> _tabs = [];

    internal RibbonTabCollection(Ribbon owner) => _owner = owner;

    /// <summary>The number of tabs.</summary>
    public int Count => _tabs.Count;

    /// <summary>The tab at the given index.</summary>
    public RibbonTab this[int index] => _tabs[index];

    /// <summary>The index of the tab, or -1 when it is not part of this ribbon.</summary>
    public int IndexOf(RibbonTab tab) => _tabs.IndexOf(tab);

    /// <summary>Appends a tab; the first tab added becomes the selected one.</summary>
    public void Add(RibbonTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tabs.Add(tab);
        tab.Owner = _owner;
        _owner.OnTabAdded(tab);
    }

    /// <summary>Appends several tabs in order.</summary>
    public void AddRange(params RibbonTab[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        foreach (var tab in tabs)
            this.Add(tab);
    }

    /// <summary>Removes a tab; returns whether it was present.</summary>
    public bool Remove(RibbonTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
            return false;

        _tabs.RemoveAt(index);
        tab.Owner = null;
        _owner.OnTabRemoved(tab, index);
        return true;
    }

    /// <summary>Removes every tab.</summary>
    public void Clear()
    {
        while (_tabs.Count > 0)
            this.Remove(_tabs[^1]);
    }

    /// <inheritdoc/>
    public IEnumerator<RibbonTab> GetEnumerator() => _tabs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
