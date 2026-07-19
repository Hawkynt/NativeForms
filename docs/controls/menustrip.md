# MenuStrip

> The application menu bar: an owner-drawn row of top-level items whose drop-downs — icons, check
> and radio marks, shortcut text, cascading submenus, light dismiss — open through the shared
> `MenuDropDown` popup engine.

`Hawkynt.NativeForms.MenuStrip` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer` +
`IPopupPeer` per open cascade level

## Usage

```csharp
var strip = new MenuStrip { Bounds = new(0, 0, 300, 24) };

var file = new ToolStripMenuItem("&File");
var open = new ToolStripMenuItem("Open") { ShortcutKeys = Keys.Control | Keys.O };
open.Click += (_, _) => OpenDocument();
file.DropDownItems.Add(open);
file.DropDownItems.Add(new ToolStripSeparator());
file.DropDownItems.Add(new ToolStripMenuItem("Exit"));

var size = new ToolStripMenuItem("&View");
size.DropDownItems.AddRange(
    new ToolStripMenuItem("Small") { CheckOnClick = true, CheckedGroup = "size", Checked = true },
    new ToolStripMenuItem("Large") { CheckOnClick = true, CheckedGroup = "size" });

strip.Items.AddRange(file, size);
form.Controls.Add(strip);
```

## API

### MenuStrip

| Member | Type | Description |
|---|---|---|
| `Items` | `ToolStripItemCollection` | The top-level items. Mutating the collection (or any item in it) repaints the bar. |
| `OpenIndex` | `int` (get) | Index of the top-level item whose drop-down is open, `-1` for none. |
| `OpenDropDown(int index)` | method | Opens the drop-down of the item at `index` below the bar. A no-op for out-of-range indexes, disabled items, items without children, or before realization. |
| `CloseDropDown()` | method | Closes the open drop-down cascade, if any. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`). The bar is focusable.

### ToolStripItem (the shared item model)

`ToolStripItem` is the abstract base of everything a strip hosts — menu items, toolbar buttons,
separators, status labels. An item is **not** a `Control`: it owns no peer and no bounds; the
hosting strip (or the drop-down engine) lays it out and paints it, and state changes bubble through
the owning collection into a repaint.

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | The caption. `&` marks the following character as the mnemonic; `&&` escapes a literal ampersand. |
| `Image` | `IImage?` | `null` | Direct icon; wins over `ImageList` + `ImageIndex`. |
| `ImageList` | `ImageList?` | `null` | Icon store that `ImageIndex` indexes into. |
| `ImageIndex` | `int` | `-1` | Index of the icon within `ImageList`; negative for none. |
| `Enabled` | `bool` | `true` | Whether the item reacts to the user. With a `Command` attached, its `CanExecute` gates the effective value on top of the assigned one. |
| `Visible` | `bool` | `true` | Whether the item occupies space and paints. |
| `Command` | `ICommand?` | `null` | MVVM command: `PerformClick` executes it, `CanExecuteChanged` re-evaluates `Enabled` and repaints the hosting strip. |
| `Tag` | `object?` | `null` | Arbitrary caller data. |
| `Click` | event | | Raised when the item is activated (click, Enter, shortcut). |
| `PerformClick()` | method | | Activates as a user click would: raises `Click`, then executes `Command` when it can. A no-op while not `Enabled`. |

### ToolStripMenuItem

Adds the menu-specific surface on top of `ToolStripDropDownItem` (below):

| Member | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether the check (or radio) mark shows. Checking a `CheckedGroup` member unchecks its sibling group members. |
| `CheckOnClick` | `bool` | `false` | Whether clicking toggles `Checked` automatically. In a `CheckedGroup`, clicking always checks (radio semantics — the checked member stays checked). |
| `CheckedGroup` | `string?` | `null` | The radio group among siblings, or `null` for an ordinary check item. Group members are mutually exclusive and paint a bullet instead of a check mark. |
| `ShortcutKeys` | `Keys` | `None` | The chord that activates the item, e.g. `Keys.Control \| Keys.S`. Dispatched by the owning `MenuStrip`. |
| `ShortcutKeyDisplayString` | `string?` | `null` | Overrides the shortcut text shown right-aligned in the drop-down; `null` renders the formatted chord (`"Ctrl+Shift+S"`, digits without the `D` prefix). |
| `CheckedChanged` | event | | Raised after `Checked` changes — including a group sibling being turned off. |

