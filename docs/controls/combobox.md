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
| `AutoCompleteMode` | `AutoCompleteMode` | `None` | How the editable style completes what is typed against the items: `Suggest` narrows the drop-down to the matches, `Append` fills the rest of the first match into the field and leaves it selected, `SuggestAppend` does both. Candidates are the combo's own items — Windows Forms' `AutoCompleteSource.ListItems` — because committing one sets `SelectedIndex`, and a candidate from anywhere else has no index to commit. A `DropDownList` combo ignores this: with no editor there is nothing to complete. |
| `DataSource` | `IEnumerable?` (set) | — | Clears `Items` and copies the sequence in (one-way snapshot, not a live view). |
| `SetDataSource<T>(IEnumerable<T>, string?, string?)` | Replaces the items and resolves `displayMember`/`valueMember` names through the `[Bindable]` generator — the Windows Forms shape, without reflection. See [MVVM](../mvvm.md#binding-a-list-by-member-name--bindable). |
| `DisplaySelector` | `Func<object?, string>` | `ToString()` | Produces the display text for an item. Setting `null` restores the default. |
| `DropDownStyle` | `ComboBoxStyle` | `DropDownList` | Closed and owner-painted (`DropDownList`) or editable through a hosted native `TextBox` (`DropDown`). `Simple` throws `NotSupportedException`. |
| `DroppedDown` | `bool` | `false` | Whether the drop-down is currently open. Settable, like its WinForms namesake. |
| `ImageList` / `ImageIndexSelector` | `ImageList?` / `Func<object?, int>?` | `null` | Icon store plus an index selector (negative index means no icon); images materialize lazily. |
| `ImageSelector` | `Func<object?, IImage?>?` | `null` | Optional selector producing an icon per item; wins over `ImageList` + `ImageIndexSelector` when both are set. |
| `Items` | `ObservableList<object?>` | empty | The items offered by the drop-down. Mutating the collection repaints the control and keeps the selection on the same item across inserts/removes. |
| `MaxDropDownItems` | `int` | `8` | Maximum rows the drop-down shows before it scrolls. |
| `PlaceholderText` | `string` | `""` | Greyed hint shown while nothing is selected (closed style) or the hosted editor is empty. |
| `SelectedIndex` | `int` | `-1` | Selected item index, `-1` for none. Out-of-range values coerce to `-1`. |
| `SelectedItem` | `object?` | `null` | The selected item; setting selects by `IndexOf`. |
| `SelectedValue` | `object?` | `null` | `ValueSelector` applied to `SelectedItem` (or the item itself). Assigning selects the first item whose value `Equals` the given one; no match clears the selection. |
| `ValueSelector` | `Func<object?, object?>?` | `null` | Maps an item to its binding value — the reflection-free stand-in for `ValueMember`; `null` makes the item its own value. |

The inherited `Text` property is overridden: in the editable style it mirrors the hosted editor; in the closed style it is the selected item's display text, and assigning selects the item with that text.

### Events

| Name | Description |
|---|---|
| `DropDown` | Raised when the popup opens. |
| `DropDownClosed` | Raised when the popup closes — commit, cancel and light-dismiss alike. |
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes — by popup commit, keyboard, assignment, or the selected item being removed. |

### Methods

| Method | Description |
|---|---|
| `CloseDropDown()` | Closes the popup without changing the selection. |
| `OpenDropDown()` | Opens the popup below the field: field width, one row per item up to `MaxDropDownItems`, hover starting on the selected item. A no-op while open or before realization. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Native widget promotion

| Name | Type | Default | Description |
|---|---|---|---|
| `IsNativeWidget` | `bool` (get) | — | Whether the control is currently backed by a real platform drop-down list. |
| `UseNativeWidget` | `bool?` | `null` | Overrides [`Application.PreferNativeWidgets`](application.md) for this control. |

A stock combo box shows a flat list of strings and nothing else, so the gate is narrow. The control stays
owner-drawn when `DropDownStyle` is `DropDown` (whose editor is a hosted `TextBox`), when
`PlaceholderText` is set, or when `ImageSelector`, `ImageIndexSelector` or `ImageList` would put an icon
beside a row.

**The widget owns the list.** `OpenDropDown`, `CloseDropDown` and `DroppedDown` drive and follow the
platform's own drop-down; `DropDown` and `DropDownClosed` still fire, but there is no popup surface of
ours on screen to inspect. `Items`, `SelectedIndex` and `SelectedIndexChanged` behave identically either
way — items are rendered through `DisplaySelector` and handed over whole whenever the collection changes.

**Crossing the gate on a live control swaps the peer** in either direction — assigning an
`ImageSelector`, switching to the editable style, setting or clearing a placeholder — keeping the items and
the selection. Keyboard focus survives the swap — the promotion is state-transparent.
`UseNativeWidget` itself is read at realization only — a form that is already
showing should not change its rendering out from under the user. See [PRD §12](../PRD.md#12-native-peer-promotion-opt-into-real-widgets-where-the-platform-has-one).

## Notes

- **Keyboard model, matching the classic control.** Alt+Down and F4 open the drop-down (F4 also closes it). While *closed*, Up/Down move the selection directly and typing a letter cycles through the items whose display text starts with it. While *open*, Up/Down move only the hover row, typing jumps the hover to the next prefix match, Enter commits the hovered row, and Escape closes without committing. The wheel scrolls the popup three rows per notch.
- **The popup is a light-dismiss surface** (`IPopupPeer`): a click outside, grab loss or Escape dismisses it without changing the selection. Committing (click or Enter) closes first, then sets `SelectedIndex` — one `SelectedIndexChanged` per commit.
- The editable `DropDown` style hosts a native `TextBox` over the field area (the arrow-button zone stays free), so caret, clipboard and IME are platform-native; its text mirrors into `Text`/`TextChanged`, and selecting an item pushes the display text into the editor.
- Icons come from `ImageSelector` or `ImageList` + `ImageIndexSelector` and are painted by the shared `ListBox` row painter in both the closed field and the popup rows.
- **Autocomplete.** A suggestion filter narrows the drop-down to the matching items and the list re-fits itself *in place* rather than re-showing — on a backend with a pointer grab, re-showing would hand the grab round and dismiss the list mid-edit. No match closes the list instead of opening an empty box under the field, and an empty field is no filter at all rather than every row. A deletion never completes, or the text could not be shortened; it is told from an insertion by comparing against what was **typed** rather than what the field shows, since typing over a selected completion makes the field shorter and a field-length comparison would read every second keystroke as a backspace. While a filter is up, hover, hit-testing, paint and the commit all run in row space, so a click lands on the item the filter left under the pointer rather than the one that position holds unfiltered.
- `ComboBoxTests` pin the whole surface headlessly: popup geometry, hover/commit, dismissal, the keyboard model, value binding, and the hosted editor. `ComboBoxAutoCompleteTests` pin the completion rules above.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.4): the `Simple` style.

## Differences from System.Windows.Forms.ComboBox

- **`DropDownStyle` defaults to `DropDownList`** (closed, owner-painted), not WinForms' editable `DropDown`; `Simple` throws `NotSupportedException`.
- **`SelectedIndexChanged` fires on commit only** — a click or Enter in the popup commits, mere hover never does — so there is no separate `SelectionChangeCommitted`; the one event covers commits, keyboard moves on the closed field and programmatic assignment.
- **Binding is selector-based**: `DisplaySelector`/`ValueSelector` replace `DisplayMember`/`ValueMember` (no reflection), and `DataSource` is a set-only snapshot, not a live currency-managed binding.
- `DropDown` and `DropDownClosed` exist as in WinForms; there is no `TextUpdate`, `DropDownWidth`/`DropDownHeight` (the popup is field-wide, `MaxDropDownItems` rows tall) or autocomplete yet.
