# RangeSlider

> An owner-drawn two-thumb [`TrackBar`](trackbar.md): a lower and an upper value over a shared min/max, with the span between them filled in the theme accent. For media trim points, level/curve endpoints and numeric filter ranges.

![RangeSlider in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.RangeSlider` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var range = new RangeSlider
{
    Bounds = new(16, 100, 300, 26),
    Minimum = 0, Maximum = 100,
    LowerValue = 25, UpperValue = 75,
};
range.RangeChanged += (_, _) => Filter(range.LowerValue, range.UpperValue);
form.Controls.Add(range);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `LowerValue` | `int` | `Minimum` | The lower endpoint, clamped to `[Minimum, UpperValue]`. |
| `Maximum` | `int` | `100` | The upper bound. Setting it below `Minimum` pulls `Minimum` down with it, as `TrackBar` does. |
| `Minimum` | `int` | `0` | The lower bound. |
| `SmallChange` | `int` | `1` | The step an arrow key applies to the active thumb. |
| `UpperValue` | `int` | `Maximum` | The upper endpoint, clamped to `[LowerValue, Maximum]`. |

### Events

| Name | Description |
|---|---|
| `RangeChanged` | Raised when either endpoint changes, whether by drag, key or code. |

Inherits the common members of [`Control`](control.md) plus the `OwnerDrawnControl` surface.

## Notes

- A press picks the nearer thumb and drags it; the thumbs cannot cross, they clamp against each other.
- Arrow keys move the thumb last touched by `SmallChange`.
- The groove is drawn in the theme border colour, the selected span in `Accent`.

## Differences from WinForms

No `System.Windows.Forms` equivalent — WinForms ships only the single-value `TrackBar`.
