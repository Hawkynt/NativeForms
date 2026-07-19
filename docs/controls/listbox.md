# ListBox

> An owner-drawn single-selection list with optional per-item icons, wheel/keyboard scrolling and a
> one-way `DataSource` snapshot — items are arbitrary objects rendered through reflection-free
> selector delegates.

`Hawkynt.NativeForms.ListBox` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

var list = new ListBox { Bounds = new(0, 0, 120, 88) }; // 4 rows at the default 22 px row height
list.DisplaySelector = static item => ((Device)item!).Name;
list.ImageSelector = static item => ((Device)item!).Icon;
list.Items.AddRange([new Device("Printer", null), new Device("Scanner", null)]);
list.SelectedIndexChanged += (_, _) => Console.WriteLine(list.SelectedIndex);

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
| `SelectedIndex` | `int` | `-1` | Selected item index, `-1` for none. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `object?` | `null` | The selected item; setting selects by `IndexOf`. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible row (scroll position). |
| `DataSource` | `IEnumerable?` (set) | — | Clears `Items` and copies the sequence in (one-way snapshot, not a live view). |

### Events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes. |

### Methods

| Method | Description |
|---|---|
| `EnsureVisible(int index)` | Scrolls so the given index is inside the visible row range. |

## Notes

**Virtualization.** `OnPaint` walks only the visible row window (`TopIndex` through
`TopIndex + VisibleRowCount`, plus one partial row); item count does not affect paint cost. The
control keeps no per-item UI state — one scroll index and one selection index besides the item list
itself.

**Binding.** Text and icon come from `DisplaySelector`/`ImageSelector` lambdas — no reflection, no
`TypeDescriptor`, trim/NativeAOT-safe. `Items` is an `ObservableList<object?>`; its `ListChanged`
event drives the repaint and clamps selection and scroll (removing the selected item moves selection
to the last valid index). `DataSource` is a set-only convenience that snapshots any `IEnumerable`
into `Items`.

**Theming.** Painted with the backend's `ITheme`: field background, selection background/text,
control text, border, default font. The default `ItemHeight` is the theme row height.

**Keyboard and mouse.** Left-click focuses the control and selects the row under the cursor.
Up/Down move by one row, PageUp/PageDown by one visible page, Home/End jump to first/last item.
The wheel scrolls three rows per notch without changing the selection. Selection changes call
`EnsureVisible`.

**Not yet implemented** (per `docs/PRD.md` §7.4): multi-selection (`SelectionMode`).
