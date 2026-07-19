namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="ComboBox"/> presents its field, matching <c>System.Windows.Forms.ComboBoxStyle</c>.</summary>
public enum ComboBoxStyle
{
    /// <summary>An editable text field with a drop-down list; typed text may differ from every item.</summary>
    DropDown,

    /// <summary>A closed, non-editable field showing the selected item; the list is the only way to change it.</summary>
    DropDownList,

    /// <summary>An editable text field with the list permanently visible below it. Not implemented yet.</summary>
    Simple,
}
