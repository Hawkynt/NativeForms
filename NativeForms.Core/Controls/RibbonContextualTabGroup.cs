using System.Collections;
using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A set of related <see cref="RibbonTab"/>s shown only in context — a colour-coded family such as
/// "Table Tools" that appears while the relevant object is selected and vanishes otherwise. The tabs
/// stay in <see cref="Ribbon.Tabs"/> but are hidden from the strip while the group's
/// <see cref="Visible"/> is <see langword="false"/>, and while shown they carry a coloured marker in
/// the group's <see cref="Color"/>.
/// </summary>
public sealed class RibbonContextualTabGroup
{
    private readonly List<RibbonTab> _tabs = [];

    /// <summary>Creates a hidden contextual group with the given title and accent colour.</summary>
    public RibbonContextualTabGroup(string text, Color color)
    {
        this.Text = text ?? string.Empty;
        this.Color = color;
    }

    /// <summary>The ribbon holding this group, or <see langword="null"/> while detached.</summary>
    internal Ribbon? Owner { get; set; }

    /// <summary>The group's title (shown by the colour-coding; the Office-style banner is a future refinement).</summary>
    public string Text { get; }

    /// <summary>The accent colour marking this group's tabs.</summary>
    public Color Color { get; }

    /// <summary>The tabs belonging to this group, in strip order.</summary>
    public IReadOnlyList<RibbonTab> Tabs => _tabs;

    /// <summary>Whether the group's tabs currently appear in the strip. Hiding a group holding the
    /// selected tab hands the selection to the nearest still-shown tab.</summary>
    public bool Visible
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Owner?.OnContextualVisibilityChanged();
        }
    }

    /// <summary>Adds a tab to this group, associating it so the strip shows it only while visible. The
    /// tab must already belong to the same ribbon.</summary>
    public void Add(RibbonTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.ContextualGroup = this;
        _tabs.Add(tab);
        this.Owner?.OnContextualVisibilityChanged();
    }

    /// <summary>Adds several tabs in order.</summary>
    public void AddRange(params RibbonTab[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        foreach (var tab in tabs)
            this.Add(tab);
    }
}

/// <summary>The contextual tab groups of a <see cref="Ribbon"/>.</summary>
public sealed class RibbonContextualTabGroupCollection : IReadOnlyList<RibbonContextualTabGroup>
{
    private readonly Ribbon _owner;
    private readonly List<RibbonContextualTabGroup> _groups = [];

    internal RibbonContextualTabGroupCollection(Ribbon owner) => _owner = owner;

    /// <summary>The number of contextual groups.</summary>
    public int Count => _groups.Count;

    /// <summary>The group at the given index.</summary>
    public RibbonContextualTabGroup this[int index] => _groups[index];

    /// <summary>Registers a contextual group with the ribbon.</summary>
    public void Add(RibbonContextualTabGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Owner = _owner;
        _groups.Add(group);
        _owner.OnContextualVisibilityChanged();
    }

    /// <inheritdoc/>
    public IEnumerator<RibbonContextualTabGroup> GetEnumerator() => _groups.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
