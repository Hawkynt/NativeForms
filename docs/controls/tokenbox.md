# TokenBox

> An owner-drawn tag/chip input: committed entries become removable chips, each with an × zone, flowing left to right and wrapping. A hosted native [`TextBox`](textbox.md) holds the caret, so typing, selection, clipboard and IME stay platform-native. For tags, recipient fields and search scopes.

![TokenBox in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.TokenBox` · strategy: **owner-drawn + hosted editor** · peer: `ICanvasPeer` + `ITextBoxPeer`

## Usage

```csharp
var tags = new TokenBox { Bounds = new(16, 300, 500, 60), PlaceholderText = "Add a tag…" };
tags.AddToken("design");
tags.AutoCompleteSource = prefix => Vocabulary.Where(v => v.StartsWith(prefix)).ToArray();
tags.ChipStyleProvider = t => t == "urgent"
    ? new TokenChipStyle { BackColor = Color.Firebrick, ForeColor = Color.White, FontStyle = FontStyle.Bold }
    : default;
tags.TokensChanged += (_, _) => Save(tags.Tokens);
form.Controls.Add(tags);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `AllowDuplicates` | `bool` | `false` | Whether the same token may be added twice. |
| `AutoCompleteSource` | `Func<string, IReadOnlyList<string>>?` | `null` | Filters a typed prefix into suggestions dropped down under the editor. |
| `ChipStyleProvider` | `Func<string, TokenChipStyle>?` | `null` | Per-chip fill, text colour and font style chosen from the token text. `null` uses the accent tint. |
| `PlaceholderText` | `string` | empty | The greyed hint shown while the field is empty. |
| `Tokens` | `IReadOnlyList<string>` | empty | The committed chips, in order. |

### Methods

| Name | Description |
|---|---|
| `AddToken(string text)` | Adds a chip; ignores empty text and, unless `AllowDuplicates`, an existing one. |
| `ClearTokens()` | Removes every chip. |
| `RemoveToken(int index)` | Removes the chip at the index; out-of-range is a no-op. |

### Events

| Name | Description |
|---|---|
| `TokensChanged` | Raised whenever the chip set changes (add or remove). |

### `TokenChipStyle`

| Name | Type | Description |
|---|---|---|
| `BackColor` | `Color?` | Chip fill; `null` uses the default accent tint. |
| `FontStyle` | `FontStyle` | Chip font style, e.g. `Bold` or `Italic`. |
| `ForeColor` | `Color?` | Chip text and × colour; `null` uses the theme text colour. |

## Notes

- **Enter** or **comma** commits the typed text as a chip; **Backspace** on an empty editor deletes the last one; clicking a chip's trailing × removes it.
- The suggestion drop-down is a grabbing popup whose key events are routed back into the hosted editor, so typing keeps filtering (the same pattern [`Breadcrumb`](breadcrumb.md) uses).
- Chips wrap to a new row when the current one runs out; the editor takes the remainder of the last row, dropping to the next when too little is left.

## Differences from WinForms

No equivalent.
