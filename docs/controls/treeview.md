# TreeView

> An owner-drawn tree painted in the native theme: hierarchical `TreeNode`s with expand/collapse

![TreeView in the NativeForms demo](../screenshots/03-lists.png)
> glyphs, connector lines, per-node icons from an `ImageList`, optional check boxes, single selection
> and a cancelable Before/After event pipeline — the expanded part of the tree is flattened into a
> row list and painting is virtualized to the visible window.

`Hawkynt.NativeForms.TreeView` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var tree = new TreeView { Bounds = new(0, 0, 300, 220) }; // 10 rows at the default 22 px row height
var root = tree.Nodes.Add("Project");
root.Nodes.Add("src").Nodes.Add("Program.cs");
root.Nodes.Add("docs");
root.Expand();

tree.AfterSelect += (_, e) => Console.WriteLine(e.Node.Text);
tree.BeforeExpand += (_, e) => e.Cancel = e.Node.Tag is "locked"; // veto keeps it collapsed

tree.CheckBoxes = true;
tree.AfterCheck += (_, e) => Console.WriteLine($"{e.Node.Text}: {e.Node.Checked}");
```

Nodes are plain data: build, nest and expand them long before (or without) a backend existing —
attaching them to a realized tree just re-flattens and repaints.

## API

Inherits the common members of [`Control`](control.md).

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Nodes` | `TreeNodeCollection` | empty | The root nodes. Mutating any level of the hierarchy re-flattens and repaints. |
| `SelectedNode` | `TreeNode?` | `null` | The selected node. Setting it runs the vetoable `BeforeSelect`, scrolls the node into view and raises `AfterSelect`; setting `null` clears silently (no events); a node attached to a different tree throws `ArgumentException`. |
| `CheckBoxes` | `bool` | `false` | Whether every node shows a themed check box. |
| `ImageList` | `ImageList?` | `null` | The icon store for `TreeNode.ImageIndex`; `null` for no icons. |
| `ItemHeight` | `int` | theme row height | Pixel height of a row — also the indent per level. |
| `ShowLines` | `bool` | `true` | Whether connector lines are drawn between nodes. |
| `ShowPlusMinus` | `bool` | `true` | Whether expand/collapse glyphs are drawn for parent nodes. |
| `ShowRootLines` | `bool` | `true` | Whether root nodes get their own glyph/line cell. When `false`, everything shifts one indent level left and roots lose their glyphs. |
| `AllowReorder` | `bool` | `false` | Whether nodes can be dragged within the tree to reorder or reparent them, with a live insertion marker. Intra-tree movement, distinct from the cross-control `Control.AllowDrop` data drag. |
| `AutoExpandDelay` | `int` | `700` | Milliseconds the pointer must dwell on a collapsed node while dragging before it auto-expands; `0` disables hover-expansion. |
| `ShowDragImage` | `bool` | `true` | Whether a drag carries a translucent image of the dragged node (icon + label) under the pointer; `false` gives a marker-only drag. |
| `TopIndex` | `int` (get) | `0` | Index of the first visible row in the flattened tree (scroll position). |
| `VisibleNodeCount` | `int` (get) | `0` | The number of rows the expanded part of the tree currently occupies. |

### Events

| Event | Description |
|---|---|
| `BeforeSelect` | Raised before the selection moves to a node; set `TreeViewCancelEventArgs.Cancel` to veto. |
| `AfterSelect` | Raised after `SelectedNode` changes to a node. |
| `BeforeExpand` | Raised before a node expands; set `Cancel` to veto. |
| `AfterExpand` | Raised after a node expanded. |
| `BeforeCollapse` | Raised before a node collapses; set `Cancel` to veto. |
| `AfterCollapse` | Raised after a node collapsed. |
| `BeforeCheck` | Raised before a node's `Checked` state flips; set `Cancel` to veto. |
| `AfterCheck` | Raised after a node's `Checked` state changed. |
| `ItemDrag` | Raised when a node starts being dragged (the pointer crossed the drag threshold). Carries the node in `Node`. |
| `NodeDragOver` | Raised continuously as the drop target changes during a drag; set `TreeNodeDragEventArgs.Cancel` to reject the current target (no marker, no drop). |
| `NodeDrop` | Raised when a dragged node is released over a valid target; set `Cancel` to abort the move. |

The Before/After/expand/collapse/check args carry the affected node in `Node`. The drag args
(`TreeNodeDragEventArgs`) carry `DraggedNode`, `TargetNode`, a `Location` of `Above` / `Onto` /
`Below`, and a `Cancel` flag.

### Methods

| Method | Description |
|---|---|
| `ExpandAll()` | Expands every node in the tree. |
| `CollapseAll()` | Collapses every node in the tree. |

### TreeNode

