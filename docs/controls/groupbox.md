# GroupBox

> An owner-drawn container that frames its children with a themed border and paints its `Text` as a caption over the top-left of that frame, optionally preceded by a small icon.

`Hawkynt.NativeForms.GroupBox` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var group = new GroupBox { Text = "Settings", Bounds = new(10, 10, 200, 120) };
group.Controls.Add(new CheckBox { Text = "Enable", Bounds = new(12, 28, 160, 20) });
form.Controls.Add(group);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Image` | `IImage?` | `null` | An optional icon rendered with the caption in the frame gap; `TextImageRelation` places it before or after the text. Changing it invalidates the control. |
| `TextImageRelation` | `TextImageRelation` | `ImageBeforeText` | Where the `Image` sits relative to the caption; `TextBeforeImage` puts the icon after the text. The header is one horizontal strip, so the before/after values are the meaningful ones. |

The caption itself is the inherited `Text` property. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`ControlBackground`, `Border`, `ControlText`, `DefaultFont`), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- The frame's top edge runs through the caption strip's vertical middle; a gap is punched into the top border and icon + caption painted over it, fieldset-style through the shared image + text content layout. The gap widens to fit both; an `Image` without a caption still punches it, and with neither only the frame is drawn.
- Purely decorative — it takes no focus and handles no input. Children are real nested children: added to `Controls` as usual, they realize as native peers inside the group's canvas peer.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.2): child inset/layout convenience (children position against the group's top-left corner, not inside the frame).
