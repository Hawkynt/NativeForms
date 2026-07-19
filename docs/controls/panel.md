# Panel

> A simple owner-drawn container that fills itself with the theme's control background and optionally draws a border — a grouping surface for other controls.

`Hawkynt.NativeForms.Panel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var panel = new Panel { Bounds = new(10, 10, 300, 200), BorderStyle = BorderStyle.FixedSingle };
panel.Controls.Add(new Label { Text = "Inside", Bounds = new(8, 8, 120, 20) });
form.Controls.Add(panel);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `BorderStyle` | `BorderStyle` | `BorderStyle.None` | The border drawn around the panel; changing it invalidates the control. |

`BorderStyle` (enum, defined alongside `Panel`): `None` (no border), `FixedSingle` (a single flat line), `Fixed3D` (a sunken 3-D edge).

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`ControlBackground`, `Border`), so the panel matches the host desktop; testable headlessly through the test backend's recording canvas.
- `Fixed3D` currently draws only the top and left sunken edges.
- Children go into the inherited `Controls` collection; the panel itself takes no focus and handles no input.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.2): `AutoScroll`.
