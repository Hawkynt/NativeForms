# SplitContainer

> An owner-drawn container split into two panels by a draggable bar — live relayout while the mouse
> button is held, a single `SplitterMoved` on release, minimum-size clamps on both sides and arrow-key
> nudging when focused.

`Hawkynt.NativeForms.SplitContainer` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
split.Panel1.Controls.Add(new Label { Text = "Left", Bounds = new(8, 8, 80, 20) });
split.Panel2.Controls.Add(new Label { Text = "Right", Bounds = new(8, 8, 80, 20) });
split.SplitterMoved += (_, _) => Console.WriteLine(split.SplitterDistance);
form.Controls.Add(split);
```

## API

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Panel1` | `Panel` (get) | — | The left (or top) panel; parented by the constructor. |
| `Panel2` | `Panel` (get) | — | The right (or bottom) panel; parented by the constructor. |
| `Orientation` | `Orientation` | `Vertical` | The direction of the splitter bar: `Vertical` puts the panels side by side, `Horizontal` stacks them. |
| `SplitterDistance` | `int` | `50` | Pixel size of `Panel1` along the split axis. Assignments clamp to the minimum sizes against the current control size. |
| `SplitterWidth` | `int` | `4` | Thickness of the splitter bar; at least 1. |
| `FixedPanel` | `FixedPanel` | `None` | Which panel keeps its size when the container resizes: `Panel1`, `Panel2`, or `None` (both scale proportionally). |
| `Panel1Collapsed` | `bool` | `false` | Hides `Panel1` and gives the whole area to `Panel2`; mutually exclusive with `Panel2Collapsed`. |
| `Panel2Collapsed` | `bool` | `false` | Hides `Panel2` and gives the whole area to `Panel1`. |
| `Panel1MinSize` | `int` | `25` | The smallest size `Panel1` may be squeezed to. |
| `Panel2MinSize` | `int` | `25` | The smallest size `Panel2` may be squeezed to. |

### Events

| Event | Description |
|---|---|
| `SplitterMoved` | Raised when a move is committed — on mouse release after a drag, or on each keyboard nudge that actually moved the bar. Not raised during the live drag. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Both panels are permanent real nested children with their own canvas peers; populate them through
  `Panel1.Controls` / `Panel2.Controls`. The splitter bar is the only strip of the container's own
  surface left exposed, so mouse input there drags it — layout follows the pointer live, keeping the
  grab offset, and the clamp to the minimum sizes applies throughout.
- Resizing the container re-lays the panels out at the current distance; the distance itself is
  re-clamped, so `Panel2MinSize` is honored when the container shrinks.
- Keyboard (the control is focusable, a click focuses it): with a vertical splitter Left/Right move
  the bar by 8 px, with a horizontal one Up/Down do — each nudge commits with `SplitterMoved`.
- Painted with the platform `ITheme` (`ControlBackground`, `Border`) — the bar carries a subtle
  three-dot grip; testable headlessly through the test backend's recording canvas.
- Complete per [docs/PRD.md](../PRD.md) §7.2 — no pending items.

## Differences from System.Windows.Forms.SplitContainer

- **`SplitterMoved` carries plain `EventArgs`** (WinForms: `SplitterEventArgs` with coordinates) and fires only on commit — mouse release or a keyboard nudge — never during the live drag; there is **no `SplitterMoving`** preview event.
- **No `IsSplitterFixed`** — the bar is always draggable; use `Panel1MinSize`/`Panel2MinSize` (or equal min sizes) to constrain it.
- `FixedPanel` and `Panel1Collapsed`/`Panel2Collapsed` match WinForms.
