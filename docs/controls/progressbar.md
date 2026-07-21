# ProgressBar

> An owner-drawn progress bar: a themed track filled with the accent color, either in proportion to `Value` (`Blocks`) or as an animated segment sweeping the track (`Marquee`).

`Hawkynt.NativeForms.ProgressBar` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new ProgressBar { Bounds = new(20, 20, 200, 20), Minimum = 0, Maximum = 100, Value = 40 };
form.Controls.Add(bar);

bar.PerformStep();                      // Value += Step (default 10), clamped at Maximum
bar.Style = ProgressBarStyle.Marquee;   // indeterminate sweep, timer-driven
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `int` | `0` | The lowest value the bar can represent. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Maximum` | `int` | `100` | The highest value the bar can represent. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `Value` | `int` | `0` | The current progress, clamped to [`Minimum`, `Maximum`] on assignment. |
| `Style` | `ProgressBarStyle` | `Blocks` | How the bar presents progress. Switching to `Marquee` starts the animation timer; switching away stops it. |
| `MarqueeAnimationSpeed` | `int` | `100` | The marquee tick period in milliseconds (the WinForms default); 0 pauses the animation. Throws `ArgumentOutOfRangeException` when negative. |
| `Step` | `int` | `10` | The amount `PerformStep` advances `Value` by. |
| `Orientation` | `Orientation` | `Horizontal` | The axis the bar fills along. Horizontal bars fill left to right, vertical ones bottom to top. |

### Methods

| Name | Description |
|---|---|
| `PerformStep()` | Advances `Value` by `Step`, clamped at `Maximum`. |

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes (after clamping; assignments that clamp to the current value raise nothing). |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` track, `Accent` fill, `Border` outline), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- The fill is inset 1 px on every side; in `Blocks` its length is the track scaled by `(Value − Minimum) / (Maximum − Minimum)`.
- **Marquee**: an accent segment a quarter of the track long slides in from before the track and out past its end, then wraps — driven by a [`Timer`](timer.md) ticking every `MarqueeAnimationSpeed` ms. Each tick advances an integer phase and invalidates; nothing on the tick or paint path allocates (pinned by an allocation test). The timer starts on realization or style switch and stops when the style leaves `Marquee`, the speed drops to 0 or the control unrealizes.
- Purely visual — it takes no focus and handles no input.
- `ProgressBarTests` pin the defaults, `PerformStep` clamping, the bottom-up vertical fill, marquee timer start/stop, segment movement per tick, the zero-speed pause and the vertical sweep; `RadioGroupTests` pin the value clamping and proportional fill.
- Done per [docs/PRD.md](../PRD.md) §7.5; no open items.

## Differences from System.Windows.Forms.ProgressBar

- **`ValueChanged` and `Orientation` are NativeForms additions** — WinForms has neither (vertical bars there need a style hack).
- `ProgressBarStyle` offers `Blocks` and `Marquee` only; there is no `Continuous` (on modern themed Windows, `Blocks` renders continuously anyway).
- No `Increment(int)` — use `PerformStep` or assign `Value`.
