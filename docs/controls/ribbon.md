# Ribbon

> An Office-style ribbon: tabs across the top, each showing its groups side by side — every group a
> framed box with its caption along the bottom edge. Items come large (big icon over the caption,
> full group height) or small (three stacked per column). Groups that no longer fit collapse into a
> drop-down button; `Minimized` collapses the ribbon onto its tab strip and a tab click then floats
> that tab's groups as a transient flyout. A `RibbonGridButton` opens an Office-style table picker.

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

A `RibbonGridButton` opens an Office-style table-size [`GridPicker`](gridpicker.md) under itself:

```csharp
var table = new RibbonGridButton("Table") { MaxColumns = 10, MaxRows = 8 };
table.RangeSelected += (_, e) => InsertTable(e.Rows, e.Columns);
tables.Items.Add(table);
```

The ribbon has no automatic layout owner, so re-flow the content below when it minimizes:

```csharp
ribbon.PreferredHeightChanged += (_, _) => LayoutContentBelow(ribbon.Bottom);
```

## API

### Ribbon properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Tabs` | `RibbonTabCollection` | empty | The tabs, left to right. The first tab added becomes the selected one. |
| `ImageList` | `ImageList?` | `null` | The icons the groups' and items' image indices point into. |
| `SelectedIndex` | `int` | `-1` | Index of the selected tab, `-1` while there are no tabs. Out-of-range values coerce to `-1`. |
| `SelectedTab` | `RibbonTab?` | `null` | The selected tab; setting selects by `IndexOf`. |
| `Minimized` | `bool` | `false` | Collapses the ribbon onto its tab strip — the control shrinks its own `Height` to `TabStripHeight` (remembering the expanded height to restore) so a plain container re-flows the content below. Hosted controls go with it; the tabs stay clickable and open a flyout. |
| `TabStripHeight` | `int` (get) | theme row height + 4 | Pixel height of the tab strip. |
| `GroupAreaHeight` | `int` (get) | remaining height | Pixel height of the group area; `0` while minimized. |
| `PreferredHeight` | `int` (get) | `Height` | The height the ribbon wants: `TabStripHeight` while minimized, else the strip plus a full group area. Minimizing already shrinks the control to it. |

### Ribbon events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes. |
| `MinimizedChanged` | Raised after `Minimized` changes. |
| `PreferredHeightChanged` | Raised after `PreferredHeight` changes because the ribbon was minimized or restored, so a host can re-flow the content below it. |

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
| `RibbonGridButton` | `MaxColumns` (`int`, default `10`), `MaxRows` (`int`, default `8`) and `RangeSelected` (`EventHandler<GridRangeEventArgs>`). A click opens a [`GridPicker`](gridpicker.md) in a popup under the button instead of firing a plain click; `RangeSelected` reports the chosen `Rows`×`Columns`. Same constructors as `RibbonButton`. |

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
- **Minimize.** `Minimized` collapses the ribbon onto its tab strip: the control shrinks its own
  `Height` to `TabStripHeight`, remembers the height to restore, and raises `PreferredHeightChanged`
  so a plain container can lift the content below (there is no automatic layout owner). Restoring
  grows it back. Double-clicking a tab toggles `Minimized`.
- **Tab-click flyout.** While minimized, clicking a tab floats that tab's groups as a transient
  flyout — a light-dismiss popup (the same `IPopupPeer` engine the menus and drop-downs use)
  anchored directly under the strip, full ribbon width, painting exactly the group area the expanded
  ribbon would. It dismisses on an outside click, on Escape, and once an item inside it is activated;
  selecting a different tab swaps it. Hosted controls are not re-parented into the flyout, so only
  their item glyphs would show — the flyout is a button surface.
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
- **No auto-layout owner.** Minimizing changes the ribbon's own height and raises
  `PreferredHeightChanged`; a host re-flows the content below off that, rather than the ribbon
  pushing a docked layout the way the WinForms `ToolStripContainer` does.
