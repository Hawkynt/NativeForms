# DomainUpDown

> A spinner over a list of strings: a hosted native [`TextBox`](textbox.md) shows the selected item, the themed spinner buttons and Up/Down keys walk through `Items` — wrapping around the ends when `Wrap` is on — and typing an item's text selects it at the next commit point.

`Hawkynt.NativeForms.DomainUpDown` · strategy: **owner-drawn** (spinner shell over a hosted native `TextBox`) · peer: `ICanvasPeer` + `ITextBoxPeer`

## Usage

```csharp
var spinner = new DomainUpDown { Bounds = new(20, 20, 120, 24), Items = { "Alpha", "Beta", "Gamma" } };
spinner.SelectedItemChanged += (_, _) => Console.WriteLine(spinner.SelectedItem);
form.Controls.Add(spinner);

spinner.SelectedItem = "Beta";
spinner.DownButton();   // walks forward to "Gamma"
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Items` | `ObservableList<string>` | empty | The items the spinner walks through. Mutations keep the selection on the same item (shifted by inserts/removes before it, cleared when it vanishes). |
| `Wrap` | `bool` | `false` | Whether stepping past either end wraps around to the other. |
| `SelectedIndex` | `int` | `-1` | Selected item index, `-1` for none. Setting it mirrors the item into the editor. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `string?` | `null` | The selected item; setting selects by `IndexOf`. |

### Events

| Name | Description |
|---|---|
| `SelectedItemChanged` | Raised when the selection changes — by stepping, typing or assignment. |

### Methods

| Method | Description |
|---|---|
| `UpButton()` | Steps to the *previous* item (classic WinForms semantics: up walks backward through the list), committing a pending typed edit first. |
| `DownButton()` | Steps to the *next* item. From no selection, either direction lands on the first item. Without `Wrap` the ends clamp. |

The inherited `Text` property is the hosted editor's content; assigning it counts as a pending user edit. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Built on the shared `UpDownBase` engine (also behind [`NumericUpDown`](numericupdown.md)): hosted native editor, themed spinner column, click-and-hold autorepeat (500 ms initial delay, then every 50 ms), Up/Down key stepping.
- **Commit points, honestly.** A pending edit is committed before any step and when the surface loses focus: the text is matched case-insensitively against `Items` — a hit selects that item and normalizes the editor to the item's casing, a miss reverts the editor to the current item. `DomainUpDownTests` pin both paths headlessly.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.5 and §7.1): an Enter commit from *inside* the hosted native editor. The focus model routes keys for owner-drawn surfaces, but a native text widget cannot preview them yet — that needs a key seam on `ITextBoxPeer`.

## Differences from System.Windows.Forms.DomainUpDown

- **`Items` is an `ObservableList<string>`**, not WinForms' untyped object collection: entries are strings, and mutating the list repaints. There is no **`Sorted`** property — order the list yourself.
- **An out-of-range `SelectedIndex` coerces to `-1`** (no selection) instead of throwing `ArgumentOutOfRangeException`. This matches every other selection control in the toolkit.
- Same engine-level deltas as [`NumericUpDown`](numericupdown.md): commit-point semantics rather than per-keystroke validation, and no `InterceptArrowKeys`/`ReadOnly`/`UpDownAlign`.
