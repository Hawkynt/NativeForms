namespace Hawkynt.NativeForms;

/// <summary>
/// A ribbon push button that, instead of firing a plain click, opens a <see cref="GridPicker"/> in a
/// popup under the button — the Office "Insert Table" affordance. The chosen dimensions come back
/// through <see cref="RangeSelected"/>; the ribbon owns the popup, so a hundred of these still cost a
/// hundred small item objects rather than a hundred native widgets.
/// </summary>
public class RibbonGridButton : RibbonButton
{
    /// <summary>Creates an empty grid button.</summary>
    public RibbonGridButton() { }

    /// <summary>Creates a large grid button with the given caption.</summary>
    public RibbonGridButton(string text)
        : base(text) { }

    /// <summary>Creates a grid button with the given caption and size.</summary>
    public RibbonGridButton(string text, RibbonItemSize size)
        : base(text, size) { }

    /// <summary>The greatest number of columns the picker offers; at least one.</summary>
    public int MaxColumns { get; set; } = 10;

    /// <summary>The greatest number of rows the picker offers; at least one.</summary>
    public int MaxRows { get; set; } = 8;

    /// <summary>Raised when the user commits a table size in the picker.</summary>
    public event EventHandler<GridRangeEventArgs>? RangeSelected;

    /// <summary>Fired by the owning ribbon when the picker commits a block.</summary>
    internal void RaiseRangeSelected(int rows, int columns)
        => this.RangeSelected?.Invoke(this, new GridRangeEventArgs(rows, columns));
}
