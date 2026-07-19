namespace Hawkynt.NativeForms;

/// <summary>
/// A titled section of <see cref="ListView"/> items. Items join a group through
/// <see cref="ListViewItem.Group"/>; the control renders every group of its
/// <see cref="ListView.Groups"/> collection (in collection order) as an accent-colored header row
/// followed by the member items, with items belonging to no listed group gathered under a trailing
/// default section — matching <c>System.Windows.Forms.ListViewGroup</c>.
/// </summary>
public sealed class ListViewGroup
{
    /// <summary>Creates a group with an empty header.</summary>
    public ListViewGroup() { }

    /// <summary>Creates a group with the given header caption.</summary>
    /// <param name="header">The caption shown in the group's header row.</param>
    public ListViewGroup(string header) => this.Header = header;

    /// <summary>The caption shown in the group's header row.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Arbitrary caller data associated with the group.</summary>
    public object? Tag { get; set; }
}
