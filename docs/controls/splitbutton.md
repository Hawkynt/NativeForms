# SplitButton & DropDownButton

> Standalone menu buttons, the control-sized siblings of `ToolStripSplitButton` and

![SplitButton & DropDownButton in the NativeForms demo](../screenshots/20-toolbars.png)
> `ToolStripDropDownButton`: a themed button face with icon+text content, a trailing arrow zone, and
> a drop-down of menu items opened below the control through the shared `MenuDropDown` engine.

`Hawkynt.NativeForms.SplitButton` / `Hawkynt.NativeForms.DropDownButton` · strategy: **owner-drawn**
(native theme) · peer: `ICanvasPeer` + `IPopupPeer` per open cascade level · shared base:
`DropDownButtonBase`

## Usage

```csharp
// SplitButton: the main zone acts, the arrow zone offers variants.
var save = new SplitButton { Text = "Save", Bounds = new(20, 20, 100, 28), Command = vm.SaveCommand };
save.Click += (_, _) => vm.Save();
save.DropDownItems.Add(new ToolStripMenuItem("Save As…"));

// DropDownButton: the whole surface opens the menu.
var open = new DropDownButton { Text = "Open", Bounds = new(20, 60, 100, 28) };
open.DropDownItems.Add(new ToolStripMenuItem("Recent"));
form.Controls.Add(save);
form.Controls.Add(open);
```

## API

### DropDownButtonBase (shared)

| Member | Type | Description |
|---|---|---|
| `DropDownItems` | `ToolStripItemCollection` | The items shown when the drop-down opens, sharing the strip/menu item model of [`MenuStrip`](menustrip.md). Lazily created. |
| `HasDropDownItems` | `bool` (get) | Whether a drop-down would show anything, without materializing an empty collection. |
| `IsDropDownOpen` | `bool` (get) | Whether the drop-down cascade is currently open. |
| `Image` | `IImage?` | Optional icon rendered before the caption through the shared content layout. |
| `ShowDropDown()` | method | Opens the drop-down below the control, left-aligned with it. A no-op before realization or while `DropDownItems` is empty. |
| `CloseDropDown()` | method | Closes the drop-down cascade, if open. |

Both controls inherit the common members of [`Control`](control.md), plus the owner-drawn surface
of `OwnerDrawnControl` (`Invalidate`, `Focus`), and are focusable. Down (plain or with Alt) opens
the menu on both.

### SplitButton

| Member | Type | Description |
|---|---|---|
| `Command` | `ICommand?` | The MVVM command the main zone invokes. Its `CanExecute` gates the main action at click time — a view-model guard silently swallows the click — while the arrow zone and the drop-down stay available. |
| `PerformMainClick()` | method | Runs the main action as a main-zone click would: raises `Click` and executes `Command`. A no-op while disabled or while the command declines. |

The face is split by a separator line ahead of the 12 px arrow zone. A left click in the main zone
(committing on mouse up inside it) runs the main action without opening the menu; a click in the
arrow zone opens the drop-down without running the main action. Enter and Space run the main
action; Down opens the menu.

### DropDownButton

Adds no members of its own: any left click on the surface — main zone or arrow zone — opens the
drop-down, and Down, Enter and Space open it as well. The arrow zone (no separator) just makes the
affordance visible. Item enablement, not the button, gates the actions: a disabled menu item never
commits and leaves the cascade open.

## Notes

- The drop-down is the same `MenuDropDown` popup engine a [`MenuStrip`](menustrip.md) or
  [`ContextMenuStrip`](contextmenustrip.md) uses — identical rows, marks, cascading submenus and
  light dismissal. Committing an item closes the cascade and raises the item's `Click`.
- The face is painted through the shared button-face renderer with theme colors; icon and caption
  center in the content zone (everything left of the arrow) via `ContentLayout`, image before text.
- Disabled buttons ignore all mouse and keyboard input and never create a popup.
- Testable headlessly: `SplitButtonTests` and `DropDownButtonTests` pin the face painting, both
  zones, command gating, the keyboard split and the disabled behavior.
- The in-toolbar variants (`ToolStripSplitButton`, `ToolStripDropDownButton`) are documented on the
  [`ToolStrip`](toolstrip.md) page.
