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
/// Describes a finished label edit; see <see cref="ListView.AfterLabelEdit"/>. A handler vetoes the
/// new text by setting <see cref="CancelEdit"/>, leaving the item's original text in place.
/// </summary>
public sealed class LabelEditEventArgs(int item, string? label) : EventArgs
{
    /// <summary>The index of the edited item.</summary>
    public int Item { get; } = item;

    /// <summary>The text the user entered, or <see langword="null"/> when the edit was cancelled.</summary>
    public string? Label { get; } = label;

    /// <summary>Set by a handler to discard the entered text and keep the item's current text.</summary>
    public bool CancelEdit { get; set; }
}
