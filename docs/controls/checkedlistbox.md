# CheckedListBox

> A [`ListBox`](listbox.md) whose rows carry a themed check square in front of the text — checking is

![CheckedListBox in the NativeForms demo](../screenshots/03-lists.png)
> independent of selection, every flip is announced through the vetoable `ItemCheck` event, and the
> check states stay index-aligned as items mutate.

`Hawkynt.NativeForms.CheckedListBox` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var list = new CheckedListBox { Bounds = new(0, 0, 120, 110) }; // 5 rows at the default 22 px row height
list.Items.AddRange(["Alpha", "Beta", "Gamma"]);
list.CheckOnClick = true; // every click toggles; the default needs a click on the already-selected row

list.ItemCheck += (_, e) =>
{
    if (e.Index == 0)
        e.NewValue = e.CurrentValue; // veto: row 0 keeps its state
};

list.SetItemChecked(1, true);
Console.WriteLine(string.Join(", ", list.CheckedItems)); // Beta
```

## API

Inherits everything from [`ListBox`](listbox.md) — items, selectors, selection modes, scrolling.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `CheckOnClick` | `bool` | `false` | Whether a single click toggles the check. Otherwise the first click on a row only selects it and a further click toggles. |
| `CheckedIndices` | `IReadOnlyList<int>` (get) | empty | The checked indices, sorted ascending — a live view over the check states. |
| `CheckedItems` | `IReadOnlyList<object?>` (get) | empty | The checked items in index order — a live view over the check states. |

### Events

| Event | Description |
|---|---|
| `ItemCheck` | Raised before an item's check state flips. `ItemCheckEventArgs` carries `Index`, `CurrentValue` and a writable `NewValue`; a handler vetoes the flip by resetting `NewValue` to `CurrentValue` (or redirects it to any state). |

### Methods

| Method | Description |
|---|---|
| `GetItemChecked(int index)` | Whether the item at the given index is checked. |
| `SetItemChecked(int index, bool value)` | Sets the check state, raising `ItemCheck` first. Setting the state an item already has does nothing. |

## Notes

**Check versus selection.** Left-click runs the normal selection gesture first (focus + select, like
the classic control), then toggles the check when the row was already selected before the press — or
on every click with `CheckOnClick`. Space toggles the check of every selected row; in the multi
selection modes this replaces the inherited Space gesture (which would toggle selection membership).
All toggling funnels through `SetItemChecked`, so the `ItemCheck` veto covers mouse, keyboard and
code alike, and the state is still unflipped while the event runs.

**Item mutation.** The check states are a parallel list kept aligned by the `Items` change pipeline:
inserts start unchecked, removals drop the state so surviving items keep theirs at their new index,
replacing an item resets its state, and a reset clears everything.

**Painting.** Each row draws the shared themed check glyph (`GlyphRenderer`, the same 14 px square
and checkmark strokes `CheckBox` uses), vertically centered, then delegates the rest of the row —
icon and text, shifted right past the glyph — to the `ListBox` row painter. Virtualization is
inherited: only the visible row window is painted, whatever the item count.

## Differences from System.Windows.Forms.CheckedListBox

- **`ItemCheckEventArgs` is `bool`-based**, not `CheckState`-based — there is no indeterminate state — and its constructor order is `(index, currentValue, newValue)`, the reverse of WinForms' `(index, newCheckValue, currentCheckValue)`. Port handlers by property name (`CurrentValue`/`NewValue`), not argument position.
- **Multi-selection is allowed**: the control inherits every `SelectionMode` (WinForms forces `One`), and **Space toggles the check of *all* selected rows**, not just the caret item.
- `CheckOnClick`, `GetItemChecked`/`SetItemChecked` and `CheckedItems`/`CheckedIndices` match WinForms; there is no `CheckedItemCollection` class — the live views are plain `IReadOnlyList`s.
