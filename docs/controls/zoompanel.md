# ZoomPanel

> An owner-drawn zoomable, pannable viewport: it scales and offsets an image (and/or host-drawn content), zooming about the cursor on the wheel and panning on a drag, with optional rulers, a pixel grid and a bottom-right zoom slider. The working surface for an image editor and a document/media viewport — [`PictureBox`](picturebox.md) only displays, and `Panel.AutoScroll` only scrolls.

![ZoomPanel in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.ZoomPanel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var canvas = new ZoomPanel { Bounds = new(0, 0, 440, 300), ShowRulers = true, GridSize = 16 };
canvas.Image = backend.CreateImage(w, h, pixels);
canvas.ZoomChanged += (_, _) => status.Text = $"{canvas.Zoom * 100:F0}%";

// Draw your own content in the same space:
canvas.PaintContent += (_, e) =>
{
    var p = e.ToView(new PointF(10, 10));   // content -> view mapping
    e.Graphics.FillRectangle(Color.Red, new Rectangle((int)p.X, (int)p.Y, 4, 4));
};
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `ContentSize` | `Size` | `0×0` | The content's own pixel size. Set it explicitly when there is no `Image` but a `PaintContent` host draws. |
| `GridColor` | `Color` | theme grid line | The overlay grid's line colour. |
| `GridSize` | `int` | `0` | Content-space spacing of an overlaid grid; `0` draws none. Suppressed automatically once a cell would be under ~4 device pixels. |
| `Image` | `IImage?` | `null` | The displayed image. Setting it adopts its size as `ContentSize` and fits it to the window. |
| `MaxZoom` | `double` | `20.0` | Largest allowed scale. |
| `MinZoom` | `double` | `0.05` | Smallest allowed scale. |
| `ShowRulers` | `bool` | `false` | Whether tick bands with numbers are drawn along the top and left. |
| `ShowZoomControl` | `bool` | `true` | Whether the bottom-right −/slider/+ and percentage read-out are drawn. |
| `Viewport` | `Rectangle` | — | The client rectangle content occupies, inside the rulers when shown. |
| `Zoom` | `double` | `1.0` | The scale factor, clamped to `[MinZoom, MaxZoom]`. Setting it zooms about the viewport centre. |

### Methods

| Name | Description |
|---|---|
| `ActualSize()` | Resets the zoom to 1.0 and centres the content. |
| `FitToWindow()` | Scales the content to fit entirely within the viewport and centres it. |
| `ZoomTo(double zoom, int anchorX, int anchorY)` | Zooms while keeping the content point under the given viewport-local pixel fixed. |

### Events

| Name | Description |
|---|---|
| `PaintContent` | Raised during painting, clipped to the viewport, so a host can draw scaled/panned content. `ZoomPanelPaintEventArgs` carries `Graphics`, `Zoom`, `Origin`, `Viewport` and `ToView(PointF)`. |
| `ZoomChanged` | Raised whenever `Zoom` changes. |

## Notes

- The wheel zooms about the cursor; a left or middle drag pans. `+`/`-` zoom about the centre, Ctrl+0 is actual size and Ctrl+9 fits.
- The zoom control's slider is logarithmic, so equal pixel steps multiply the zoom by a constant factor. **Clicking the percentage read-out turns it into a text field** — type an exact level and press Enter.
- Content is rendered as `view = content × Zoom + Origin`; hosts draw in view space through `ToView` because `IGraphics` has no transform stack.

## Differences from WinForms

No equivalent.
