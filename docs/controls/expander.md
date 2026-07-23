# Expander

> A collapsible container: a themed header row — triangle glyph plus `Text` — over a content area of
> ordinary child controls. Collapsing folds the control down to the header; expanding restores
> exactly what was there.

`Hawkynt.NativeForms.Expander` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
expander.Controls.Add(new Button { Text = "More", Bounds = new(10, 40, 80, 24) });
expander.ExpandedChanged += (_, _) => Console.WriteLine(expander.Expanded);
form.Controls.Add(expander);
```

## API

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Expanded` | `bool` | `true` | Whether the content area is shown. Collapsing remembers the current height and shrinks to `HeaderHeight`; expanding restores it. |
| `HeaderHeight` | `int` (get) | theme row height | Pixel height of the header row — the whole control while collapsed. |
| `Image` | `IImage?` | `null` | An optional icon painted in the header beside the caption (after the expand glyph); `TextImageRelation` places it before or after the text. |
| `TextImageRelation` | `TextImageRelation` | `ImageBeforeText` | Where the `Image` sits relative to the caption; `TextBeforeImage` puts the icon after the text. |

### Events

| Event | Description |
|---|---|
| `ExpandedChanged` | Raised after `Expanded` changes, whether by property, header click or Space. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Collapsing hides the child *peers* only — each child's logical `Visible` stays untouched, so
  expanding brings back exactly the children that were visible before. A child added while collapsed
  realizes hidden and appears on expand.
- A left-button release inside the header row toggles; clicks in the content area do not. Space
  toggles too (the control is focusable, a click focuses it).
- The header paints a triangle glyph — down while expanded, right while collapsed — followed by the
  caption; the whole control is framed with a themed border over `HeaderBackground` /
  `ControlBackground`. Testable headlessly through the test backend's recording canvas.
- Complete per [docs/PRD.md](../PRD.md) §7.9 — no pending items.
