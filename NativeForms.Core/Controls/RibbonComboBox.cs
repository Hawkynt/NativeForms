using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// A drop-down list on the ribbon — the <c>ComboBoxStyle.DropDownList</c> kind: a caption, a box showing
/// the selected entry and a chevron that opens <see cref="Items"/> in a popup under the box. Owner-drawn
/// by the ribbon, so it sizes to a small row and keeps working inside a collapsed group's flyout.
/// </summary>
/// <remarks>
/// The list is created on first access to <see cref="Items"/>, so a combo box costs a few fields until
/// it is filled. Keyboard: once the box has been clicked, Alt+Down or F4 opens the list and Up/Down
/// change the selection; in the open list the arrows walk it, Enter commits and Escape closes it
/// unchanged.
/// </remarks>
public class RibbonComboBox : RibbonFieldItem {
  private ObservableList<string>? _items;
  private int _selectedIndex = -1;

  /// <summary>Creates a combo box without a caption.</summary>
  public RibbonComboBox() { }

  /// <summary>Creates a combo box with the given caption beside its box.</summary>
  public RibbonComboBox(string text) => this.Text = text;

  /// <summary>The entries the list offers, in order.</summary>
  public ObservableList<string> Items => _items ??= this.CreateItems();

  /// <summary>
  /// The index of the selected entry, or -1 for none. An index outside <see cref="Items"/> selects
  /// nothing; removing the selected entry clears the selection, and inserting or removing entries before
  /// it keeps the same entry selected.
  /// </summary>
  public int SelectedIndex {
    get => _selectedIndex;
    set {
      var clamped = value < 0 || value >= this.ItemCount ? -1 : value;
      if (clamped == _selectedIndex)
        return;

      _selectedIndex = clamped;
      this.NotifyValueChanged();
      this.OnSelectedIndexChanged(EventArgs.Empty);
    }
  }

  /// <summary>The selected entry, or <see langword="null"/> while nothing is selected.</summary>
  public string? SelectedItem => _selectedIndex >= 0 ? _items![_selectedIndex] : null;

  /// <summary>Raised after <see cref="SelectedIndex"/> changes, by the user or by assignment.</summary>
  public event EventHandler? SelectedIndexChanged;

  /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
  protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

  /// <summary>The number of entries, without creating the list.</summary>
  internal int ItemCount => _items?.Count ?? 0;

  /// <inheritdoc/>
  internal override string FieldText => this.SelectedItem ?? string.Empty;

  private ObservableList<string> CreateItems() {
    var items = new ObservableList<string>();
    items.ListChanged += this.OnItemsChanged;
    return items;
  }

  /// <summary>Keeps the selection on the same entry across edits to the list.</summary>
  private void OnItemsChanged(object? sender, ListChangedEventArgs e) {
    var selected = _selectedIndex;
    var adjusted = selected < 0
        ? -1
        : e.ChangeType switch {
          ListChangeType.Added when e.Index <= selected => selected + 1,
          ListChangeType.Removed when e.Index == selected => -1,
          ListChangeType.Removed when e.Index < selected => selected - 1,
          ListChangeType.Moved when e.OldIndex == selected => e.Index,
          ListChangeType.Moved when e.OldIndex < selected && e.Index >= selected => selected - 1,
          ListChangeType.Moved when e.OldIndex > selected && e.Index <= selected => selected + 1,
          _ => selected < this.ItemCount ? selected : -1,
        };

    _selectedIndex = adjusted;
    this.NotifyValueChanged(); // the shown text may have changed even when the index did not

    // A shifted index names the same entry, so only a lost selection is a change worth announcing.
    if (selected >= 0 && adjusted < 0)
      this.OnSelectedIndexChanged(EventArgs.Empty);
  }
}
