# Breadcrumb

> An Explorer-style breadcrumb bar: a row of path segments separated by chevrons, each hover-highlit and clickable, that folds its leading segments behind a "…" overflow chip when the path outgrows the control's width.

`Hawkynt.NativeForms.Breadcrumb` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var crumb = new Breadcrumb { Bounds = new(8, 8, 400, 26) };
crumb.Items.AddRange("Computer", "Documents", "Projects", "NativeForms");
crumb.ItemClicked += (_, e) => Navigate((string)e.Item.Tag! /* or e.Item.Text */);
form.Controls.Add(crumb);
```

Clicking a segment trims the path to it (the navigate-up gesture) and raises `ItemClicked`. Turn
`TrimOnClick` off to keep the whole path and drive navigation entirely from the event.

## API

### Breadcrumb

| Member | Description |
|---|---|
| `Items` | `BreadcrumbItemCollection` — the path segments, left to right (`Add`, `Add(string)`, `AddRange(params string[])`, `Remove`, `TrimAfter(index)`, `Clear`). |
| `ImageList` | `ImageList?` — the icons segments' `ImageIndex`/`ImageKey` point into. |
| `TrimOnClick` | `bool` (default `true`) — whether clicking a segment trims the path to it before `ItemClicked` fires. |
| `ItemClicked` | Raised when a segment is clicked (after any trim), carrying the `BreadcrumbItem` and its `Index`. |

### BreadcrumbItem

| Member | Description |
|---|---|
| `Text` | The segment caption. |
| `Tag` | Arbitrary caller data — a folder path, a node, an id. |
| `ImageIndex` / `ImageKey` | The segment's icon in the breadcrumb's `ImageList` (index wins, key falls back). |

## Notes

- **Overflow.** When the segments do not fit, the leading ones fold behind a non-navigable "…" chip
  and the trailing path — the last segment always included — stays visible, so a deep path never
  spills out of frame. Widths are measured through `IGraphics.MeasureText`.
- Segments hover-highlight (the themed header background, accent caption); the chevron separators are
  painted as themed glyphs. The control is focusable and painted with the platform `ITheme`.
- Content is clipped to the control, and hit zones come from the most recent paint — a click maps to
  the segment under it, ignoring the overflow chip and empty space.

## Differences from a WinForms control

WinForms has no breadcrumb control; this fills the Explorer navigation-bar gap. The overflow chip is
a plain fold indicator here (it does not drop down a menu of the hidden segments) — reference the
hidden path from your own model if you need that.
