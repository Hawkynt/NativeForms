# SearchBox

> A search field: a hosted native [`TextBox`](textbox.md) framed by an owner-drawn surface that paints a magnifier glyph at the left and, while text is present, a clear (×) zone at the right — clicking it empties the box and raises `SearchCleared`.

`Hawkynt.NativeForms.SearchBox` · strategy: **owner-drawn** (frame over a hosted native `TextBox`) · peer: `ICanvasPeer` + `ITextBoxPeer`

## Usage

```csharp
var search = new SearchBox { Bounds = new(20, 20, 150, 24) };
search.TextChanged += (_, _) => Filter(search.Text);
search.SearchCleared += (_, _) => Filter(string.Empty);
search.SearchCommitted += (_, _) => RunSearch(search.Text);
form.Controls.Add(search);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `PlaceholderText` | `string` | `"Search"` | Greyed hint shown while the box is empty, forwarded to the hosted editor. |

The inherited `Text` property is the hosted editor's content; user edits flow back into it and raise `TextChanged`.

### Events

| Name | Description |
|---|---|
| `SearchCleared` | Raised after a click on the clear (×) zone emptied the box. Clicking while already empty does nothing. |
| `SearchCommitted` | Raised when Enter commits the search, whether the caret is in the hosted editor or on the painted surface. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- Built like the `UpDownBase` spinners: the native editor fills the field so caret, selection, clipboard and IME stay platform-native. The editor sits between a 20 px leading zone (magnifier: stroked lens circle plus handle) and a 20 px trailing clear zone; the × strokes paint only while text is present. Everything uses the platform `ITheme` colors, greyed when disabled.
- Clearing rewrites both the control and the native editor and raises `SearchCleared` plus one `TextChanged`. A disabled box ignores the clear zone.
- **Enter reaches the composite.** `SearchCommitted` fires for Enter typed inside the hosted native editor as well as on the painted surface: `ITextBoxPeer.KeyDown` lets the editor offer a key to its owner before acting on it, and `SearchBox` claims Enter there. Keys it does not claim stay with the editor.
- `SearchBoxTests` pin the surface headlessly: editor placement, glyph painting, clear behavior, Enter commit, and the disabled state.
