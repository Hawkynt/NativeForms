# TrackBar

> A slider with a themed groove, accent-filled portion and thumb, optional tick marks controlled by `TickFrequency` and `TickStyle`, track-click paging and live thumb-drag scrubbing.

![TrackBar in the NativeForms demo](../screenshots/02-input.png)

`Hawkynt.NativeForms.TrackBar` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new TrackBar
{
    Bounds = new(20, 20, 200, 30),
    Minimum = 0,
    Maximum = 10,
    Value = 5,
    TickFrequency = 2,
    TickStyle = TickStyle.BottomRight,
};
bar.ValueChanged += (_, _) => Console.WriteLine(bar.Value); // fires live while dragging
form.Controls.Add(bar);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `LargeChange` | `int` | `5` | The step a track click or PageUp/PageDown changes the value by. Coerced to at least 1. |
| `Maximum` | `int` | `10` | The value at the end of the track. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `Minimum` | `int` | `0` | The value at the start of the track. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Orientation` | `Orientation` | `Horizontal` | The axis the track runs along. |
| `SmallChange` | `int` | `1` | The step an arrow key changes the value by. Coerced to at least 1. |
| `TickFrequency` | `int` | `1` | The value spacing between tick marks. Coerced to at least 1. |
| `TickStyle` | `TickStyle` | `BottomRight` | Where ticks are shown: none, above/left, below/right, or on both sides. |
| `Value` | `int` | `0` | The current position, clamped to [`Minimum`, `Maximum`] on assignment. |

### Events

| Name | Description |
|---|---|
| `Scroll` | Raised for **user gestures only** — thumb drag, track page, arrow key — after the value moved; programmatic `Value` writes raise no `Scroll`. |
| `ValueChanged` | Raised when `Value` changes (after clamping) — by gesture or assignment; live, once per position change, while the thumb is dragged. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Native widget promotion

On a backend that offers one — Win32 and GTK both do — a slider can realize onto a real
`msctls_trackbar32` or `GtkScale`, so the desktop draws the groove and thumb and applies its own
keyboard and scroll conventions. The public surface is identical either way; `IsNativeWidget` reports
which path was taken, and `UseNativeWidget` overrides
[`Application.PreferNativeWidgets`](application.md) per control.

A slider with `TickStyle.None` can use any native `ITrackBarPeer`. Visible ticks additionally require
the peer to implement `ITrackBarTickPeer` and confirm that it can reproduce the requested range,
frequency and side placement exactly. Win32 and GTK satisfy that capability: the common-controls
trackbar uses its `TBS_*` styles plus `TBM_SETTICFREQ`, while GTK uses explicit `GtkScale` marks. A
backend that cannot represent the requested marks falls back to the owner-drawn path rather than
silently dropping or approximating them.

Changing `TickStyle` or `TickFrequency` on a realized control re-evaluates promotion and may rebuild or
swap the peer; Win32 tick placement is a creation-time style. Changing `Orientation` likewise rebuilds
a promoted slider because the native orientation is fixed when its widget is created. In each case the
managed value and focus are preserved across the swap. See
[PRD §12](../PRD.md#12-native-peer-promotion-opt-into-real-widgets-where-the-platform-has-one).

## Notes

- Owner-drawn mode uses the platform `ITheme` (`FieldBackground` groove, `Accent` traveled portion and thumb, `Border` outlines, `ControlText` ticks), so it matches the host desktop and remains testable headlessly through the recording canvas.
- Tick placement follows `TickStyle`: `TopLeft` means above a horizontal track or left of a vertical one, `BottomRight` means below/right, and `Both` paints both sides. One logical tick is placed per `TickFrequency` step plus an exact tick at `Maximum` when the range does not divide evenly.
- **Mouse**: a left press on the thumb starts a drag that scrubs the value under the pointer; a press on the track pages by `LargeChange` toward the click, like the native control. Pressing also takes focus.
- **Keyboard** (Win32 directions): Left/Up step by −`SmallChange`, Right/Down by +`SmallChange`, PageUp/PageDown by ∓`LargeChange`, Home/End jump to `Minimum`/`Maximum`.
- **Geometry**: an 8 px margin at both ends leaves room for the 10 px owner-drawn thumb to center over the extremes.
- `TrackBarTests` pin the defaults, clamping, both key sets, track paging, live drag scrubbing, tick styles, non-divisible maximum marks and painted geometry in both orientations. `NativePeerPromotionTests` additionally pin exact-tick capability gating and state preservation when the control swaps between native and owner-drawn peers.
- Done per [docs/PRD.md](../PRD.md) §7.5; no open items.

## Differences from System.Windows.Forms.TrackBar

- `Scroll` keeps the WinForms gesture-only contract, but carries plain `EventArgs` (no `ScrollEventArgs`).
- No `SetRange` and no `AutoSize`.
