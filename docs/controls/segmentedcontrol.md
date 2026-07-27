# SegmentedControl

> An owner-drawn horizontal group of mutually-exclusive toggle segments — the button-styled radio group, or iOS-style picker: one rounded, bordered strip split into equal cells with the selected cell filled in the theme accent.

![SegmentedControl in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.SegmentedControl` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var view = new SegmentedControl { Bounds = new(16, 36, 300, 28) };
view.SetSegments("Day", "Week", "Month");
view.SelectedIndexChanged += (_, _) => Reschedule(view.SelectedSegment);
form.Controls.Add(view);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Segments` | `IReadOnlyList<string>` | empty | The segment captions, left to right. |
| `SelectedIndex` | `int` | `-1` (`0` once segments exist) | The selected segment. Clamped to the segment range; setting it repaints and raises `SelectedIndexChanged`. |
| `SelectedSegment` | `string?` | `null` | The caption of the selected segment, or `null` when there are none. |

### Methods

| Name | Description |
|---|---|
| `SetSegments(params string[] labels)` | Replaces the captions; the selection lands on the first segment. |

### Events

| Name | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes, whether by click, arrow key or code. |

Inherits the common members of [`Control`](control.md) plus the `OwnerDrawnControl` surface.

## Notes

- A left click selects the segment under the pointer; Left/Right arrows move the selection while focused.
- The selected cell rounds only the corners that sit on the strip's outer edge, so the accent fill never overhangs the rounded border.
- A disabled control paints the selected cell in the border grey and every caption in the disabled text colour.

## Differences from WinForms

No `System.Windows.Forms` equivalent — the classic composition is a `Panel` of `RadioButton`s with `Appearance.Button`. This is one control with an index-based API instead.
