# ColorPicker

> A colour swatch that drops down a palette of standard colours — the toolbar/ribbon/dialog colour chooser. The face shows the current colour and a chevron; a click opens a light-dismiss grid, and choosing a swatch sets the colour.

`Hawkynt.NativeForms.ColorPicker` · strategy: **owner-drawn** · peer: `ICanvasPeer` (+ an `IPopupPeer` palette)

## Usage

```csharp
var picker = new ColorPicker { Bounds = new(8, 8, 120, 26), SelectedColor = Color.RoyalBlue };
picker.SelectedColorChanged += (_, _) => ApplyColor(picker.SelectedColor);
form.Controls.Add(picker);
```

Because it is an ordinary control, it drops straight into a `RibbonHostItem` or a `ToolStripControlHost`.

## API

| Member | Description |
|---|---|
| `SelectedColor` | `Color` — the chosen colour; setting it repaints and raises `SelectedColorChanged`. |
| `Palette` | `static IReadOnlyList<Color>` — the 40 standard swatches the drop-down offers. |
| `DroppedDown` | `bool` (get) — whether the palette is currently open. |
| `OpenDropDown()` / `CloseDropDown()` | Open/close the palette drop-down. |
| `SelectedColorChanged` | Raised when `SelectedColor` changes. |

## Notes

- The palette is an 8-column grid of standard colours (greys, then hue rows). The hovered swatch is
  outlined in the accent colour; clicking one commits and closes the drop-down.
- The drop-down is a light-dismiss `IPopupPeer` (Escape or an outside click closes it), the same
  mechanism the `ComboBox`, `DateTimePicker` and `TimePicker` use.
- Space or Enter opens the drop-down, so the field is keyboard-reachable; while disabled the swatch
  greys out.

## Differences from WinForms

WinForms has no inline colour-picker control (only the modal `ColorDialog`); this fills that gap with
an embeddable swatch + palette, and does not (yet) offer a custom "more colours" mixer.
