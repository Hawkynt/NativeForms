# GroupBox

> An owner-drawn container that frames its children with a themed border and paints its `Text` as a caption over the top-left of that frame.

`Hawkynt.NativeForms.GroupBox` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var group = new GroupBox { Text = "Settings", Bounds = new(10, 10, 200, 120) };
group.Controls.Add(new CheckBox { Text = "Enable", Bounds = new(12, 28, 160, 20) });
form.Controls.Add(group);
```

## API

`GroupBox` adds no members of its own — the caption is the inherited `Text` property. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Painted with the platform `ITheme` (`ControlBackground`, `Border`, `ControlText`, `DefaultFont`), so it matches the host desktop; testable headlessly through the test backend's recording canvas.
- The frame's top edge runs through the caption's vertical middle; a gap is punched into the top border and the caption painted over it, fieldset-style. With an empty `Text`, only the frame is drawn.
- Purely decorative — it takes no focus and handles no input; children are added to `Controls` as usual.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.2): child inset/layout and a caption image (icon before the caption text).
