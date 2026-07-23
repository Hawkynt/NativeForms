# ToggleSwitch

> An owner-drawn on/off switch — the modern [`CheckBox`](checkbox.md) alternative: a pill-shaped track (accent-filled while on, border-grey while off) with a themed thumb that sits left for off and right for on, plus an optional caption beside it.

![ToggleSwitch in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.ToggleSwitch` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var toggle = new ToggleSwitch { Text = "Dark mode", Bounds = new(20, 20, 140, 24) };
toggle.CheckedChanged += (_, _) => ApplyTheme(toggle.Checked);
form.Controls.Add(toggle);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether the switch is on. Setting it repaints the control and raises `CheckedChanged`; assigning the current value again does not re-raise. |

### Events

| Name | Description |
|---|---|
| `CheckedChanged` | Raised when `Checked` changes — whether toggled by the user or set in code. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`). The inherited `Text` is the caption painted beside the track.

## Notes

- Painted with the platform `ITheme`: the 36×16 px track pill is `Accent` while enabled and on, `Border` grey otherwise; the thumb is a `FieldBackground` circle with a `Border` outline hugging the off (left) or on (right) end. The pill is composed from two filled ellipses plus a center rectangle — there is no rounded-rectangle primitive.
- A left mouse-button release inside the bounds toggles the switch; so does Space when focused (the control is focusable). Each user toggle raises `CheckedChanged` and the inherited `Click`. A disabled switch ignores input, keeps the grey track even when on (the thumb side alone reports the state), and greys the caption.
- The thumb snaps to its new side; there is no slide animation.
- `ToggleSwitchTests` pin the painting and behavior headlessly against the recording canvas.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.9): the slide animation.
