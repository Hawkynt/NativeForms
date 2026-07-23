# ProgressTile

> An Explorer-style tile: an icon, a primary caption, an optional secondary caption and a themed usage bar that turns a warning colour once it is nearly full — the shape a file manager uses to show how full a drive is.

![ProgressTile in the NativeForms demo](../screenshots/07-pickers.png)

`Hawkynt.NativeForms.ProgressTile` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var drive = new ProgressTile
{
    Bounds = new(20, 20, 300, 76),
    Text = "Windows (C:)",
    SecondaryText = "45.2 GB free of 128 GB",
    Image = icons.GetImage(0),
    Maximum = 128,
    Value = 83,
    WarningThreshold = 115,
    Clickable = true,
};
drive.Click += (_, _) => Open(@"C:\");
form.Controls.Add(drive);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Image` | `IImage?` | `null` | The icon at the leading edge, or `null` for a text-only tile. |
| `SecondaryText` | `string` | `""` | The line under the caption. Empty hides the line. |
| `Maximum` | `int` | `100` | The highest value the bar can represent. |
| `Value` | `int` | `0` | The amount used, clamped to `[0, Maximum]`. |
| `WarningThreshold` | `int` | `0` | The value at which the bar switches to `WarningColor`, in `Value`'s units. `0` leaves the warning off. |
| `WarningColor` | `Color` | `#E81123` | The fill used past the threshold — the alert red Explorer paints a nearly-full drive in. |
| `IsWarning` | `bool` | `false` | Whether the bar is currently past the threshold. |
| `Clickable` | `bool` | `false` | Whether the tile behaves as a button: focusable, hover-highlighted, raising `Click`. |
| `Selected` | `bool` | `false` | Whether the tile paints as the selected one of a set. |
| `Compact` | `bool` | `false` | Short one-row layout: the icon on the left with the caption stacked directly over the usage bar to its right, the two sized to the icon's height. The `SecondaryText` line is not shown in this mode. |

The inherited `Text` is the primary caption. Also honours the ambient `Font` and `ForeColor`.

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes — a real move only, not a re-assignment of the same number. |
| `Click` | Raised on a click or Space, while `Clickable`. |

Inherits the common members of [`Control`](control.md) plus the owner-drawn surface of `OwnerDrawnControl`.

## Notes

- **Why it is not called `DriveTile`.** The type knows nothing about drives: there is no `DriveInfo` binding and no byte formatting, because `NativeForms.Core` stays platform-agnostic and the paint path may not touch the filesystem (PRD §4). Both captions are plain strings the application sets, so one tile serves a mailbox quota or a download just as well as a volume. The name describes the shape; the drive list is the motivating use, not the limit.
- `SecondaryText` being a stored string rather than a value plus a formatter is deliberate: it is what keeps a repaint from formatting, concatenating or boxing anything.
- The bar reuses `GlyphRenderer.DrawProgressBar`, so a tile and a [`ProgressBar`](progressbar.md) render the same fill from the same code — the warning colour is a fill-colour overload, not a second implementation.
- **Give the tile room for three rows.** Content is caption, bar, secondary caption, stacked inside 8 px padding. A tile too short for the whole stack drops the parts that no longer fit, bottom-up, rather than overdrawing them — the caption is what identifies the tile, so it is the last to go. At the platform's default font, ~76 px is enough for all three; the headless test metric fits in 64 px, which is why the demo uses the larger figure.
- `Clickable` gates the whole button behaviour at once: an inert tile takes no focus, does not highlight on hover and raises no `Click`. Space activates on the key *release*, like every other button face in the toolkit.
- `Selected` paints the theme's selection background and switches both captions to the selection text colour, so a set of tiles reads as one exclusive list.
- Construction costs ~352 B, inside the 768 B owner-drawn budget (PRD §4); a steady-state repaint allocates 0 bytes.
- `ProgressTileTests` pin the surface headlessly: all four content parts, the proportional fill, value clamping, the threshold switch and a replaceable warning colour, the inert-vs-clickable split, hover and selection faces, and clipping.

## Differences from WinForms

Windows Forms has no equivalent; this is a modern extra (PRD §7.9). `Value`/`Maximum` follow `ProgressBar`'s names and clamping, but there is no `Minimum` — a usage tile always starts at zero.
