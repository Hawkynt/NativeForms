# FlowLayoutPanel

> A [`Panel`](panel.md) that positions its own children: they flow in `Controls` order along
> `FlowDirection`, each keeping its own `Size` and offset by its `Margin`, wrapping into a new row or
> column at the client edge.

`Hawkynt.NativeForms.FlowLayoutPanel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
panel.Controls.AddRange(
    new Button { Text = "One", Size = new(60, 24), Margin = new(3) },
    new Button { Text = "Two", Size = new(60, 24), Margin = new(3) },
    new Button { Text = "Three", Size = new(60, 24), Margin = new(3) });
form.Controls.Add(panel);
```

## API

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `FlowDirection` | `FlowDirection` | `LeftToRight` | The edge children flow from and the axis they advance along; changing it re-flows. |
| `WrapContents` | `bool` | `true` | Whether the flow wraps at the client edge; off keeps a single row or column. |

`FlowDirection` (enum): `LeftToRight` (rightward, wrapping into rows below), `TopDown` (downward,
wrapping into columns to the right), `RightToLeft` (leftward from the right edge, rows below),
`BottomUp` (upward from the bottom edge, columns to the right).

### Methods

| Method | Description |
|---|---|
| `PerformLayout()` | Repositions every child along the flow in a single pass. Runs automatically whenever the panel resizes, a child joins, leaves or resizes, or a flow property changes — an explicit call is rarely needed. |

Inherits [`Panel`](panel.md) (`BorderStyle`, `AutoScroll`, `AutoScrollPosition`) and through it the
common members of [`Control`](control.md).

## Notes

- The flow never resizes a child — each keeps the `Size` it was given and consumes it plus its
  `Margin` along the flow. A new row (or column) starts below (beside) the tallest (widest)
  child-plus-margin of the previous one.
- Layout runs in logical space: children get logical `Bounds` along the flow, so with the inherited
  `AutoScroll` on, an overflowing flow paints themed scrollbars and scrolls by moving the child
  peers — the logical bounds stay put.
- Re-flow triggers: panel resize (which re-wraps), `Controls.Add`/`Remove`, a child's `Bounds` or
  `Margin` change, and any change to `FlowDirection` or `WrapContents`.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.2): `Anchor`/`Dock` interplay and skipping
  invisible children.
