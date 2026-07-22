using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A single row in a <see cref="ListView"/>. <see cref="Text"/> is the primary (first-column) label;
/// <see cref="SubItems"/> supplies the text for the remaining Details-view columns (and the Tile
/// view's second line) and <see cref="Image"/>/<see cref="ImageIndex"/> select the item's icon.
/// While the item is attached to a control, <see cref="Selected"/> and <see cref="Checked"/> writes
/// route through the owner so its selection set, events and paint stay consistent.
/// </summary>
public sealed class ListViewItem
{
    private bool _selected;
    private bool _checked;

    /// <summary>Creates an empty item.</summary>
    public ListViewItem() { }

    /// <summary>Creates an item with the given primary text.</summary>
    /// <param name="text">The first-column label.</param>
    public ListViewItem(string text) => this.Text = text;

    /// <summary>Creates an item with primary text and additional column texts.</summary>
    /// <param name="text">The first-column label.</param>
    /// <param name="subItems">The texts for the remaining columns.</param>
    public ListViewItem(string text, params string[] subItems)
    {
        this.Text = text;
        this.SubItems.AddRange(subItems);
    }

    /// <summary>The primary label, shown in the first column and in every non-Details view.</summary>
    public string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            this.Owner?.Invalidate();
        }
    } = string.Empty;

    /// <summary>The texts for the additional Details-view columns, in column order.</summary>
    public List<string> SubItems { get; } = [];

    /// <summary>The item's explicit icon; <see langword="null"/> falls back to <see cref="ImageIndex"/>.</summary>
    public IImage? Image { get; set; }

    /// <summary>The index into the owner's image list (large or small, per view), or -1 for no icon.</summary>
    public int ImageIndex { get; set; } = -1;

    /// <summary>The key of this item's icon in the owning <c>ListView.SmallImageList</c>/<c>LargeImageList</c>,
    /// used when <see cref="ImageIndex"/> is unset (&lt; 0). The index takes precedence when both are set.</summary>
    public string? ImageKey { get; set; }

    /// <summary>The group this item is rendered under, or <see langword="null"/> for the default section.</summary>
    public ListViewGroup? Group
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.Owner?.OnItemGroupChanged();
        }
    }

    /// <summary>Arbitrary caller data associated with the item.</summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Whether the item is currently selected. Writes on an attached item change the owner's
    /// selection (respecting <see cref="ListView.MultiSelect"/>) and raise its selection event.
    /// </summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (this.Owner is { } owner)
                owner.SetItemSelected(this, value);
            else
                _selected = value;
        }
    }

    /// <summary>
    /// Whether the item is checked (<see cref="ListView.CheckBoxes"/>). Writes on an attached item
    /// run through the owner's vetoable <see cref="ListView.ItemCheck"/> pipeline.
    /// </summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            if (this.Owner is { } owner)
                owner.RequestItemCheck(this, value);
            else
                _checked = value;
        }
    }

    /// <summary>The control this item currently belongs to, or <see langword="null"/>.</summary>
    internal ListView? Owner { get; set; }

    /// <summary>Writes the selection flag directly, bypassing the owner routing.</summary>
    internal void SetSelectedCore(bool value) => _selected = value;

    /// <summary>Writes the check flag directly, bypassing the veto pipeline.</summary>
    internal void SetCheckedCore(bool value) => _checked = value;

    /// <summary>Starts editing this item's label on its owning control; see <see cref="ListView.BeginEdit(int)"/>.</summary>
    /// <exception cref="InvalidOperationException">The item is not attached to a control, or its
    /// owner's <see cref="ListView.LabelEdit"/> is disabled.</exception>
    public void BeginEdit()
    {
        var owner = this.Owner ?? throw new InvalidOperationException("The item is not attached to a ListView.");
        owner.BeginEdit(owner.Items.IndexOf(this));
    }
}
