# NumericUpDown

> A spinner for a `decimal` value: a hosted native [`TextBox`](textbox.md) shows the number formatted to `DecimalPlaces`, the themed spinner buttons and Up/Down keys step by `Increment`, and assignments clamp into [`Minimum`, `Maximum`].

`Hawkynt.NativeForms.NumericUpDown` · strategy: **owner-drawn** (spinner shell over a hosted native `TextBox`) · peer: `ICanvasPeer` + `ITextBoxPeer`

## Usage

```csharp
var spinner = new NumericUpDown
{
    Minimum = 0,
    Maximum = 10,
    Increment = 0.25m,
    DecimalPlaces = 2,
    Bounds = new(20, 20, 120, 24),
};
spinner.ValueChanged += (_, _) => Console.WriteLine(spinner.Value);
form.Controls.Add(spinner);

spinner.UpButton();   // 0.00 -> 0.25
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `decimal` | `0` | Lowest accepted value. Raising it above `Maximum` drags the maximum along; the current value re-clamps. |
| `Maximum` | `decimal` | `100` | Highest accepted value. Lowering it below `Minimum` drags the minimum along. |
| `Value` | `decimal` | `0` | The current value, clamped into the range. Reading it commits a pending typed edit first, so callers always see what the user entered. |
| `Increment` | `decimal` | `1` | The step a spinner button or Up/Down key changes the value by. Negative values throw `ArgumentOutOfRangeException`. |
| `DecimalPlaces` | `int` | `0` | Decimal digits the editor displays (0–28); out-of-range values throw. |

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes — by stepping, typing or assignment. Assigning the current value again does not re-raise. |

### Methods

| Method | Description |
|---|---|
| `UpButton()` / `DownButton()` | Steps the value one `Increment` up/down, committing a pending typed edit first. |

The inherited `Text` property is the hosted editor's content; assigning it counts as a pending user edit. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Built on the shared `UpDownBase` engine (also behind [`DomainUpDown`](domainupdown.md)): the native editor fills the field so caret, selection, clipboard and IME stay platform-native, and the owner-drawn surface paints the themed up/down button column (`ScrollBarSize + 1` px wide) at the right edge.
- **Autorepeat.** Clicking a spinner button steps once; holding it repeats through a backend timer — 500 ms initial delay, then every 50 ms. Release, or the pointer leaving the control, stops it. `NumericUpDownTests` drive the timer headlessly.
- **Commit points, honestly.** There is no toolkit-wide focus model yet, so a typed edit has no Enter-key moment to commit at. Instead a pending edit is committed — parsed and clamped, or reverted when unparsable — before any step (buttons, keys, autorepeat, exactly like the classic control validates before stepping), when the surface loses focus, and whenever `Value` is read while an edit is pending (mirroring the classic getter-side validation). Out-of-range input clamps and the editor is rewritten to the clamped value; garbage reverts to the current value.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.5 and §7.1): an Enter commit from inside the hosted editor — it needs key events on `ITextBoxPeer` and the focus model.
