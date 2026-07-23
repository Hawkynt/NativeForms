# TrackBar

> An owner-drawn slider: a themed groove with an accent-filled portion and thumb, tick marks every `TickFrequency` values, track-click paging and live thumb-drag scrubbing.

![TrackBar in the NativeForms demo](../screenshots/02-input.png)

`Hawkynt.NativeForms.TrackBar` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new TrackBar { Bounds = new(20, 20, 200, 30), Minimum = 0, Maximum = 10, Value = 5 };
bar.ValueChanged += (_, _) => Console.WriteLine(bar.Value); // fires live while dragging
form.Controls.Add(bar);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `int` | `0` | The value at the start of the track. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Maximum` | `int` | `10` | The value at the end of the track. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `Value` | `int` | `0` | The current position, clamped to [`Minimum`, `Maximum`] on assignment. |
| `SmallChange` | `int` | `1` | The step an arrow key changes the value by. Coerced to at least 1. |
| `LargeChange` | `int` | `5` | The step a track click or PageUp/PageDown changes the value by. Coerced to at least 1. |
| `TickFrequency` | `int` | `1` | The value spacing between painted tick marks. Coerced to at least 1. |
| `Orientation` | `Orientation` | `Horizontal` | The axis the track runs along. |

### Events

| Name | Description |
|---|---|
| `Scroll` | Raised for **user gestures only** — thumb drag, track page, arrow key — after the value moved; programmatic `Value` writes raise no `Scroll`. |
| `ValueChanged` | Raised when `Value` changes (after clamping) — by gesture or assignment; live, once per position change, while the thumb is dragged. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` groove, `Accent` traveled portion and thumb, `Border` outlines, `ControlText` ticks), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- **Mouse**: a left press on the thumb starts a drag that scrubs the value under the pointer; a press on the track pages by `LargeChange` toward the click, like the native control. Pressing also takes focus.
- **Keyboard** (Win32 directions): Left/Up step by −`SmallChange`, Right/Down by +`SmallChange`, PageUp/PageDown by ∓`LargeChange`, Home/End jump to `Minimum`/`Maximum`.
- **Geometry**: an 8 px margin at both ends leaves room for the 10 px thumb to center over the extremes; one tick paints per `TickFrequency` step plus one at `Maximum` when the range does not divide evenly.
- `TrackBarTests` pin the defaults, the clamping, both key sets, track paging, live drag scrubbing (one `ValueChanged` per change, none after release) and the painted groove/fill/thumb/tick geometry in both orientations.
- Done per [docs/PRD.md](../PRD.md) §7.5; no open items.

## Differences from System.Windows.Forms.TrackBar

- `Scroll` exists and keeps the WinForms gesture-only contract, but carries plain `EventArgs` (no `ScrollEventArgs`).
- No `TickStyle` (ticks always paint below/right of the groove), no `SetRange`, no `AutoSize`.
