# TreeListView

> An owner-drawn TreeView × ListView hybrid: the first column renders the expandable `TreeNode`

![TreeListView in the NativeForms demo](../screenshots/03-lists.png)
> hierarchy, the remaining columns render per-node text from reflection-free selectors under a
> ListView-style header row. Selection is full-row; expand/collapse, checking and keyboard behave
> exactly like [`TreeView`](treeview.md) — the two controls share the engine.

`Hawkynt.NativeForms.TreeListView` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var tree = new TreeListView { Bounds = new(0, 0, 400, 242) }; // 22 px header + 10 rows
tree.Columns.AddRange(
[
    new TreeListViewColumn("Name", 200),
    new TreeListViewColumn("Size", 80, static n => n.Tag as string ?? string.Empty),
]);
var root = tree.Nodes.Add("root");
root.Tag = "10 KB";
root.Nodes.Add("child").Tag = "2 KB";

// Or build the whole tree from a data source — items ride along in each node's Tag:
tree.SetDataSource(rootFolders, static f => f.Name, static f => f.Children);

sealed record Folder(string Name, List<Folder> Children);
```

## API

Inherits the common members of [`Control`](control.md). The node model — `TreeNode`,
`TreeNodeCollection`, the Before/After pipeline, `EnsureVisible` — is the one documented in
[`treeview.md`](treeview.md), unchanged.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Nodes` | `TreeNodeCollection` | empty | The root nodes. Mutating any level of the hierarchy re-flattens and repaints. |
| `Columns` | `ObservableList<TreeListViewColumn>` | empty | The columns. Index 0 is the tree column; the rest render their `TextSelector` text. Mutating the collection — or any column's caption, width or alignment — repaints. |
| `SelectedNode` | `TreeNode?` | `null` | The selected node. Setting it scrolls into view and raises `AfterSelect`; a foreign node throws `ArgumentException`. |
| `CheckBoxes` | `bool` | `false` | Whether every node shows a themed check box in the tree column. |
| `ImageList` | `ImageList?` | `null` | The icon store for `TreeNode.ImageIndex`; `null` for no icons. |
| `ItemHeight` | `int` | theme row height | Pixel height of a row, the header and the indent per level. |
| `ShowColumnHeaders` | `bool` | `true` | Whether the column header row is shown. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible row in the flattened tree (scroll position). |
| `VisibleNodeCount` | `int` (get) | `0` | The number of rows the expanded part of the tree currently occupies. |

### Events

The same six as `TreeView`: `AfterSelect`, `BeforeExpand`/`AfterExpand` and
`BeforeCollapse`/`AfterCollapse` (the Before pair cancelable), and `AfterCheck`.

### Methods

| Method | Description |
|---|---|
| `SetDataSource<T>(roots, text, children, maxDepth = 32)` | Replaces the tree from a data source: one node per item, labeled via `text`, nested via `children` (`null` for a leaf) and carrying the item in `TreeNode.Tag`. Built eagerly, cut off after `maxDepth` levels so cyclic object graphs terminate; `maxDepth ≤ 0` throws. |

### TreeListViewColumn

A `ColumnHeader` — caption `Text`, `Width` (default 120), `TextAlign` (default `MiddleLeft`) — plus
the cell-text selector. Constructors: `()`, `(text)`, `(text, width)`,
`(text, width, textSelector)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `TextSelector` | `Func<TreeNode, string>?` | `null` | Maps a node to this column's cell text; `null` (or a `null` result) renders an empty cell. Ignored for the tree column (index 0), which renders the hierarchy and the node's own `Text`. |

## Notes

**Shared engine.** The node model talks to an internal host contract that `TreeView` and
`TreeListView` both implement, and the flattened-row virtualization (`TreeRowList`), the keyboard
model (`TreeNavigation`), the header band (`HeaderRowPainter`, the same painter `ListView` uses)
and the expand glyph are single implementations — the test suite proves the glyphs pixel-identical
to `TreeView`'s. Everything said in
[`treeview.md`](treeview.md) about the Before/After pipeline, keyboard navigation, icons,
double-click toggling and virtualization holds here unchanged; painting is bounded at 100 000 nodes
with columns active.

**Columns and cells.** Every cell clips to its column's width. The tree cell draws indent, glyph,
optional check box, optional icon and the node's `Text`; the other columns draw their selector text
in the column's `TextAlign`. The selection highlight spans the full row width across all columns.

**Hit-testing.** The glyph and check cells react only inside the tree column — where painting clips
them. A click on a deep node's glyph position that falls in a neighboring column selects the row
instead of toggling. Clicks in the header band select nothing.

**SetDataSource.** A convenience for object graphs: reflection-free like all binding in the
toolkit, it snapshots the hierarchy eagerly (children start collapsed) and replaces any existing
nodes. The depth guard bounds cycles — a self-referencing item with `maxDepth: 5` yields exactly
five levels.

**Not yet implemented** (per `docs/PRD.md` §7.4): column sorting, interactive column resize and
label editing.
