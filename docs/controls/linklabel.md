# LinkLabel

> An owner-drawn hyperlink label: its whole text paints in the theme's accent color with an underline, shifts subtly while hovered, and raises `LinkClicked` on a click inside the text or on Space when focused.

`Hawkynt.NativeForms.LinkLabel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var link = new LinkLabel { Text = "Release notes", Bounds = new(20, 20, 160, 24) };
link.LinkClicked += (_, _) =>
{
    OpenBrowser("https://example.test/notes");
    link.Visited = true;
};
form.Controls.Add(link);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Visited` | `bool` | `false` | Whether the link has been followed; blends the paint color halfway toward the theme's grey. Not set automatically — flip it in your `LinkClicked` handler. |

### Events

| Name | Description |
|---|---|
| `LinkClicked` | Raised when the link is activated: a left mouse-button release inside the text extent, or Space while focused. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`). The inherited `Text` is the link text.

## Notes

- Painted with the platform `ITheme`: `Accent` text with an underline drawn under the measured text extent, over the `ControlBackground`. Hovering the text shifts the color 30 % toward `ControlText`; `Visited` blends it 50 % toward `DisabledText` first.
- Hit testing is precise: clicks (and hover) count only inside the rectangle the middle-left-aligned text actually occupies, not the whole control bounds — a click in the empty space to the right of a short caption does nothing.
- The control is focusable; Space activates it. `LinkLabelTests` pin painting, hit testing, hover/visited color shifts and the keyboard path headlessly.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): per-character link ranges (WinForms `LinkArea`) — the entire text is the link.
