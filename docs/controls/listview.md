# ListView

> An owner-drawn list view painted in the native theme, covering the full view family — `Details`
> (multi-column grid with a header row), `List`, `LargeIcon`, `SmallIcon` and `Tile` — with groups
> flattened into header rows, vetoable check boxes, extended Ctrl/Shift multi-selection, in-place
> sorting, label editing through a hosted native text box, and painting virtualized to the visible
> row window in every view.

`Hawkynt.NativeForms.ListView` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var list = new ListView { Bounds = new(0, 0, 300, 220) }; // Details by default
list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
list.Items.AddRange(
[
    new ListViewItem("File1", "1 KB"),
    new ListViewItem("File2", "2 KB"),
    new ListViewItem("File3", "3 KB"),
]);
list.SelectedIndexChanged += (_, _) => Console.WriteLine(list.SelectedItems.Count);

list.Sorting = SortOrder.Ascending; // sorts Items in place; header clicks re-sort and flip

var docs = new ListViewGroup("Documents");
list.Groups.Add(docs);
list.Items[0].Group = docs; // File1 now renders under the "Documents" header row

list.CheckBoxes = true;
list.ItemCheck += (_, e) => e.NewValue &= e.Index != 0; // veto: item 0 stays unchecked
list.ItemChecked += (_, e) => Console.WriteLine($"{e.Item.Text}: {e.Item.Checked}");

