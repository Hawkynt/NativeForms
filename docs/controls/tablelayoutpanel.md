# TableLayoutPanel

> A [`Panel`](panel.md) that slices its client area into a `ColumnCount` × `RowCount` grid and fills
> each child into its cell minus the child's `Margin` — tracks sized by absolute, percent or
> auto-size styles, explicit cell positions and spans, row-major auto-placement for the rest, and an
> optional themed cell grid.

`Hawkynt.NativeForms.TableLayoutPanel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var table = new TableLayoutPanel { Bounds = new(0, 0, 300, 100), ColumnCount = 2, RowCount = 2 };
table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

var wide = new Button { Text = "Spans both columns", Margin = new(3) };
table.Controls.AddRange(wide, new Button { Text = "A", Margin = new(3) }, new Button { Text = "B", Margin = new(3) });
table.SetColumnSpan(wide, 2);           // row 0 entirely; A and B auto-place into row 1
form.Controls.Add(table);
```

## API

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ColumnCount` | `int` | `1` | The number of columns; coerced to at least one. |
| `RowCount` | `int` | `1` | The number of rows; coerced to at least one. |
| `ColumnStyles` | `TableLayoutStyleCollection<ColumnStyle>` | empty | The sizing rules per column, in track order. |
| `RowStyles` | `TableLayoutStyleCollection<RowStyle>` | empty | The sizing rules per row, in track order. |
| `CellBorderStyle` | `TableLayoutPanelCellBorderStyle` | `None` | The grid lines painted between cells; `Single` insets every cell by one pixel per line. |

### Methods

| Method | Description |
|---|---|
| `SetCellPosition(control, column, row)` | Pins a control to the given cell; auto-placement flows the other children around it. |
| `GetCellPosition(control)` | The `TableLayoutPanelCellPosition` a control was pinned to, or `(-1, -1)` while it auto-places. |
| `SetColumnSpan(control, value)` / `GetColumnSpan(control)` | Stretches a control across neighboring columns; 1 by default. |
| `SetRowSpan(control, value)` / `GetRowSpan(control)` | Stretches a control across neighboring rows; 1 by default. |
| `PerformLayout()` | Recomputes the grid and repositions every child. Runs automatically on every structural change — an explicit call is rarely needed. |

Inherits [`Panel`](panel.md) (`BorderStyle`, `AutoScroll`, `AutoScrollPosition`) and through it the
common members of [`Control`](control.md).

### Styles

`ColumnStyle` / `RowStyle` (both `TableLayoutStyle`) carry a `SizeType` and a size (`Width` /
`Height`); the parameterless constructors create auto-sized styles. Editing a style that belongs to
a live panel — including its `Width`/`Height` — re-lays the grid out immediately, as does every
mutation of the style collections (`Add`, `RemoveAt`, `Clear`).

`SizeType` (enum): `AutoSize` (sized to the largest child in the track, plus that child's margin),
`Absolute` (fixed pixels), `Percent` (a weighted share of the space left after absolute and
auto-sized tracks). Tracks beyond the styled ones share the remaining space equally.

## Notes

- **Placement.** Explicit cell assignments are clamped into the grid and marked first; then children
  without one scan row-major for the next run of free cells their spans fit into. A child no free
  cell can hold keeps its bounds untouched. Each placed child fills its cell (or span) minus its
  `Margin`.
- **Track sizing.** Auto-sized tracks measure each single-span child's *externally set* size — the
  layout pass itself resizes children to their cells, so the size a child had when it joined or was
  last resized from outside is what counts. Percent tracks split the leftover by weight, the last
  one absorbing the rounding remainder.
- **Cell borders.** `Single` paints a themed grid line around every cell, offsets every track by one
  pixel per line, and shifts the grid along with the content when the inherited `AutoScroll`
  scrolls.
- Layout runs in logical space, so an overflowing grid paints themed scrollbars with `AutoScroll`
  and scrolls by moving the child peers.
- Re-layout triggers: panel resize, `Controls.Add`/`Remove`, a child's `Bounds` or `Margin` change,
  `ColumnCount`/`RowCount`, `CellBorderStyle`, style edits and cell/span assignments. The pass is a
  single deterministic sweep over reused buffers — no per-layout allocation once warm.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.2): grid auto-grow, skipping invisible
  children, and `Anchor`/`Dock` interplay.

## Differences from System.Windows.Forms.TableLayoutPanel

- **No `GrowStyle`** — the grid never adds rows or columns on overflow. Explicit cell positions past
  the grid are clamped into it; an auto-placed child no free run of cells can hold is simply not
  laid out and keeps its current bounds.
- **`SetCellPosition(control, column, row)` takes an int pair**, not a
  `TableLayoutPanelCellPosition` argument (the struct exists as `GetCellPosition`'s return type).
- A child's `Anchor`/`Dock` inside its cell is not honored yet (each child fills cell minus
  `Margin`); WinForms aligns and stretches by anchor within the cell.
