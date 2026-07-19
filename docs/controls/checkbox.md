# CheckBox

> An owner-drawn check box painted in the native theme — themed box, accent checkmark, themed text — that toggles on click or Space.

`Hawkynt.NativeForms.CheckBox` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var check = new CheckBox { Text = "Remember me", Bounds = new(20, 20, 160, 20) };
check.CheckedChanged += (_, _) => Console.WriteLine(check.Checked);
form.Controls.Add(check);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether the box is checked. Setting it invalidates the control and raises `CheckedChanged`. |

### Events

| Name | Description |
|---|---|
| `CheckedChanged` | Raised when `Checked` changes — whether toggled by the user or set in code. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` box, `Accent` checkmark and checked border, `ControlText`/`DisabledText` label), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- A left mouse-button release inside the bounds toggles `Checked`; so does the Space key when focused (the control is focusable). Each user toggle raises `CheckedChanged` and the inherited `Click`.
- `OwnerDrawnControlTests` pin the behavior: two clicks toggle on then off with two `CheckedChanged` events, Space toggles, and the label text is painted.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): tri-state `CheckState` and an image next to the box.
