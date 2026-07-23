# TextBox

> A text input box backed by the platform's native editor — a Win32 `EDIT`, a `GtkEntry`/`GtkTextView` — so caret, selection, clipboard and IME behave exactly like every other text field on the user's desktop.

![TextBox in the NativeForms demo](../screenshots/02-input.png)

`Hawkynt.NativeForms.TextBox` · strategy: **native** · peer: `ITextBoxPeer`

## Usage

```csharp
var box = new TextBox
{
    PlaceholderText = "Name",
    MaxLength = 40,
    Bounds = new(20, 20, 200, 24),
};
box.TextChanged += (_, _) => Console.WriteLine(box.Text);
form.Controls.Add(box);

box.SelectionStart = 0;
box.SelectionLength = 4;      // select the first four characters
box.SelectedText = "Jane";    // replace them, caret lands after the insertion
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Multiline` | `bool` | `false` | Multiline editor with vertical scrolling instead of a single-line entry. May recreate the native widget (see Notes). |
| `PlaceholderText` | `string` | `""` | Greyed hint shown while the box is empty. Single-line only on most platforms. |
| `PasswordChar` | `char` | `'\0'` | Masks the displayed text with this character; `'\0'` turns masking off. |
| `UseSystemPasswordChar` | `bool` | `false` | Masks with the platform's standard glyph (`●`), overriding `PasswordChar`. |
| `ReadOnly` | `bool` | `false` | Text can be selected and copied but not edited. |
| `MaxLength` | `int` | `0` | Maximum characters the user can type; 0 means unlimited. Negative values coerce to 0. |
| `CharacterCasing` | `CharacterCasing` | `Normal` | Forces `Upper`/`Lower` casing. Changing it re-cases the current `Text`; while active, programmatic writes and user input are normalized alike. |
| `AcceptsReturn` | `bool` | `false` | Whether a multiline box keeps Enter (a newline) instead of activating the form's `AcceptButton`. Wired through the peer key seam. |
| `AcceptsTab` | `bool` | `false` | Whether the box keeps an unmodified Tab (a tab character) instead of moving focus. Wired through the peer key seam. |
| `SelectionStart` | `int` | `0` | Index of the first selected character (the caret position when nothing is selected). Buffered before realization, read live from the widget afterwards. |
| `SelectionLength` | `int` | `0` | Number of selected characters. Buffered/live like `SelectionStart`. |
| `SelectedText` | `string` | `""` | The selected run of `Text`; assigning replaces the selection and places the caret after the inserted text. |

### Methods

| Name | Description |
|---|---|
| `Select(int start, int length)` | Selects the given run (negative values clamp to zero); buffered until realization like the `Selection*` setters. |
| `SelectAll()` | Selects the whole content. |
| `Clear()` | Empties the box, raising `TextChanged` when it held text. |
| `AppendText(string text)` | Appends to the content and parks the caret at the end — the classic log-window helper. |

The inherited `Text` property is overridden: assigned text is normalized by `CharacterCasing` and pushed to the widget; user edits flow back from the peer and raise `TextChanged` exactly once — the peer's echo of a programmatic write never raises a second event.

Inherits the common members of [`Control`](control.md).

## Notes

- Every setting is buffered until realization and flushed into the peer when the native widget is created; writes afterwards forward immediately. `TextBoxTests` pin the whole surface headlessly against the test backend's text-box peer.
- **Multiline recreates the widget.** Win32 `ES_MULTILINE` is a creation-time style, so flipping `Multiline` on a live control destroys and recreates the HWND (same control id, so `WM_COMMAND` routing survives); GTK swaps between a `GtkEntry` and a `GtkTextView` in a `GtkScrolledWindow`. Peers buffer their state and re-flush it into the fresh widget, so the swap is invisible — text and selection survive.
- **Platform limits, documented honestly.** Multiline placeholders are owner-drawn: neither Win32's cue banner (`EM_SETCUEBANNER`, single-line `EDIT` only) nor `GtkTextView` offers a native one, so on GTK the hint is painted after the view's own draw while the buffer is empty. The Win32 multiline half is not wired yet. `MaxLength` and password masking are also entry-only on GTK (`GtkTextView` has no native limit or masking).
- `CharacterCasing` is normalized in the core — on assignment and on user input alike (a corrective push rewrites the widget when it disagrees) — so it behaves identically on every backend; no `ES_UPPERCASE`/`ES_LOWERCASE` style bits.
- [`MaskedTextBox`](maskedtextbox.md) and [`RichTextBox`](richtextbox.md) build on this control; [`SearchBox`](searchbox.md), [`ComboBox`](combobox.md) (editable style) and the spinners host one as their editor.
- **Key preview.** A native text box previews its keys through the form's dialog-key chain over the `ITextBoxPeer.KeyDown` seam: Enter reaches the `AcceptButton`, Escape the `CancelButton`, Tab/Shift+Tab navigate, and menu shortcuts fire — unless `AcceptsReturn`/`AcceptsTab` keep the key for a multiline editor.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): the Win32 half of the multiline placeholder (GTK is done), word-wrap control, and an undo API.

## Differences from System.Windows.Forms.TextBox

- **`MaxLength` defaults to `0` = unlimited** — WinForms defaults to 32767. Same property name, different default and sentinel.
- **No `Modified` flag, no undo** (`Undo`/`CanUndo`/`ClearUndo`), **no `ScrollBars`** (a multiline box always scrolls vertically), **no `Lines` array** and no `WordWrap` toggle.
- `Select`/`SelectAll`/`Clear`/`AppendText` and the `Selection*` trio match WinForms; `AcceptsReturn`/`AcceptsTab` steer Enter/Tab through the peer key seam (see above).
