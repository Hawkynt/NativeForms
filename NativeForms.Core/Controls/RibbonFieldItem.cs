namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for the ribbon items that are fields rather than buttons — <see cref="RibbonComboBox"/>
/// and <see cref="RibbonSpinner"/>: an optional caption (<see cref="ToolStripItem.Text"/>, and an icon)
/// followed by a framed value box of <see cref="FieldWidth"/> pixels. A field always takes one stacked
/// row, whatever its <see cref="RibbonItem.ItemSize"/> says, because a box stretched over a whole group
/// height would read as anything but a field.
/// </summary>
/// <remarks>
/// Fields are drawn by the ribbon like every other item, so they work wherever the group is shown —
/// the expanded ribbon, a collapsed group's flyout and the minimized tab flyout — which a hosted
/// native control (<see cref="RibbonHostItem"/>) cannot follow into a popup.
/// </remarks>
public abstract class RibbonFieldItem : RibbonItem {
  /// <summary>Creates a field, one stacked row tall.</summary>
  private protected RibbonFieldItem() => this.ItemSize = RibbonItemSize.Small;

  /// <summary>The pixel width of the value box, not counting the caption beside it.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
  public int FieldWidth {
    get => field;
    set {
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
      if (field == value)
        return;

      field = value;
      this.NotifyOwner();
    }
  } = 80;

  /// <summary>The text the value box shows; never allocates once warm, so the paint path can read it.</summary>
  internal abstract string FieldText { get; }

  /// <summary>Tells the owning ribbon the value changed, so it repaints wherever the field is shown.</summary>
  private protected void NotifyValueChanged() => this.NotifyOwner();
}
