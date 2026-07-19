# RadioButton

> An owner-drawn radio button painted in the native theme — themed ring, accent dot, themed text — with mutual exclusion among siblings in the same parent.

`Hawkynt.NativeForms.RadioButton` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var small = new RadioButton { Text = "Small", Bounds = new(20, 20, 120, 20), Checked = true };
var large = new RadioButton { Text = "Large", Bounds = new(20, 44, 120, 20) };
large.CheckedChanged += (_, _) => Console.WriteLine($"Large: {large.Checked}");
form.Controls.AddRange(small, large);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether this button is the selected one in its group. Setting it `true` unchecks its siblings; setting it invalidates the control and raises `CheckedChanged`. |
| `Image` | `IImage?` | `null` | An optional icon rendered between the ring and the caption through the shared `ContentLayout`; the text shifts right to make room. |

### Events

| Name | Description |
|---|---|
| `CheckedChanged` | Raised when `Checked` changes — including when a sibling's selection unchecks this button. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` ring fill, `Accent` dot and checked ring, `ControlText`/`DisabledText` label), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- **Grouping**: the group is the parent container — setting `Checked = true` clears `Checked` on every other `RadioButton` in `Parent.Controls` (direct siblings only, not nested containers). Put each group in its own `Panel` or `GroupBox` to separate them. Clearing `Checked` directly in code is permitted, leaving the group with no selection.
- A left mouse-button release inside the bounds selects the button; so does the Space key when focused (the control is focusable). Selection raises `CheckedChanged` and the inherited `Click`; the unchecked sibling raises only `CheckedChanged`.
- With an `Image`, the icon and text lay out via the shared `ContentLayout` (`ImageBeforeText`, middle-left) in the area right of the 14 px ring; without one, the classic text placement stays untouched.
- `RadioGroupTests` pin the exclusivity: selecting the second of two siblings unchecks the first. `RadioButtonImageTests` pin the icon placement, the text shift and the invalidation on image change.
- Done per [docs/PRD.md](../PRD.md) §7.3 (image + text via `ContentLayout` included).