list.LabelEdit = true;              // F2 (or BeginEdit) opens the hosted editor
list.View = ListViewView.LargeIcon; // icon grid; SmallIcon and Tile flow the same way
```

## API

Inherits the common members of [`Control`](control.md).

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Columns` | `ObservableList<ColumnHeader>` | empty | The columns for Details view. Mutating the collection repaints the control. |
| `Items` | `ObservableList<ListViewItem>` | empty | The items shown. Mutating the collection repaints; selection, caret and a pending label edit shift and prune with it. |
| `Groups` | `ObservableList<ListViewGroup>` | empty | The groups items can join via `ListViewItem.Group`, rendered in this order. |
| `View` | `ListViewView` | `Details` | Item arrangement. Changing it commits a pending label edit. |
| `ShowColumnHeaders` | `bool` | `true` | Whether the header row is shown (Details view only). |
| `ShowGroups` | `bool` | `true` | Whether items render under their group's header section — in every view except `List`, like the classic control. No effect while `Groups` is empty. |
| `FullRowSelect` | `bool` | `true` | Whether the selection highlight spans the full Details row; when `false` it covers only the first column. |
| `MultiSelect` | `bool` | `true` | Whether Ctrl/Shift gestures can select more than one item. Turning it off collapses the selection to its first index. |
| `CheckBoxes` | `bool` | `false` | Whether every item shows a themed check box; see `ListViewItem.Checked`. |
| `LabelEdit` | `bool` | `false` | Whether the user can edit item labels; see `BeginEdit`. |
| `LargeImageList` | `ImageList?` | `null` | The icon store for `LargeIcon` and `Tile` (via `ListViewItem.ImageIndex`); its image size drives those views' cell size. |
| `SmallImageList` | `ImageList?` | `null` | The icon store for the remaining views; its image size drives the `SmallIcon` cell size. |
| `Sorting` | `SortOrder` | `None` | The automatic sort direction over the item text (or the last clicked column's text). Assigning `Ascending`/`Descending` sorts immediately; `None` stops sorting without restoring the old order. |
| `ItemSorter` | `Comparison<ListViewItem>?` | `null` | A custom item ordering — the delegate-shaped stand-in for the classic `ListViewItemSorter`. Wins over `Sorting`; assigning a non-null comparison sorts immediately. |
| `ItemHeight` | `int` | theme row height | Pixel height of a row and of the header. |
| `SelectedIndex` | `int` | `-1` | The first selected index, `-1` for none. Setting it replaces the whole selection and scrolls the item into view; out-of-range values coerce to `-1`. |
| `SelectedIndices` | `IReadOnlyList<int>` (get) | empty | The selected indices, always sorted ascending. |
| `SelectedItems` | `IReadOnlyList<ListViewItem>` (get) | empty | The selected items in index order — a live, allocation-free view over `SelectedIndices`. |
| `SelectedItem` | `ListViewItem?` | `null` | The first selected item; setting selects it alone (by `IndexOf`). |
| `FocusedIndex` | `int` (get) | `-1` | The caret item keyboard navigation operates on, `-1` before any interaction. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible flattened row (scroll position). Group header rows count as rows; in the icon views a row spans a whole rank of cells. |
| `IsEditing` | `bool` (get) | `false` | Whether a label edit is currently in progress. |

### Events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised once per gesture when the set of selected indices changes — a Shift range over ten rows is one event. |
| `ColumnClick` | Raised when a Details column header is clicked, before any automatic sort; `ColumnClickEventArgs.Column` is the index into `Columns`. |
| `ItemCheck` | Raised before an item's check state flips. `ItemCheckEventArgs` (the same type [`CheckedListBox`](checkedlistbox.md) uses) carries `Index`, `CurrentValue` and a writable `NewValue`; a handler vetoes the flip by resetting `NewValue` to `CurrentValue`. |
| `ItemChecked` | Raised after an item's check state flipped; `ItemCheckedEventArgs.Item` is the item. Vetoed flips raise nothing. |
| `AfterLabelEdit` | Raised after a label edit finished. `LabelEditEventArgs` carries the `Item` index and the entered `Label` (`null` when the edit was cancelled); setting `CancelEdit` discards the text and keeps the item's current label. |

### Methods

| Method | Description |
|---|---|
| `EnsureVisible(int index)` | Scrolls so the given item is inside the visible row window. |
| `GetItemBounds(int index)` | The item's cell rectangle in client coordinates for the current scroll position (possibly outside the visible area). Details and List cells span the full control width. Throws for out-of-range indices. |
| `Sort()` | Sorts `Items` in place — stably — by `ItemSorter` when set, else by the active column's text in the `Sorting` direction; a no-op while neither is active. Selected items stay selected and the caret follows its item. |
| `BeginEdit(int index)` | Starts editing the given item's label: a hosted native text box appears over it, pre-filled and fully selected. Throws `InvalidOperationException` while `LabelEdit` is off. |
| `EndEdit(bool cancel)` | Ends a pending label edit, committing the editor's text (or discarding it when `cancel` is set), then raises `AfterLabelEdit` — whose handler may still veto the commit. A no-op with no edit in progress. |

### ListViewItem

Constructors: `ListViewItem()`, `ListViewItem(string text)`,
`ListViewItem(string text, params string[] subItems)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | Primary label — first column in Details, the whole cell in every other view. Setting it repaints an attached control. |
| `SubItems` | `List<string>` (get) | empty | Texts for the remaining Details columns, in column order; the Tile view shows the first as its greyed second line. |
| `Image` | `IImage?` | `null` | The explicit icon; `null` falls back to `ImageIndex`. |
| `ImageIndex` | `int` | `-1` | Index into the owner's image list (large or small, per view), `-1` for none. |
| `Group` | `ListViewGroup?` | `null` | The group the item renders under, `null` for the default section. Changing it re-flattens the presentation. |
| `Tag` | `object?` | `null` | Arbitrary caller data. |
| `Selected` | `bool` | `false` | Whether the item is selected. Writes on an attached item change the owner's selection (respecting `MultiSelect`) and raise its event; detached items just store the flag. |
| `Checked` | `bool` | `false` | The check state. Writes on an attached item run through the vetoable `ItemCheck` pipeline; detached items flip silently. |

| Method | Description |
|---|---|
| `BeginEdit()` | Starts a label edit on the owning control; throws while detached or while the owner's `LabelEdit` is off. |

### ListViewGroup

Constructors: `ListViewGroup()`, `ListViewGroup(string header)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `Header` | `string` | `""` | The caption shown in the group's header row. |
| `Tag` | `object?` | `null` | Arbitrary caller data. |

### ColumnHeader

Constructors: `ColumnHeader()`, `ColumnHeader(string text)`, `ColumnHeader(string text, int width)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | Header caption. |
| `Width` | `int` | `120` | Column width in pixels. |
| `TextAlign` | `ContentAlignment` | `MiddleLeft` | Alignment of header and cell text. |

### ListViewView

All five members are painted — none falls back. `Details` is the multi-column grid with the
optional header; `List` a single full-width column that never groups. The remaining three flow
cells left-to-right in rows: `LargeIcon` centers the icon above a centered label, `SmallIcon` puts
the icon beside a fixed-width label, and `Tile` puts the large icon beside a two-line block — the
label above the greyed first sub-item.

## Notes

**Virtualization.** Every view paints flat rows: group header rows interleaved with runs of item
cells (one item per row in Details/List, a rank of cells in the icon views). Without grouping the
rows are pure arithmetic over the item indices — nothing is materialized; with grouping the display
order is flattened lazily, so structural changes only mark it dirty and the next access rebuilds.
Painting walks only the rows intersecting the client area, so 100 000 items cost the same handful
of operations as ten — in any view, grouped or not. The wheel scrolls three rows per notch.

**Grouping.** Groups render in `Groups` order as an accent-colored caption over a separator rule,
each followed by its member items in collection order; items with no (or an unlisted) group gather
under a trailing "Default" section, and empty groups are skipped. Header rows take no selection —
clicking one selects nothing — and keyboard navigation follows the display order across groups.
`ShowGroups = false` (or the `List` view) returns to plain model order.

**Selection.** The extended model of a `SelectionMode.MultiExtended` list box: a plain click or
arrow key replaces the selection, Ctrl+click toggles membership, and Shift+click or Shift+arrow
selects the range from the anchor. `SelectedIndices` stays sorted ascending, per-item `Selected`
flags stay in sync (writes to them join or leave the selection), removing items prunes and shifts
the indices, and every gesture raises at most one `SelectedIndexChanged` and one repaint. With
`CheckBoxes` off (and `MultiSelect` on), Space toggles the caret item's membership.

**Check boxes.** Details, List and SmallIcon draw the themed glyph inline before the icon and
label; LargeIcon and Tile overlay it in the cell's top-left corner. A click on the glyph toggles
without selecting; a click past it selects without toggling; Space toggles every selected item (or
the caret item with nothing selected). Every flip — mouse, keyboard or a `Checked` write — raises
`ItemCheck` first (still unflipped, vetoable) and `ItemChecked` after.

**Sorting.** `Sort` mutates the order of `Items` itself — deliberately unlike
[`DataGridView`](datagridview.md), which sorts through an index indirection because its rows are
arbitrary bound objects the grid must not reorder. The list view owns its items, so item indices
always equal presentation order and callers observe the sorted list, exactly like the classic
control. The sort is stable: equal keys keep their relative order. A Details header click always
raises `ColumnClick`; with `Sorting` active it then sorts by the clicked column (column 0 compares
`Text`, the rest their sub-item text) and repeat clicks on the sorted column flip the direction,
while an `ItemSorter` — which ignores columns — is just re-applied. The active column carries a
themed sort arrow. Items are not kept sorted automatically — after bulk mutation, call `Sort`
again.

**Label editing.** `BeginEdit` hosts a native [`TextBox`](textbox.md) over the item's label,
pre-filled and fully selected for overtyping. There is no toolkit-wide focus model yet, so no
reliable native focus-loss moment exists; edits commit at the honest points available: Enter, any
click on the list, `EndEdit`, starting another edit and changing `View`. Escape cancels (the event
reports a `null` label). `AfterLabelEdit` runs on every outcome and can still veto the commit;
removing the edited item abandons the edit without an event. F2 edits the caret item.

**Icons.** An item's explicit `Image` wins; otherwise `ImageIndex` resolves against
`LargeImageList` in the LargeIcon and Tile views and against `SmallImageList` everywhere else.
Details and List scale the icon to the row height minus 4 px; the icon views draw it at the image
list's native size, and that size drives their cell geometry (defaults: 32×32 large, 16×16 small).

**Keyboard.** Up/Down move by one row — one rank of cells in the icon views, where Left/Right move
by one cell. Home/End jump to the first/last item, PageUp/PageDown by one visible page. Navigation
maps through the display order, and selection changes scroll the caret into view.

**Sub-item mutation.** `SubItems` is a plain `List<string>` with no change notification — mutating
it in place does not repaint. Setting `Text` (or any collection or selection change) does; after
in-place sub-item edits, call `Invalidate()`.

**Not yet implemented** (per `docs/PRD.md` §7.4): a virtual-mode item API.
