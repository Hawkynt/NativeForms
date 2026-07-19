# ListBox

> An owner-drawn list with the full set of WinForms selection modes, optional per-item icons,
> wheel/keyboard scrolling and a one-way `DataSource` snapshot — items are arbitrary objects rendered
> through reflection-free selector delegates.

`Hawkynt.NativeForms.ListBox` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

var list = new ListBox { Bounds = new(0, 0, 120, 88) }; // 4 rows at the default 22 px row height
list.DisplaySelector = static item => ((Device)item!).Name;
list.ImageSelector = static item => ((Device)item!).Icon;
list.Items.AddRange([new Device("Printer", null), new Device("Scanner", null)]);
list.SelectedIndexChanged += (_, _) => Console.WriteLine(string.Join(", ", list.SelectedIndices));

// Windows-style multi-selection: plain click replaces, Ctrl toggles, Shift ranges from the anchor.
list.SelectionMode = SelectionMode.MultiExtended;

// One-way binding convenience: snapshot any sequence into Items.
list.DataSource = new[] { new Device("Camera", null) };

sealed record Device(string Name, IImage? Icon);
```

Plain values work without any selector: the default `DisplaySelector` calls `ToString()`, so
`list.Items.AddRange(["a", "b", "c"])` renders as-is.

## API

Inherits the common members of [`Control`](control.md).

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Items` | `ObservableList<object?>` | empty | The items shown. Mutating the collection repaints the control. |
| `DisplaySelector` | `Func<object?, string>` | `ToString()` | Produces the display text for an item. Setting `null` restores the default. |
| `ImageSelector` | `Func<object?, IImage?>?` | `null` | Optional selector producing a leading icon per item; `null` result means none. |
| `ItemHeight` | `int` | theme row height | Pixel height of a row. |
| `SelectionMode` | `SelectionMode` | `One` | How the user selects: `None`, `One`, `MultiSimple` or `MultiExtended`. Changing the mode clears the selection. |
| `SelectedIndex` | `int` | `-1` | The first selected index, `-1` for none. Setting replaces the whole selection with the one item (in `None` mode it only moves the caret); out-of-range values coerce to `-1`. |
| `SelectedIndices` | `IReadOnlyList<int>` (get) | empty | The selected indices, always sorted ascending regardless of click order. |
| `SelectedItems` | `IReadOnlyList<object?>` (get) | empty | The selected items in index order — a live view over `SelectedIndices`. |
| `SelectedItem` | `object?` | `null` | The first selected item; setting selects by `IndexOf`. |
| `FocusedIndex` | `int` (get) | `-1` | The caret row keyboard navigation operates on — independent of the selection in the multi modes. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible row (scroll position). |
| `DataSource` | `IEnumerable?` (set) | — | Clears `Items` and copies the sequence in (one-way snapshot, not a live view). |

### Events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised once per gesture when the set of selected indices changes — a Shift+click range is one event, not one per row. |

### Methods

| Method | Description |
|---|---|
| `EnsureVisible(int index)` | Scrolls so the given index is inside the visible row range. |
| `GetSelected(int index)` | Whether the row at the given index is selected. |
| `IndexFromPoint(int x, int y)` | The row index at the given client coordinates, or `-1` for none. |

## Notes

**Virtualization.** `OnPaint` walks only the visible row window (`TopIndex` through
`TopIndex + VisibleRowCount`, plus one partial row); item count does not affect paint cost. The
control keeps no per-item UI state — a scroll index, the caret/anchor and the sorted selection list
besides the item list itself.

**Selection model.** The selection lives in one sorted index list; caret (`FocusedIndex`) and anchor
are tracked separately, which is what makes the multi modes work: in `MultiSimple` arrows move only
the caret and Space toggles it, in `MultiExtended` a plain click or arrow replaces the selection,
Ctrl+click toggles, and Shift+click or Shift+arrow selects the range from the anchor. `None` selects
nothing at all — clicks and arrows just move the caret. Every gesture ends in at most one repaint
and one `SelectedIndexChanged`.

**Binding.** Text and icon come from `DisplaySelector`/`ImageSelector` lambdas — no reflection, no
`TypeDescriptor`, trim/NativeAOT-safe. `Items` is an `ObservableList<object?>`; its `ListChanged`
event drives the repaint and keeps selection, caret and anchor pointing at the same items: inserts
shift the indices, removals prune and shift, and in `One` mode removing the selected row hands the
selection to its neighbor, like the classic control. `DataSource` is a set-only convenience that
snapshots any `IEnumerable` into `Items`.

**Theming.** Painted with the backend's `ITheme`: field background, selection background/text,
control text, border, default font. Every selected row gets the selection highlight. The default
`ItemHeight` is the theme row height.

**Keyboard and mouse.** Left-click focuses the control and applies the mode's gesture to the row
under the cursor. Up/Down move by one row, PageUp/PageDown by one visible page, Home/End jump to
first/last item — selecting in `One` and `MultiExtended`, caret-only in `None` and `MultiSimple`.
Space toggles the caret row in both multi modes. The wheel scrolls three rows per notch without
changing the selection. Keyboard moves call `EnsureVisible`.
