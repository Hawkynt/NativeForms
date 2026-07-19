using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Describes a pending check-state flip in a <see cref="CheckedListBox"/>. Raised before the state
/// changes; a handler vetoes the flip by resetting <see cref="NewValue"/> to <see cref="CurrentValue"/>.
/// </summary>
public sealed class ItemCheckEventArgs(int index, bool currentValue, bool newValue) : EventArgs
{
    /// <summary>The index of the item about to flip.</summary>
    public int Index { get; } = index;

    /// <summary>The item's check state right now.</summary>
    public bool CurrentValue { get; } = currentValue;

    /// <summary>The state the item is about to take; writable so a handler can override or veto it.</summary>
    public bool NewValue { get; set; } = newValue;
}

/// <summary>
/// A <see cref="ListBox"/> whose rows carry a themed check square in front of the text. Checking is
/// independent of selection: by default the first click on a row selects it and a further click
/// toggles its check (<see cref="CheckOnClick"/> makes every click toggle), Space toggles the
/// selected rows, and every flip is announced through the vetoable <see cref="ItemCheck"/> event.
/// </summary>
public class CheckedListBox : ListBox
{
    private const int _GlyphGap = 4;

    /// <summary>Per-item check states, index-aligned with <see cref="ListBox.Items"/>.</summary>
    private readonly List<bool> _checkStates = [];

    /// <summary>Whether a single click toggles the check; otherwise a row must be selected first and
    /// only a click on the already-selected row toggles.</summary>
    public bool CheckOnClick { get; set; }

    /// <summary>The checked row indices, sorted ascending. A live view over the check states.</summary>
    public IReadOnlyList<int> CheckedIndices => field ??= new CheckedIndexList(this);

    /// <summary>The checked items, in index order. A live view over the check states.</summary>
    public IReadOnlyList<object?> CheckedItems => field ??= new CheckedItemList(this);

    /// <summary>Raised before an item's check state flips; see <see cref="ItemCheckEventArgs"/>.</summary>
    public event EventHandler<ItemCheckEventArgs>? ItemCheck;

    /// <summary>Whether the item at the given index is checked.</summary>
    public bool GetItemChecked(int index) => _checkStates[index];

    /// <summary>
    /// Sets the item's check state, raising <see cref="ItemCheck"/> first; a handler may veto or
    /// redirect the change. Setting the state an item already has does nothing.
    /// </summary>
    public void SetItemChecked(int index, bool value)
    {
        var current = _checkStates[index];
        if (current == value)
            return;

        var args = new ItemCheckEventArgs(index, current, value);
        this.OnItemCheck(args);
        if (args.NewValue == current)
            return;

        _checkStates[index] = args.NewValue;
        this.Invalidate();
    }

    /// <summary>Raises <see cref="ItemCheck"/>.</summary>
    protected virtual void OnItemCheck(ItemCheckEventArgs e) => this.ItemCheck?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnItemsChanged(ListChangedEventArgs e)
    {
        switch (e.ChangeType)
        {
            case ListChangeType.Added:
                _checkStates.Insert(e.Index, false);
                break;

            case ListChangeType.Removed:
                _checkStates.RemoveAt(e.Index);
                break;

            case ListChangeType.Replaced:
                _checkStates[e.Index] = false;
                break;

            case ListChangeType.Reset:
                _checkStates.Clear();
                for (var i = this.Items.Count; i > 0; --i)
                    _checkStates.Add(false);
                break;
        }

        base.OnItemsChanged(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        var row = this.IndexFromPoint(e.X, e.Y);
        var toggles = row >= 0 && (this.CheckOnClick || this.GetSelected(row));
        base.OnMouseDown(e); // focus + selection gesture first, like the classic control
        if (toggles)
            this.SetItemChecked(row, !this.GetItemChecked(row));
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space)
        {
            base.OnKeyDown(e);
            return;
        }

        var selected = this.SelectedIndices;
        for (var i = 0; i < selected.Count; ++i)
            this.SetItemChecked(selected[i], !this.GetItemChecked(selected[i]));

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnDrawRow(IGraphics g, int index, Rectangle bounds, bool selected)
    {
        var boxTop = bounds.Y + Math.Max(0, (bounds.Height - GlyphRenderer.CheckBoxSize) / 2);
        GlyphRenderer.DrawCheckBox(g, this.Theme, new(bounds.X + 2, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize), _checkStates[index]);

        var indent = GlyphRenderer.CheckBoxSize + _GlyphGap + 2;
        base.OnDrawRow(g, index, new Rectangle(bounds.X + indent, bounds.Y, bounds.Width - indent, bounds.Height), selected);
    }

    /// <summary>A live, sorted view of the indices whose check state is on.</summary>
    private sealed class CheckedIndexList(CheckedListBox owner) : IReadOnlyList<int>
    {
        public int Count
        {
            get
            {
                var count = 0;
                var states = owner._checkStates;
                for (var i = 0; i < states.Count; ++i)
                    if (states[i])
                        ++count;

                return count;
            }
        }

        public int this[int index]
        {
            get
            {
                var states = owner._checkStates;
                for (var i = 0; i < states.Count; ++i)
                    if (states[i] && index-- == 0)
                        return i;

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (var i = 0; i < owner._checkStates.Count; ++i)
                if (owner._checkStates[i])
                    yield return i;
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    /// <summary>A live view of the items whose check state is on, in index order.</summary>
    private sealed class CheckedItemList(CheckedListBox owner) : IReadOnlyList<object?>
    {
        public int Count => owner.CheckedIndices.Count;

        public object? this[int index] => owner.Items[owner.CheckedIndices[index]];

        public IEnumerator<object?> GetEnumerator()
        {
            for (var i = 0; i < owner._checkStates.Count; ++i)
                if (owner._checkStates[i])
                    yield return owner.Items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
