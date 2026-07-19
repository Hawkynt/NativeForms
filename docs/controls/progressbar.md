# ProgressBar

> An owner-drawn, determinate progress bar: a themed track with an accent-filled portion sized in proportion to `Value` within [`Minimum`, `Maximum`].

`Hawkynt.NativeForms.ProgressBar` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new ProgressBar { Bounds = new(20, 20, 200, 20), Minimum = 0, Maximum = 100, Value = 40 };
form.Controls.Add(bar);

bar.Value += 10; // clamped to [Minimum, Maximum], repaints, raises ValueChanged
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `int` | `0` | The lowest value the bar can represent. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Maximum` | `int` | `100` | The highest value the bar can represent. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `Value` | `int` | `0` | The current progress, clamped to [`Minimum`, `Maximum`] on assignment. |

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes (after clamping; assignments that clamp to the current value raise nothing). |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` track, `Accent` fill, `Border` outline), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- The fill is inset 1 px on every side; its width is the track width scaled by `(Value - Minimum) / (Maximum - Minimum)`. `RadioGroupTests` pin both the clamping (200 → 100, −50 → 0) and the proportional fill.
- Purely visual — it takes no focus and handles no input.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.5): `Style.Marquee` (animated, allocation-free), `Step`/`PerformStep`, and vertical orientation.
