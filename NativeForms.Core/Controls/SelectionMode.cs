namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="ListBox"/> lets the user select items, matching
/// <c>System.Windows.Forms.SelectionMode</c>.
/// </summary>
public enum SelectionMode
{
    /// <summary>Nothing selects; clicks and arrows move only the caret.</summary>
    None,

    /// <summary>Exactly one item at a time (the default).</summary>
    One,

    /// <summary>Any click or Space toggles an item's membership.</summary>
    MultiSimple,

    /// <summary>Windows-style: a plain click replaces the selection, Ctrl+click toggles, Shift+click
    /// selects the range from the anchor.</summary>
    MultiExtended,
}
