# StatusStrip

> An owner-drawn status bar: a row of text+icon panels, embedded progress gauges and an optional

![StatusStrip in the NativeForms demo](../screenshots/01-basics.png)
> size grip — fixed panels take their measured width, `Spring` panels share whatever is left over.

`Hawkynt.NativeForms.StatusStrip` · strategy: **owner-drawn** (native theme) · peer: `ICanvasPeer`

## Usage

```csharp
var strip = new StatusStrip { Bounds = new(0, 0, 300, 24) };
var message = new ToolStripStatusLabel("Ready") { Spring = true };
var progress = new ToolStripProgressBarItem { Width = 100 };
var clock = new ToolStripStatusLabel("12:00");
strip.Items.AddRange(message, progress, clock);
form.Controls.Add(strip);

progress.Value = 50; // repaints the embedded gauge
```

## API

### StatusStrip

| Member | Type | Default | Description |
|---|---|---|---|
| `Items` | `ToolStripItemCollection` | empty | The panels. Mutating the collection (or any item in it) repaints the bar. |
| `SizingGrip` | `bool` | `true` | Whether the diagonal-dot resize grip is painted in the bottom-right corner. Its 14 px square is reserved from the spring budget; hiding it returns the width to the springs. |
| `GetItemWidth(int index)` | method | | The pixel width panel `index` currently occupies — fixed panels their measured width, springs their equal share of the leftover. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`). Panels share the `ToolStripItem` model documented on
the [`MenuStrip`](menustrip.md) page.

### ToolStripStatusLabel

| Member | Type | Default | Description |
|---|---|---|---|
| `Spring` | `bool` | `false` | Whether the panel stretches to absorb the width the fixed panels leave unused. Several springs share the leftover equally, the first springs taking any odd pixels, so the panels always tile the full width exactly. |

Constructors: `ToolStripStatusLabel()`, `ToolStripStatusLabel(string text)`. A panel paints its
optional icon (`Image` or `ImageList` + `ImageIndex`, 16 px, vertically centered) followed by its
left-aligned caption.

### ToolStripProgressBarItem

| Member | Type | Default | Description |
|---|---|---|---|
| `Width` | `int` | `100` | The fixed pixel width the gauge occupies in the strip; clamped to at least 1. |
| `Minimum` | `int` | `0` | The lowest value the gauge can represent. Raising it above `Maximum` pulls `Maximum` up; `Value` is re-clamped. |
| `Maximum` | `int` | `100` | The highest value the gauge can represent. Lowering it below `Minimum` pulls `Minimum` down; `Value` is re-clamped. |
| `Value` | `int` | `0` | The current progress, clamped to [`Minimum`, `Maximum`] on assignment. |

The gauge paints through the same renderer as the standalone
[`ProgressBar`](progressbar.md) control, so the fill math and theming are identical — only the
hosting differs. It is inset 2 px horizontally and 3 px vertically within its panel.

## Notes

- The classic "message area | details | clock" layout is one spring panel between fixed ones; the
  spring math is pinned by `StatusStripTests` down to the odd-pixel distribution.
- The size grip is the classic three-diagonal-row dot pattern, painted in the theme's disabled-text
  color; it is purely visual — the resize drag itself is the window frame's business.
- Any panel state change (`Text`, `Spring`, `Value`, …) bubbles through the item collection into a
  repaint; the bar draws a theme border line along its top edge.
- Testable headlessly through the test backend's recording canvas.

## Differences from System.Windows.Forms.StatusStrip

- **`ToolStripProgressBarItem` replaces `ToolStripProgressBar`** — an owner-drawn item painting
  through the shared gauge renderer, not a hosted `ProgressBar` control (no `Style`/marquee in the
  strip).
- The shared item-model differences apply (no `ItemClicked`, items are not controls — see
  [menustrip.md](menustrip.md)); the size grip is visual only, the frame handles the actual resize.
