# ContextMenuStrip

> A context menu: the same item model and owner-drawn drop-down engine as a
> [`MenuStrip`](menustrip.md) drop-down, opened at an arbitrary screen point instead of below a bar.

`Hawkynt.NativeForms.ContextMenuStrip` · strategy: **owner-drawn** (native theme) · component, no
peer of its own — one `IPopupPeer` per open cascade level

## Usage

```csharp
var menu = new ContextMenuStrip();
var copy = new ToolStripMenuItem("Copy");
copy.Click += (_, _) => CopySelection();
menu.Items.Add(copy);
menu.Items.Add(new ToolStripMenuItem("Paste"));

var panel = new Panel { Bounds = new(10, 10, 200, 150), ContextMenuStrip = menu };
form.Controls.Add(panel);

// Or open it programmatically, at a point in the control's client space:
menu.Show(panel, new(5, 6));
```

## API

| Member | Type | Description |
|---|---|---|
| `Items` | `ToolStripItemCollection` | The menu items, sharing the [`MenuStrip`](menustrip.md) item model — icons, check/radio marks, shortcut text, nested submenus, `ICommand` wiring. |
| `IsOpen` | `bool` (get) | Whether the menu is currently open. |
| `Show(Control control, Point clientLocation)` | method | Opens the menu at a position given in `control`'s client space. The control must be realized — only a live widget knows its screen position; before that the call is a no-op. |
| `Close()` | method | Closes the menu, if open. |
| `Opening` | event | Raised before the menu opens, with a settable `CancelEventArgs.Cancel` — veto to keep it closed, or populate `Items` for the context on the fly. |
| `Closed` | event | Raised after the menu (and its whole cascade) has closed, whatever caused it — commit, `Close()` or light dismissal. |

`ContextMenuStrip` is a component, not a control: it does not derive from `Control`, owns no peer
until it opens, and can serve any number of controls at once.

## Notes

- **Wiring.** Assign the menu to the inherited `Control.ContextMenuStrip` property; a right-click
  on an owner-drawn control then opens it at the cursor (the owner-drawn mouse pipeline calls
  `Show` with the click location). A left click never opens it.
- **Native-widget controls pending.** Right-click opening works on owner-drawn controls only for
  now; native-widget controls (Button, TextBox, …) need right-click events on their peers first —
  tracked in [docs/PRD.md](../PRD.md) §7.6.
- Opening, painting, hover, cascading submenus, commit-and-close and light dismissal are all the
  shared `MenuDropDown` engine — see the [`MenuStrip`](menustrip.md) page for the row anatomy and
  keyboard model. An item click commits it, closes the cascade and raises `Closed` once.
- Testable headlessly: `ContextMenuTests` pin the right-click gesture, the screen-space anchor,
  item painting, commit/close and light dismissal through the test backend's popup peer.

## Differences from System.Windows.Forms.ContextMenuStrip

- **`Opening` exists** (cancelable, as in WinForms), but there is no `Opened`/`Closing` — just `Opening` before and `Closed` after; `Closed` carries plain `EventArgs`, no close-reason.
- **No `ItemClicked`** on the strip — subscribe each item's `Click` (or wire a shared `ICommand`).
- `Show` takes a control plus a client-space point only; there is no screen-point overload, and the control must be realized.
