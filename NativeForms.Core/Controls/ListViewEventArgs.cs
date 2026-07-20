namespace Hawkynt.NativeForms;

/// <summary>Carries the index of a clicked <see cref="ListView"/> column header.</summary>
public sealed class ColumnClickEventArgs(int column) : EventArgs
{
    /// <summary>The index of the clicked column in <see cref="ListView.Columns"/>.</summary>
    public int Column { get; } = column;
}

/// <summary>Carries the item whose check state just flipped; see <see cref="ListView.ItemChecked"/>.</summary>
public sealed class ItemCheckedEventArgs(ListViewItem item) : EventArgs
{
    /// <summary>The item that changed.</summary>
    public ListViewItem Item { get; } = item;
}

/// <summary>
/// Describes a label edit at both ends of its life. For <see cref="ListView.BeforeLabelEdit"/> it
/// carries the item's current text and <see cref="CancelEdit"/> vetoes the edit before it starts;
/// for <see cref="ListView.AfterLabelEdit"/> it carries the entered text and <see cref="CancelEdit"/>
/// discards it, leaving the item's original text in place.
/// </summary>
public sealed class LabelEditEventArgs(int item, string? label) : EventArgs
{
    /// <summary>The index of the edited item.</summary>
    public int Item { get; } = item;

    /// <summary>The label text: the current text before an edit, the text the user entered after
    /// one — or <see langword="null"/> when the edit was cancelled.</summary>
    public string? Label { get; } = label;

    /// <summary>Set by a handler to veto the pending edit (before) or discard the entered text (after).</summary>
    public bool CancelEdit { get; set; }
}
