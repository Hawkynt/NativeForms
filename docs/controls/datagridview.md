# DataGridView

> The flagship owner-drawn control: a vertically virtualized data grid whose rows are arbitrary
> objects and whose cells are produced by reflection-free selector lambdas — millions of rows at
> constant per-row cost, painted in the native theme.

`Hawkynt.NativeForms.DataGridView` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var grid = new DataGridView { Bounds = new(0, 0, 200, 110) }; // 22 px header + 4 rows at 22 px
grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });

grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25)]);
grid.AlternatingRows = true;
grid.SelectionChanged += (_, _) => Console.WriteLine(grid.SelectedItem);

// One-way binding convenience: snapshot any sequence into Items.
grid.DataSource = new[] { new Person("Carol", 40) };

sealed record Person(string Name, int Age);
```

A column's `ValueSelector` maps the row object to the cell value (rendered via `ToString()`); an
optional `ImageSelector` adds a per-cell icon in front of the text. Both are plain lambdas — no
property names, no reflection.

## API

Inherits the common members of [`Control`](control.md).

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Columns` | `IList<DataGridViewColumn>` | empty | The columns shown. Mutate, then call `Invalidate()` to repaint. |
| `Items` | `ObservableList<object?>` | empty | The row items. Mutating the collection repaints the control. |
| `RowHeight` | `int` | theme row height | Pixel height of a data row. |
| `ColumnHeaderHeight` | `int` | `RowHeight` | Pixel height of the column-header row. |
| `ShowColumnHeaders` | `bool` | `true` | Whether the header row is painted. |
| `ShowGridLines` | `bool` | `true` | Whether horizontal and vertical grid lines are painted. |
| `AlternatingRows` | `bool` | `false` | Whether every other data row is tinted with `AlternatingRowColor`. |
| `AlternatingRowColor` | `Color` | `#F6F6F6` | Background tint of alternating rows. |
| `HorizontalOffset` | `int` | `0` | Horizontal scroll offset in pixels; columns are shifted left by this amount. Clamped to ≥ 0. |
| `SelectedRowIndex` | `int` | `-1` | Selected row index, `-1` for none. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `object?` | `null` | The selected row item; setting selects by `IndexOf`. |
| `TopRow` | `int` (get) | `0` | Index of the first visible data row (vertical scroll position). |
| `DataSource` | `IEnumerable?` (set) | — | Clears `Items` and copies the sequence in (one-way snapshot, not a live view). |

### Events

| Event | Description |
|---|---|
| `SelectionChanged` | Raised when `SelectedRowIndex` changes. |

### Methods

| Method | Description |
|---|---|
| `EnsureVisible(int rowIndex)` | Scrolls so the given data row is inside the visible range. |

### DataGridViewColumn

Constructor: `DataGridViewColumn(string headerText, Func<object?, object?> valueSelector)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `HeaderText` | `string` | ctor | Text painted in the column header. |
| `Width` | `int` | `100` | Column width in pixels. |
| `Alignment` | `ContentAlignment` | `MiddleLeft` | Alignment of header and cell content. |
| `ValueSelector` | `Func<object?, object?>` | ctor | Maps a row item to the cell value, rendered via `ToString()`. |
| `ImageSelector` | `Func<object?, IImage?>?` | `null` | Optional per-cell icon in front of the text; `null` result means none. |

There are no distinct column-type classes yet: one `DataGridViewColumn` covers the PRD's "text" and
"image" column types through `ValueSelector` and `ImageSelector`.

## Notes

**Virtualization.** The paint loop covers only `TopRow` through `TopRow + VisibleRowCount` (plus one
partial row); everything else on screen is header, grid lines and border. Cell values and icons are
materialized on demand inside the paint loop — the grid holds no cell objects, no row views and no
cached layout, so its own memory stays constant regardless of row count. What scales with the data
is only the `Items` list of row references. The test suite proves the bound: 100 000 rows produce
fewer than 32 text draw operations per paint.

**Binding.** Rows are plain objects in an `ObservableList<object?>`; its `ListChanged` event
repaints and clamps selection and vertical scroll (removing the selected row moves selection to the
last valid index). Cells come from `ValueSelector`/`ImageSelector` lambdas — no reflection, no
`TypeDescriptor`, trim/NativeAOT-safe. `DataSource` is a set-only convenience that snapshots any
`IEnumerable` into `Items`. Binding is one-way: the grid renders row state, it does not write back.

**Selection.** Full-row only: clicking a data row (below the header band) focuses the grid, selects
that row and raises `SelectionChanged`; clicks inside the header select nothing.

**Keyboard and wheel.** Up/Down move the selection by one row, PageUp/PageDown by one visible page,
Home/End jump to first/last row. The wheel scrolls `TopRow` by three rows per notch without changing
the selection. Selection changes call `EnsureVisible`.

**Header and styling.** The header row paints in the theme's header background/text colors with a
border line beneath; data rows use field background, selection background/text and control text,
grid lines use the theme grid-line color. `AlternatingRows` tints odd rows with
`AlternatingRowColor` (selection wins over the tint). `HorizontalOffset` shifts header and cells
left in lockstep — the programmatic half of horizontal scrolling; the interactive scrollbar is
pending.

**Not yet implemented** (per `docs/PRD.md` §7.4): check/button/combo/link column types; row/column
resize; frozen columns; cell editing, validation, formatting; sorting; extra selection modes;
clipboard copy/paste; per-cell styles; DPI + dark mode; row headers; column drag-reorder; column
auto-size modes; interactive horizontal scrollbar.