Constructors: `TreeNode()`, `TreeNode(string text)`.

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | The node's label. |
| `Tag` | `object?` | `null` | Arbitrary caller data. |
| `ImageIndex` | `int` | `-1` | The image-list index of the node's icon, or `-1` for none. |
| `ImageKey` | `string?` | `null` | The image-list key of the node's icon — the string alternative to `ImageIndex` (index wins when both are set). |
| `SelectedImageIndex` | `int` | `-1` | The icon used while the node is selected, or `-1` to reuse `ImageIndex`. |
| `SelectedImageKey` | `string?` | `null` | The keyed alternative to `SelectedImageIndex`. |
| `Checked` | `bool` | `false` | The check state. Changing it raises `AfterCheck` on an attached control. |
| `IsExpanded` | `bool` (get) | `false` | Whether the node's children are currently shown. |
| `Nodes` | `TreeNodeCollection` (get) | empty | The child nodes, created on first access. |
| `Parent` | `TreeNode?` (get) | `null` | The parent node; `null` for roots. |
| `TreeView` | `TreeView?` (get) | `null` | The tree this node is attached to; `null` while detached. |
| `Level` | `int` (get) | `0` | Zero-based depth: 0 for roots, parent level + 1 below. |

| Method | Description |
|---|---|
| `Expand()` | Shows the children. On an attached control this runs the cancelable Before/After pipeline; detached nodes just flip the state. |
| `ExpandAll()` | Expands the node and every descendant. |
| `Collapse()` | Hides the children (same pipeline). When the selected node vanishes under the collapsing one, the selection moves up to this node. |
| `Toggle()` | Expands a collapsed node, collapses an expanded one. |
| `EnsureVisible()` | Expands every ancestor and scrolls the attached control until this node is on screen. |
| `SetChildLoader(Func<TreeNode, IEnumerable<TreeNode>>?)` | Registers a delegate that supplies the node's children the first time it is expanded — lazy population for large or **virtual** trees (folders, archive entries, remote listings). The node paints as expandable immediately; the loader runs once. `null` clears it. |

### TreeNodeCollection

An `IReadOnlyList<TreeNode>` with `Add(string)`, `Add(TreeNode)`, `AddRange`, `Insert`, `Remove`,
`RemoveAt`, `Clear` and `IndexOf`. Adding a node that already lives in a collection throws — remove
it first. Every structural change re-parents the affected subtree and, if a control is attached,
re-flattens and repaints; removing the subtree holding the selected node clears the selection.

## Notes

**Virtualization.** The expanded part of the tree is flattened into a visible-row list, lazily:
structural changes only mark it dirty and the next access rebuilds it. Painting walks only the rows
intersecting the client area, so 100 000 root nodes paint the same handful of text operations as
ten. The wheel scrolls three rows per notch.

**Mouse.** A click on the expand glyph toggles the node without selecting it; with `CheckBoxes` on,
a click on the check cell toggles `Checked` without selecting. Clicks elsewhere on a row select it
(raising `AfterSelect`), and a second click on the same node within 500 ms toggles its expansion.

**Keyboard.** Down/Up/Home/End/PageDown/PageUp walk the visible rows. Right expands the selected
node, then a second Right enters its first child; Left collapses an expanded node, otherwise jumps
to the parent. `+`/`−` expand/collapse, `*` expands the whole subtree, Enter toggles, and Space
toggles the check when `CheckBoxes` is on (expansion otherwise). The model lives in a shared
`TreeNavigation` helper, so [`TreeListView`](treelistview.md) navigates identically.

**Icons.** With an `ImageList` set, a node with a valid `ImageIndex` paints its icon (sized to the
row height minus 4 px) between the check box and the text; `SelectedImageIndex` swaps it while the
node is selected. Nodes without an index paint no icon.

**Lines and glyphs.** Connector lines are drawn solid in the theme's disabled-text color — the
`IGraphics` seam has no dashed strokes, so the classic dotted look is approximated. Expand glyphs
appear only on nodes that actually have children.

**Drag reorder.** With `AllowReorder` on, pressing a node and dragging past a few pixels begins a
drag (`ItemDrag`). Which third of the row under the pointer it is in decides the drop: the top
quarter inserts the node as a sibling above the target, the bottom quarter below it, and the middle
half reparents it as a child. An above/below drop paints an indented insertion line with end ticks;
an onto drop outlines the whole target row. Unless `ShowDragImage` is off, a half-transparent image of
the dragged node (its icon and label) rides under the pointer as well. `NodeDragOver` can reject a target (the marker hides and
the drop is refused) and `NodeDrop` can veto the release; neither vetoing nor a drop into the node's
own subtree is ever allowed. Hovering an onto-target that is a collapsed parent for `AutoExpandDelay`
milliseconds auto-expands it. A completed drop selects the moved node and scrolls it into view.

**Not yet implemented** (per `docs/PRD.md` §7.4): label editing (waits on the text-box overlay) and
state images; multi-selection and a virtual-mode node API are further open TODOs.

## Differences from System.Windows.Forms.TreeView

- **The event args carry no `TreeViewAction`**: `TreeViewEventArgs` has only `Node`, `TreeViewCancelEventArgs` only `Node` and `Cancel` — a handler cannot tell mouse from keyboard from programmatic origin.
- **`SelectedNode = null` raises no event**: clearing the selection fires neither `BeforeSelect` nor `AfterSelect` (WinForms raises `AfterSelect` with a null node); the veto pipeline runs only when selecting an actual node.
- `BeforeSelect`/`BeforeCheck` and `ExpandAll`/`CollapseAll` exist as in WinForms (node-level folding is `Expand`/`ExpandAll`/`Collapse`/`Toggle` on `TreeNode`; there is no `TreeNode.CollapseAll`).
- No `FullPath`/`PathSeparator`, no `HideSelection`, no `Sorted`/`TreeViewNodeSorter`, no label editing yet (see above).
