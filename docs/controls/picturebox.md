# PictureBox

> An owner-drawn image surface: one `IImage` fitted per `SizeMode` — top-left at native size, stretched, centered, or aspect-fit zoomed — clipped to the client area, with an optional themed border.

`Hawkynt.NativeForms.PictureBox` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var box = new PictureBox
{
    Bounds = new(20, 20, 100, 80),
    Image = image,                      // any IImage, e.g. from ImageList.GetImage
    SizeMode = PictureBoxSizeMode.Zoom,
    BorderStyle = BorderStyle.FixedSingle,
};
form.Controls.Add(box);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Image` | `IImage?` | `null` | The static image to display, or `null` for background (and border) only. |
| `AnimatedImage` | `AnimatedImage?` | `null` | A decoded still or animated image; when animated, the shared animation clock repaints the box as the frame advances. Takes precedence over `Image`. A hidden box is not repainted but shows the correct frame when shown again (the frame is a function of elapsed time). **Disabling** the box freezes the animation on its current frame and renders it **grayscale**; re-enabling resumes exactly where it stopped (the paused span is excluded from the clock). |
| `SizeMode` | `PictureBoxSizeMode` | `Normal` | How the image is fitted into the client area (see the geometries below). |
| `BorderStyle` | `BorderStyle` | `None` | `None` or `FixedSingle` — a single line in the theme's border color. |

Every setter invalidates on change. There are no own events.

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **SizeMode geometries** (client `C`, image `I`):
  - `Normal` — `I` at native size in the top-left corner, clipped if larger.
  - `StretchImage` — stretched to fill `C`, ignoring the aspect ratio.
  - `CenterImage` — native size at `((C.w − I.w)/2, (C.h − I.h)/2)`, clipped if larger.
  - `Zoom` — scaled to the largest size that fits, keeping the aspect ratio, centered; a wide image letterboxes, a tall one pillarboxes. The scale is computed with a cross product in integer math, so there are no fractions.
- The image always draws inside a pushed clip of the client rectangle, so oversized `Normal`/`CenterImage` content cannot bleed over neighbors; the clip is popped before the border paints.
- The background fills with the theme's `ControlBackground`; an image whose `Width` or `Height` is 0 is treated as absent.
- Purely visual — it takes no focus and handles no input.
- `PictureBoxTests` pin each geometry with exact destination rectangles (e.g. a 40×30 image in a 100×80 box centers at 30,25; a 50×25 image zooms to 0,15,100,50), the clip bracket around the draw, the `FixedSingle` frame and one invalidation per property change.
- Done per [docs/PRD.md](../PRD.md) §7.7; no open items.

## Differences from System.Windows.Forms.PictureBox

- **`PictureBoxSizeMode` has no `AutoSize`** — the box never resizes itself to the image — and the
  remaining members therefore sit at different ordinals (`CenterImage` = 2, `Zoom` = 3 versus
  WinForms' 3 and 4). Match by name, never by persisted numeric value.
- `Image` is an `IImage` (the toolkit's decoder-free image seam), not a `System.Drawing.Image`;
  there is no `ImageLocation`/`Load`/`LoadAsync` and no `ErrorImage`/`InitialImage`.
- The control is purely visual: no focus, no click events of its own.
