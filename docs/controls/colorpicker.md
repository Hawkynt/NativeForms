# ColorPicker

> A colour swatch that drops down a full mixer — a saturation/value square (or a hue wheel), hue and alpha bars, a hex field, new-versus-current preview, basic/custom swatch palettes, RGB/HSL/HSV/CMYK numeric tabs and an eyedropper. The face shows the current colour and a chevron; a click opens the light-dismiss mixer, and every edit sets the colour.

![ColorPicker mixer in the NativeForms demo](../screenshots/25-colorpicker-mixer.png)

`Hawkynt.NativeForms.ColorPicker` · strategy: **owner-drawn** · peer: `ICanvasPeer` (+ an `IPopupPeer` mixer)

## Usage

```csharp
var picker = new ColorPicker { Bounds = new(8, 8, 120, 26), SelectedColor = Color.RoyalBlue, AlphaEnabled = true };
picker.SelectedColorChanged += (_, _) => ApplyColor(picker.SelectedColor);
form.Controls.Add(picker);
```

Because it is an ordinary control, it drops straight into a `RibbonHostItem` or a `ToolStripControlHost`.

## API

| Member | Description |
|---|---|
| `SelectedColor` | `Color` — the chosen colour; setting it repaints and raises `SelectedColorChanged`. |
| `AlphaEnabled` | `bool` — when set, the mixer shows an alpha bar and the face draws a transparency checkerboard behind a translucent colour. Defaults to `false`. |
| `Palette` | `static IReadOnlyList<Color>` — the 40 standard swatches the mixer offers. |
| `CustomColors` | `IReadOnlyList<Color>` (get) — the user-saved custom swatches (up to sixteen, oldest dropped first). |
| `AddCustomColor(Color)` | Adds a colour to the custom palette. |
| `DroppedDown` | `bool` (get) — whether the mixer is currently open. |
| `OpenDropDown()` / `CloseDropDown()` | Open/close the mixer drop-down. |
| `SelectedColorChanged` | Raised when `SelectedColor` changes (live, on every mixer edit). |

## The mixer

- **Saturation/value square** for the current hue, with a draggable reticle; a **hue bar** (0→360) and,
  with `AlphaEnabled`, an **alpha bar** over a checkerboard. Every drag updates `SelectedColor` live.
- A **hue-wheel** toggle swaps the square for a rainbow ring with an inscribed saturation/value square —
  the ring picks the hue, the square the saturation and value.
- **New / Current** preview swatches over a checkerboard, and the **hex** value (`#RRGGBB` or `#RRGGBBAA`).
- **Basic** and **custom** swatch grids; an empty custom slot reads as a checkerboard hole.
- **RGB / HSL / HSV / CMYK** numeric tabs, each a row of draggable channel sliders with a value read-out.
- An **eyedropper** button samples the colour of any pixel on screen (the next click after arming it).
  Screen sampling is an X11/Win32 capability — Wayland forbids reading other surfaces, so it is a no-op there.

The gradients (SV square, hue bar, alpha bar, wheel) are built as cached ARGB bitmaps via
`IPlatformBackend.CreateImage` and blitted, since `IGraphics` has no gradient primitive. The mixer's
transient state lives in a single nullable field allocated only while the drop-down is open, so an
unrealized picker keeps its small footprint.

## Notes

- The drop-down is a light-dismiss `IPopupPeer` (Escape or an outside click closes it), the same
  mechanism the `ComboBox`, `DateTimePicker` and `TimePicker` use.
- Colour conversions (HSV/HSL/CMYK ↔ RGB, hex parse/format) live in `ColorMath`.
- Space or Enter opens the drop-down, so the field is keyboard-reachable; while disabled the swatch
  greys out.

## Differences from WinForms

WinForms has no inline colour-picker control (only the modal `ColorDialog`); this fills that gap with
an embeddable swatch that drops down a full mixer. The hue-wheel's inner shape is a saturation/value
square in every space (rather than an HSL triangle or CMYK disc).
