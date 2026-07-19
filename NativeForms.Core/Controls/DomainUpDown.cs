using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// A spinner over a list of strings: the hosted editor shows the selected item, the spinner buttons
/// and Up/Down keys walk through <see cref="Items"/> (wrapping around the ends when <see cref="Wrap"/>
/// is on), and typing an item's text selects it at the next commit point.
/// </summary>
/// <remarks>
/// A typed edit is committed at the base class's commit points (before a step, on focus loss): the
/// text is matched case-insensitively against <see cref="Items"/> — a hit selects that item and
/// normalizes the casing, a miss reverts the editor to the current item.
/// </remarks>
public class DomainUpDown : UpDownBase
{
    private int _selectedIndex = -1;

    /// <summary>Creates an empty spinner with nothing selected.</summary>
    public DomainUpDown()
    {
        this.Items = new();
        this.Items.ListChanged += this.OnItemsListChanged;
    }

    /// <summary>The items the spinner walks through. Mutations keep the selection on the same item.</summary>
    public ObservableList<string> Items { get; }

    /// <summary>Whether stepping past either end wraps around to the other.</summary>
    public bool Wrap { get; set; }

    /// <summary>The selected item's index, or -1 for none. Setting it mirrors the item into the
    /// editor and raises <see cref="SelectedItemChanged"/> when the value actually changes.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (_selectedIndex == clamped)
                return;

            _selectedIndex = clamped;
            this.UpdateEditText();
            this.OnSelectedItemChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected item, or <see langword="null"/> for none.</summary>
    public string? SelectedItem
    {
        get => _selectedIndex >= 0 ? this.Items[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>Raised when the selection changes, by stepping, typing or assignment.</summary>
    public event EventHandler? SelectedItemChanged;

    /// <summary>Raises <see cref="SelectedItemChanged"/>.</summary>
    protected virtual void OnSelectedItemChanged(EventArgs e) => this.SelectedItemChanged?.Invoke(this, e);

    /// <inheritdoc/>
    public override void UpButton()
    {
        this.CommitEdit();
        this.MoveSelection(-1);
    }

    /// <inheritdoc/>
    public override void DownButton()
    {
        this.CommitEdit();
        this.MoveSelection(+1);
    }

    /// <inheritdoc/>
    protected override void UpdateEditText()
        => this.SetEditorText(_selectedIndex >= 0 ? this.Items[_selectedIndex] : string.Empty);

    /// <inheritdoc/>
    protected override void ValidateEditText()
    {
        var match = this.IndexOfText(this.Text);
        if (match >= 0)
            this.SelectedIndex = match; // a no-op when the same item is already selected

        this.UpdateEditText(); // normalize the casing on a match, revert on a miss
    }

    /// <summary>Walks the selection by <paramref name="delta"/>: from nothing to the first item,
    /// otherwise clamped at the ends or wrapped around them per <see cref="Wrap"/>.</summary>
    private void MoveSelection(int delta)
    {
        var count = this.Items.Count;
        if (count == 0)
            return;

        var next = _selectedIndex < 0 ? 0 : _selectedIndex + delta;
        this.SelectedIndex = this.Wrap ? (next % count + count) % count : Math.Clamp(next, 0, count - 1);
    }

    /// <summary>Finds the item matching <paramref name="text"/> case-insensitively; -1 for none.</summary>
    private int IndexOfText(string text)
    {
        for (var i = 0; i < this.Items.Count; ++i)
            if (string.Equals(this.Items[i], text, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    /// <summary>Keeps the selection pointing at the same item across item mutations: shifted by
    /// inserts/removes before it, cleared (with one event) when the selected item vanishes.</summary>
    private void OnItemsListChanged(object? sender, ListChangedEventArgs e)
    {
        var changed = false;
        switch (e.ChangeType)
        {
            case ListChangeType.Added:
                if (_selectedIndex >= e.Index)
                    ++_selectedIndex;
                break;

            case ListChangeType.Removed:
                if (_selectedIndex == e.Index)
                {
                    _selectedIndex = -1;
                    changed = true;
                }
                else if (_selectedIndex > e.Index)
                    --_selectedIndex;

                break;

            case ListChangeType.Reset:
                if (_selectedIndex >= this.Items.Count)
                {
                    _selectedIndex = -1;
                    changed = true;
                }

                break;
        }

        if (!changed)
            return;

        this.UpdateEditText();
        this.OnSelectedItemChanged(EventArgs.Empty);
    }
}
