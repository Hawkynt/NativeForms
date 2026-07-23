# ComboBox

> A drop-down selector: an owner-drawn field in the native theme whose list opens as a light-dismiss popup below the field, with rows painted by the same renderer as [`ListBox`](listbox.md) — icons, hover highlight, theme selection colors — so the drop-down is pixel-identical to a list.

![ComboBox in the NativeForms demo](../screenshots/03-lists.png)

`Hawkynt.NativeForms.ComboBox` · strategy: **owner-drawn** (native theme; the editable style hosts a native [`TextBox`](textbox.md)) · peer: `ICanvasPeer` + `IPopupPeer`

## Usage

```csharp
var combo = new ComboBox
{
    Bounds = new(20, 20, 160, 24),
    PlaceholderText = "pick one",
    DisplaySelector = static item => ((Fruit)item!).Name,
    ValueSelector = static item => ((Fruit)item!).Id,
};
combo.Items.AddRange([new Fruit(1, "apple"), new Fruit(2, "banana")]);
combo.SelectedIndexChanged += (_, _) => Console.WriteLine(combo.SelectedValue);
form.Controls.Add(combo);

combo.SelectedValue = 2;   // selects "banana" — the ValueMember/SelectedValue loop, reflection-free

sealed record Fruit(int Id, string Name);
```

Plain values work without any selector: the default `DisplaySelector` calls `ToString()`.

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Items` | `ObservableList<object?>` | empty | The items offered by the drop-down. Mutating the collection repaints the control and keeps the selection on the same item across inserts/removes. |
| `DropDownStyle` | `ComboBoxStyle` | `DropDownList` | Closed and owner-painted (`DropDownList`) or editable through a hosted native `TextBox` (`DropDown`). `Simple` throws `NotSupportedException`. |
| `DisplaySelector` | `Func<object?, string>` | `ToString()` | Produces the display text for an item. Setting `null` restores the default. |
| `ImageSelector` | `Func<object?, IImage?>?` | `null` | Optional selector producing an icon per item; wins over `ImageList` + `ImageIndexSelector` when both are set. |
| `ImageList` / `ImageIndexSelector` | `ImageList?` / `Func<object?, int>?` | `null` | Icon store plus an index selector (negative index means no icon); images materialize lazily. |
| `ValueSelector` | `Func<object?, object?>?` | `null` | Maps an item to its binding value — the reflection-free stand-in for `ValueMember`; `null` makes the item its own value. |
| `PlaceholderText` | `string` | `""` | Greyed hint shown while nothing is selected (closed style) or the hosted editor is empty. |
| `MaxDropDownItems` | `int` | `8` | Maximum rows the drop-down shows before it scrolls. |
| `SelectedIndex` | `int` | `-1` | Selected item index, `-1` for none. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `object?` | `null` | The selected item; setting selects by `IndexOf`. |
| `SelectedValue` | `object?` | `null` | `ValueSelector` applied to `SelectedItem` (or the item itself). Assigning selects the first item whose value `Equals` the given one; no match clears the selection. |
| `DataSource` | `IEnumerable?` (set) | — | Clears `Items` and copies the sequence in (one-way snapshot, not a live view). |
| `DroppedDown` | `bool` | `false` | Whether the drop-down is currently open. Settable, like its WinForms namesake. |

The inherited `Text` property is overridden: in the editable style it mirrors the hosted editor; in the closed style it is the selected item's display text, and assigning selects the item with that text.

### Events

| Name | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes — by popup commit, keyboard, assignment, or the selected item being removed. |
| `DropDown` | Raised when the popup opens. |
| `DropDownClosed` | Raised when the popup closes — commit, cancel and light-dismiss alike. |

### Methods

| Method | Description |
|---|---|
| `OpenDropDown()` | Opens the popup below the field: field width, one row per item up to `MaxDropDownItems`, hover starting on the selected item. A no-op while open or before realization. |
| `CloseDropDown()` | Closes the popup without changing the selection. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Keyboard model, matching the classic control.** Alt+Down and F4 open the drop-down (F4 also closes it). While *closed*, Up/Down move the selection directly and typing a letter cycles through the items whose display text starts with it. While *open*, Up/Down move only the hover row, typing jumps the hover to the next prefix match, Enter commits the hovered row, and Escape closes without committing. The wheel scrolls the popup three rows per notch.
- **The popup is a light-dismiss surface** (`IPopupPeer`): a click outside, grab loss or Escape dismisses it without changing the selection. Committing (click or Enter) closes first, then sets `SelectedIndex` — one `SelectedIndexChanged` per commit.
- The editable `DropDown` style hosts a native `TextBox` over the field area (the arrow-button zone stays free), so caret, clipboard and IME are platform-native; its text mirrors into `Text`/`TextChanged`, and selecting an item pushes the display text into the editor.
- Icons come from `ImageSelector` or `ImageList` + `ImageIndexSelector` and are painted by the shared `ListBox` row painter in both the closed field and the popup rows.
- `ComboBoxTests` pin the whole surface headlessly: popup geometry, hover/commit, dismissal, the keyboard model, value binding, and the hosted editor.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.4): the `Simple` style and autocomplete (needs key events on `ITextBoxPeer`).

## Differences from System.Windows.Forms.ComboBox

- **`DropDownStyle` defaults to `DropDownList`** (closed, owner-painted), not WinForms' editable `DropDown`; `Simple` throws `NotSupportedException`.
- **`SelectedIndexChanged` fires on commit only** — a click or Enter in the popup commits, mere hover never does — so there is no separate `SelectionChangeCommitted`; the one event covers commits, keyboard moves on the closed field and programmatic assignment.
- **Binding is selector-based**: `DisplaySelector`/`ValueSelector` replace `DisplayMember`/`ValueMember` (no reflection), and `DataSource` is a set-only snapshot, not a live currency-managed binding.
- `DropDown` and `DropDownClosed` exist as in WinForms; there is no `TextUpdate`, `DropDownWidth`/`DropDownHeight` (the popup is field-wide, `MaxDropDownItems` rows tall) or autocomplete yet.
