# ListView

> An owner-drawn list view with `Details` (multi-column grid plus header row) and `List`
> (single-column) layouts, per-item icons, sub-items, single selection and virtualized painting for
> very large item counts.

`Hawkynt.NativeForms.ListView` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var list = new ListView { Bounds = new(0, 0, 300, 220) };
list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
list.Items.AddRange(
[
    new ListViewItem("File1", "1 KB"),
    new ListViewItem("File2", "2 KB"),
    new ListViewItem("File3", "3 KB"),
]);
list.SelectedIndexChanged += (_, _) => Console.WriteLine(list.SelectedItem?.Text);

// Single-column layout, no header row:
list.View = ListViewView.List;
```

## API

Inherits the common members of [`Control`](control.md).

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Columns` | `ObservableList<ColumnHeader>` | empty | Columns for Details view. Mutating the collection repaints the control. |
| `Items` | `ObservableList<ListViewItem>` | empty | The rows shown. Mutating the collection repaints the control. |
| `View` | `ListViewView` | `Details` | Item arrangement. |
| `ShowColumnHeaders` | `bool` | `true` | Whether the header row is shown (Details view only). |
| `FullRowSelect` | `bool` | `true` | Whether the selection highlight spans the full Details row; when `false` it covers only the first column. |
| `ItemHeight` | `int` | theme row height | Pixel height of a row and of the header. |
| `SelectedIndex` | `int` | `-1` | Selected row index, `-1` for none. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `ListViewItem?` | `null` | The selected item; setting selects by `IndexOf`. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible row (scroll position). |

### Events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes. |

### Methods

| Method | Description |
|---|---|
| `EnsureVisible(int index)` | Scrolls so the given index is inside the visible row range. |

### ListViewItem

Constructors: `ListViewItem()`, `ListViewItem(string text)`,
`ListViewItem(string text, params string[] subItems)`.

| Member | Type | Description |
|---|---|---|
| `Text` | `string` | Primary label — first column in Details, the whole row in List view. |
| `SubItems` | `List<string>` | Texts for the remaining Details columns, in column order. |
| `Image` | `IImage?` | Optional leading icon in the primary cell. |
| `Tag` | `object?` | Arbitrary caller data. |
| `Selected` | `bool` | Whether the item is currently selected; kept in sync by `SelectedIndex`. |

### ColumnHeader

Constructors: `ColumnHeader()`, `ColumnHeader(string text)`, `ColumnHeader(string text, int width)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | Header caption. |
| `Width` | `int` | `120` | Column width in pixels. |
| `TextAlign` | `ContentAlignment` | `MiddleLeft` | Alignment of header and cell text. |

### ListViewView

`Details` and `List` are painted today. `LargeIcon`, `SmallIcon` and `Tile` exist as enum members
for API shape but currently fall back to the `List` layout.

## Notes

**Virtualization.** Both layouts paint only the visible row window (`TopIndex` through
`TopIndex + VisibleRowCount`, plus one partial row); 50 000 items cost the same per paint as five.
Scroll state is a single top index — no per-row peers or cached layout. Each Details cell is clipped
to its column rectangle, so long text never bleeds into the neighbor column.

**Details vs. List.** Details paints the optional header row (theme header background/text, border
separators), then per row the primary cell — icon (`Image`, sized to the row height) plus `Text` —
followed by one cell per extra column from `SubItems[column - 1]`; missing sub-items render empty.
With no columns configured, Details paints the primary cell full-width. List paints only the primary
cell, full width, no header.

**Binding.** `Columns` and `Items` are `ObservableList`s; their `ListChanged` events repaint and
clamp selection and scroll (removing the selected row moves selection to the last valid index).
There is no reflection anywhere on the paint or update path.

**Theming.** Painted with the backend's `ITheme`: field/header/selection backgrounds, control,
header and selection text colors, border, default font. `ItemHeight` defaults to the theme row
height.

**Keyboard and mouse.** Left-click focuses the control and selects the row under the cursor; clicks
in the header band select nothing. Up/Down move by one row, PageUp/PageDown by one visible page,
Home/End jump to first/last item. The wheel scrolls three rows per notch without changing the
selection. Selection changes call `EnsureVisible`.

**Not yet implemented** (per `docs/PRD.md` §7.4): `LargeIcon`/`SmallIcon`/`Tile` views, groups,
checkboxes, virtual-mode item API, label editing, sorting, multi-selection.
