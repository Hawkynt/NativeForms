# NumericUpDown

> A spinner for a `decimal` value: a hosted native [`TextBox`](textbox.md) shows the number formatted to `DecimalPlaces`, the themed spinner buttons and Up/Down keys step by `Increment`, and assignments clamp into [`Minimum`, `Maximum`].

![NumericUpDown in the NativeForms demo](../screenshots/02-input.png)

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
| `ThousandsSeparator` | `bool` | `false` | Whether the editor displays group separators (`"N"` instead of `"F"` formatting); typed input parses with separators either way. |

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

- Built on the shared `UpDownBase` engine (also behind [`DomainUpDown`](domainupdown.md)): the native editor fills the field so caret, selection, clipboard and IME stay platform-native, and the owner-drawn surface paints the themed up/down button column (`ScrollBarSize + 1` px wide) at the right edge through the shared `Drawing.SpinnerRenderer`, which [`TimePicker`](timepicker.md) reuses so every spinner in the toolkit is pixel-identical.
- **Autorepeat.** Clicking a spinner button steps once; holding it repeats through a backend timer — 500 ms initial delay, then every 50 ms. Release, or the pointer leaving the control, stops it. `NumericUpDownTests` drive the timer headlessly.
- **Commit points, honestly.** A native text widget cannot preview keys yet, so a typed edit has no Enter-key moment to commit at. Instead a pending edit is committed — parsed and clamped, or reverted when unparsable — before any step (buttons, keys, autorepeat, exactly like the classic control validates before stepping), when the surface loses focus, and whenever `Value` is read while an edit is pending (mirroring the classic getter-side validation). Out-of-range input clamps and the editor is rewritten to the clamped value; garbage reverts to the current value.
- **Enter and arrows from inside the editor.** Through the `ITextBoxPeer.KeyDown` seam the spinner gets first refusal on its keys even while the caret lives in the native editor: Enter commits the pending edit (parse-and-clamp, or revert), and Up/Down step the value — the key is consumed before the editor acts on it.

## Differences from System.Windows.Forms.NumericUpDown

- **Clamping happens at the commit points** (before a step, on focus loss, on a `Value` read) rather than per keystroke, and there is **no `MaxLength`** or input-length limiting — type freely, the commit clamps or reverts.
- `ThousandsSeparator` exists as in WinForms; **`Hexadecimal`, `InterceptArrowKeys` and `Accelerations` do not** — arrow keys always step, at the fixed autorepeat cadence.
- No `UpDownAlign`, no `ReadOnly` on the spinner itself (the hosted editor's `ReadOnly` is not surfaced).
