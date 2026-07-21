# Ribbon

> An Office-style ribbon: tabs across the top, each showing its groups side by side — every group a
> framed box with its caption along the bottom edge. Items come large (big icon over the caption,
> full group height) or small (three stacked per column). Groups that no longer fit collapse into a
> drop-down button; `Minimized` folds the group area away entirely.

`Hawkynt.NativeForms.Ribbon` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var ribbon = new Ribbon { Bounds = new(0, 0, 800, 140) };

var home = new RibbonTab("Home");
var clipboard = new RibbonGroup("Clipboard");
clipboard.Items.AddRange(
    new RibbonButton("Paste"),                          // large by default
    new RibbonButton("Cut", RibbonItemSize.Small),
    new RibbonButton("Copy", RibbonItemSize.Small),
    new RibbonButton("Format", RibbonItemSize.Small));  // three smalls = one column
home.Groups.Add(clipboard);

ribbon.Tabs.AddRange(home, new RibbonTab("Insert"));
form.Controls.Add(ribbon);
```

Items are `ToolStripItem`s, so commands wire up exactly as they do on a toolbar:

```csharp
var paste = new RibbonButton("Paste") { ImageList = icons, ImageIndex = pasteIcon };
paste.Command = new RelayCommand(Paste, CanPaste);   // CanExecute drives Enabled
```

A group can host a real control among its buttons:

```csharp
var styles = new RibbonGroup("Styles");
styles.Items.Add(new RibbonHostItem(styleComboBox) { HostWidth = 140 });
```

## API

### Ribbon properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Tabs` | `RibbonTabCollection` | empty | The tabs, left to right. The first tab added becomes the selected one. |
| `ImageList` | `ImageList?` | `null` | The icons the groups' and items' image indices point into. |
| `SelectedIndex` | `int` | `-1` | Index of the selected tab, `-1` while there are no tabs. Out-of-range values coerce to `-1`. |
| `SelectedTab` | `RibbonTab?` | `null` | The selected tab; setting selects by `IndexOf`. |
| `Minimized` | `bool` | `false` | Folds the group area away, leaving the tab strip. Hosted controls go with it; the tabs stay clickable. |
| `TabStripHeight` | `int` (get) | theme row height + 4 | Pixel height of the tab strip. |
| `GroupAreaHeight` | `int` (get) | remaining height | Pixel height of the group area; `0` while minimized. |

### Ribbon events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes. |
| `MinimizedChanged` | Raised after `Minimized` changes. |

### RibbonTab

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | The caption in the tab strip. |
| `Groups` | `RibbonGroupCollection` | empty | The groups shown while this tab is selected, left to right. |
| `Tag` | `object?` | `null` | Caller-owned data; the toolkit never reads it. |

Constructors: `RibbonTab()` and `RibbonTab(string text)`.

### RibbonGroup

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | The caption painted along the group's bottom edge. |
| `Items` | `ToolStripItemCollection` | empty | The items, laid out left to right in columns. |
| `ImageIndex` | `int` | `-1` | Index of the icon the collapsed drop-down button shows. |
| `IsCollapsed` | `bool` (get) | `false` | Whether the group is currently folded into its drop-down button. Recomputed on every layout pass. |
| `Bounds` | `Rectangle` (get) | empty | The group's laid-out rectangle, as of the last layout pass — empty while minimized or on an unselected tab. |
| `Tag` | `object?` | `null` | Caller-owned data. |

Constructors: `RibbonGroup()` and `RibbonGroup(string text)`.

### Items

`RibbonItem` is the abstract base, deriving from [`ToolStripItem`](toolstrip.md) — so every item
already carries `Text` (with `&` mnemonic parsing), `Image` / `ImageList` + `ImageIndex`, `Enabled`,
`Visible`, `Tag`, `Command`, the `Click` event and `PerformClick()`.

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemSize` | `RibbonItemSize` | `Large` | Whether the item takes the full group height or one of three stacked rows. |

| Type | Adds |
|---|---|
| `RibbonButton` | Nothing — a push button. Constructors: `()`, `(string text)`, `(string text, RibbonItemSize size)`. |
| `RibbonToggleButton` | `Checked` (`bool`) and `CheckedChanged`; a click flips `Checked` and the ribbon paints it held down. Same constructors. |
| `RibbonHostItem` | `Control` (the hosted control) and `HostWidth` (`int`, default `120`). Constructor: `(Control control)`; defaults to `Small`. |

### RibbonItemSize

| Value | Meaning |
|---|---|
| `Large` | A big icon above the caption, filling the group's content height — the prominent, single-column form. The default. |
| `Small` | A small icon beside the caption, one third of the content height, so three stack into one column. |

## Notes

- **Items are not controls.** They own no peer and no bounds; the ribbon lays them out and paints
  them, so a hundred buttons cost a hundred small objects rather than a hundred native widgets. A
  `RibbonButton` measures ~120 bytes.
- **`RibbonHostItem` is the exception** — it hosts a real `Control`, which the ribbon parents into
  itself, positions from the group layout, and whose peer it hides while the owning tab is
  unselected, the ribbon is minimized, or the group has collapsed. The control's own `Visible` flag
  is never clobbered.
- **Layout.** Items fill columns left to right: a large item is a column of its own; consecutive
  small items stack three to a column. A group is as wide as its columns, or as its caption when
  that is wider, plus padding.
- **Overflow.** When the groups outgrow the width, the rightmost ones collapse — one at a time,
  Office-style — into a fixed-width drop-down button that opens that group's items as a popup menu
  through the shared `MenuDropDown` engine. Widening the ribbon unfolds them again. Because the
  items are `ToolStripItem`s, no translation layer is needed to show them in a menu.
- **Keyboard** (the control is focusable): Left/Right move the tab selection without wrapping,
  Ctrl+Tab / Ctrl+Shift+Tab cycle with wraparound.
- **Measurement is cached.** Every caption width is cached and dropped only when the caption changes
  or the theme font moves; the font snapshot is held once per ribbon rather than once per item, so a
  two-hundred-button ribbon carries one font key. Nothing on the pointer path re-measures text, and
  a steady-state repaint allocates zero bytes.
- Painted with the platform `ITheme` (`ControlBackground`, `HeaderBackground`, `Accent`, `Border`,
  `HeaderText`, `ControlText`, `SelectionText`, `DisabledText`, `DefaultFont`); testable headlessly
  through the test backend's recording canvas.
- Complete per [docs/PRD.md](../PRD.md) §7.9, with two deliberate omissions noted below.

## Differences from the Office ribbon

- **No caption wrapping on large items.** Office breaks a long large-item caption over two lines;
  here it stays on one and the column widens. Keeps the paint path measurement-light.
- **No Quick Access Toolbar, no application (File) menu, no contextual tab groups, no KeyTips.**
  A `MenuStrip` above the ribbon covers the application-menu case.
- **Collapse order is right-to-left**, not priority-driven — there is no per-group priority.
- **`Minimized` is a property, not a gesture.** Double-clicking a tab does not toggle it; wire that
  to whatever affordance the app wants.
