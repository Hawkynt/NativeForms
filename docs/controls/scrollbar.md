# HScrollBar / VScrollBar

> Owner-drawn standalone scrollbars: themed arrows with press-and-hold autorepeat, a thumb sized proportionally to `LargeChange` over the range, channel-click paging and thumb-drag scrubbing — with the Win32 value-range semantics.

`Hawkynt.NativeForms.HScrollBar` / `VScrollBar` (base: `ScrollBar`) · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new VScrollBar { Bounds = new(300, 20, 16, 132), Maximum = 99, LargeChange = 20 };
bar.Scroll += (_, e) => Console.WriteLine($"{e.Type}: {e.NewValue}"); // user gestures only
bar.ValueChanged += (_, _) => Console.WriteLine(bar.Value);          // every change
form.Controls.Add(bar);
```

## API

`HScrollBar` and `VScrollBar` only fix the axis; every member lives on the abstract `ScrollBar` base.

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `int` | `0` | The value at the start of the track. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Maximum` | `int` | `100` | The value at the end of the track. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `SmallChange` | `int` | `1` | The step an arrow click (and each autorepeat tick) scrolls by. Coerced to at least 1. |
| `LargeChange` | `int` | `10` | The page a channel click scrolls by; also the thumb's share of the range. Coerced to at least 1; changing it re-clamps `Value`. |
| `Value` | `int` | `0` | The current scroll position, clamped to [`Minimum`, `Maximum − LargeChange + 1`]. |

### Events

| Name | Description |
|---|---|
| `Scroll` | Raised for **user gestures only**, carrying a `ScrollEventArgs` with the gesture `Type` (`SmallDecrement`/`SmallIncrement` for arrows, `LargeDecrement`/`LargeIncrement` for channel pages, `ThumbTrack` per drag step, `EndScroll` on release) and the `NewValue`. |
| `ValueChanged` | Raised for **every** value change, by user gesture or assignment. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Win32 range semantics**: like its namesake, the highest value the user can scroll to is `Maximum − LargeChange + 1`, not `Maximum` — `Value` assignments clamp to that scrollable range too. With the defaults (`Maximum` 100, `LargeChange` 10) the reachable maximum is 91.
- **Scroll vs ValueChanged**: `Scroll` describes the gesture (it fires `EndScroll` even though the value did not move); `ValueChanged` reports the state. A programmatic `Value = 30` raises `ValueChanged` and no `Scroll`. Both fire only when the clamped value actually moved (except `EndScroll`), and the `Scroll` args allocate nothing when nobody listens.
- **Thumb**: sized `track × LargeChange / (Maximum − Minimum)` pixels — the visible-page share of the range. Dragging scrubs with one `ThumbTrack` per position change; release raises `EndScroll`.
- **Arrows**: a press steps once by `SmallChange` and arms an autorepeat (500 ms initial delay, then every 50 ms via the shared `AutoRepeat` engine); release or leaving the control stops it.
- Geometry and painting live in the internal `ScrollBarRenderer`, shared with future scrolling hosts; colors come from the platform `ITheme`.
- `ScrollBarTests` pin the defaults, the `Maximum − LargeChange + 1` clamp, the proportional thumb, arrow autorepeat timing, channel paging, drag scrubbing with `EndScroll`, the horizontal layout and the programmatic-`Value` event split.
- Done per [docs/PRD.md](../PRD.md) §7.5; unifying this renderer with the `Panel.AutoScroll` one is a tracked follow-up there.
