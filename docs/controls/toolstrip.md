# ToolStrip

> An owner-drawn toolbar: icon+text buttons with hover/pressed/toggled states, separators, and
> drop-down/split buttons whose menus open through the shared `MenuDropDown` engine — trailing items
> that no longer fit collapse behind an overflow chevron.

`Hawkynt.NativeForms.ToolStrip` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer` +
`IPopupPeer` per open drop-down

## Usage

```csharp
var strip = new ToolStrip { Bounds = new(0, 0, 300, 28) };

var run = new ToolStripButton("Run") { Image = runIcon, Command = viewModel.RunCommand };
var wrap = new ToolStripButton("Wrap") { CheckOnClick = true };

var open = new ToolStripSplitButton("Open");           // main zone clicks, arrow zone drops down
open.Click += (_, _) => OpenLast();
open.DropDownItems.Add(new ToolStripMenuItem("A.txt"));

strip.Items.AddRange(run, new ToolStripSeparator(), wrap, open);
form.Controls.Add(strip);
```

## API

### ToolStrip

| Member | Type | Description |
|---|---|---|
| `Items` | `ToolStripItemCollection` | The toolbar items. Mutating the collection (or any item in it) repaints the bar. |
| `HasOverflow` | `bool` (get) | Whether the bar currently needs the overflow chevron. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`). Items share the `ToolStripItem` model documented on
the [`MenuStrip`](menustrip.md) page: `Text` (with mnemonics), `Image`/`ImageList`/`ImageIndex`,
`Enabled`, `Visible`, `Command`, `Tag`, `Click`, `PerformClick()`.

### ToolStripButton

| Member | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether the button is latched down (toggle state); painted as a themed fill with an accent frame. |
| `CheckOnClick` | `bool` | `false` | Whether clicking toggles `Checked` automatically. |
| `CheckedChanged` | event | | Raised after `Checked` changes. |

Constructors: `ToolStripButton()`, `ToolStripButton(string text)`.

### ToolStripDropDownButton

A button whose whole surface opens a drop-down of its `DropDownItems` (inherited from
`ToolStripDropDownItem`); the trailing arrow zone just makes the affordance visible. Constructors:
`ToolStripDropDownButton()`, `ToolStripDropDownButton(string text)`.

### ToolStripSplitButton

A two-zone button: the main zone clicks like a plain `ToolStripButton` (raising `Click` and running
the attached command), while the separate 12 px arrow zone — marked off by a separator line — opens
the `DropDownItems` drop-down. Constructors: `ToolStripSplitButton()`,
`ToolStripSplitButton(string text)`.

### ToolStripSeparator

A vertical dividing line, 7 px wide. Takes no clicks and no hover.

## Notes

- **Click contract.** A press arms the item and paints the pressed fill; the click commits on mouse
  up **over the same item only** — releasing elsewhere cancels. Hover and pressed states use
  distinct theme fills; disabled items paint greyed text and never arm.
- **Command gating.** A `Command` whose `CanExecute` returns false disables the button (greyed,
  never arms); `CanExecuteChanged` re-evaluates and repaints — the same wiring as menu items and a
  bound `Button`.
- **Overflow.** Items overflow as a suffix: layout stops at the first visible item whose right edge
  would cross into the 16 px chevron zone. Clicking the chevron opens every overflowed item as a
  popup menu through the shared engine, right-aligned under the chevron; committing an overflowed
  item clicks it and closes the popup. When everything fits, no chevron is painted.
- **Drop-downs.** Drop-down and split buttons open their menu below the bar, left-aligned with the
  item, through the same `MenuDropDown` engine as [`MenuStrip`](menustrip.md) — identical row
  anatomy, cascading and light dismissal.
- Buttons are laid out as padding + 16 px icon + caption (+ 12 px arrow zone for drop-down/split
  buttons); an icon comes from `Image` or `ImageList` + `ImageIndex`.
- Testable headlessly: `ToolStripTests` pin painting, hover/pressed fills, the commit contract,
  toggling, command gating, both split-button zones, and the chevron geometry and popup.
- The standalone control-sized siblings are documented in [`SplitButton`](splitbutton.md).