Constructors: `ToolStripMenuItem()`, `ToolStripMenuItem(string text)`.

### ToolStripDropDownItem

The shared base of `ToolStripMenuItem`, `ToolStripDropDownButton` and `ToolStripSplitButton`,
mirroring the Windows Forms hierarchy:

| Member | Type | Description |
|---|---|---|
| `DropDownItems` | `ToolStripItemCollection` | Child items shown when the drop-down opens. Lazily created; changes bubble through the item so the strip repaints however deep they happen. |
| `HasDropDownItems` | `bool` (get) | Whether a drop-down would show anything, without materializing an empty collection. |

### ToolStripItemCollection

`IReadOnlyList<ToolStripItem>` with `Add`, `AddRange(params ToolStripItem[])` (single change
notification), `Insert`, `Remove`, `RemoveAt`, `Clear`, `IndexOf`, `Count` and an indexer. The
collection never touches a surface itself — the same item tree serves menu bars, drop-downs,
toolbars and status bars.

### ToolStripSeparator

A thin dividing line between item groups. Purely visual: no clicks, no keyboard hover; keyboard
navigation skips it.

## Keyboard

While the bar has focus: Left/Right move the hover between top-level items (wrapping, skipping
invisible ones), Enter or Down opens the hovered menu, and a bare mnemonic letter opens the matching
top-level menu. While a drop-down is open: Up/Down move the hover row (skipping separators),
Enter commits or descends, Right descends into a submenu, Left closes one level, Escape walks the
cascade closed one level at a time, a mnemonic letter activates the matching row — and Left/Right
that fall through the cascade switch the open top-level menu live. Sliding the mouse along the bar
while a menu is open switches menus live as well, the classic menu gesture.

## Shortcuts

`ProcessShortcut(Keys)` (internal) dispatches a chord depth-first to the first enabled, visible
`ToolStripMenuItem` registered for it anywhere in the item tree and returns whether one fired;
disabled items are skipped, not swallowed. It runs from the bar's own key pipeline, so chords fire
while the bar has focus and no drop-down is open (an open cascade routes keys to the menu
navigation instead). Form-wide dispatch and Alt bar activation need the toolkit's focus/key-preview
model — tracked in [docs/PRD.md](../PRD.md) §7.1/§7.6.

## Notes

- **Owner-drawn via popups, by design.** One `MenuDropDown` engine drives menu-bar drop-downs,
  context menus, tool-strip drop-down/split buttons and the overflow chevron, so a menu looks and
  behaves identically wherever it pops up. Each cascade level is one `IPopupPeer` painting rows with
  an icon/check column, mnemonic-underlined text, right-aligned shortcut text, a submenu arrow and
  separator lines; submenus anchor right of their parent row. The native menu bar mapping (Win32
  `HMENU`, `GtkMenuBar`, `NSMenu`) is a tracked follow-up ([docs/PRD.md](../PRD.md) §7.6).
- Mnemonics are parsed once per `Text` assignment into cached display/underline metadata — the
  paint path never re-parses or allocates. Shortcut text is likewise formatted once and cached.
- Committing a click closes the whole cascade and raises `Click` once; clicking a disabled row does
  nothing and keeps the menu open. Light dismissal of any level closes the cascade and resets
  `OpenIndex`.
- Testable headlessly: `MenuStripTests` drive the bar and its popups through the test backend's
  recording canvas and popup peers — geometry, marks, hover fills, keyboard and shortcut dispatch.
