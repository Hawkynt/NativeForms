# CodeTextBox

> An owner-drawn multi-line code surface: a line-number gutter, a current-line highlight, tab/auto-indent handling, a pluggable delegate tokenizer that colours keyword/string/comment/number spans, and an optional completion drop-down. The editor an IDE is built on — [`RichTextBox`](richtextbox.md) only covers flat RTF.

![CodeTextBox in the NativeForms demo](../screenshots/29-editors.png)

`Hawkynt.NativeForms.CodeTextBox` · strategy: **owner-drawn + completion popup** · peer: `ICanvasPeer`

## Usage

```csharp
var editor = new CodeTextBox { Bounds = new(0, 0, 580, 380), TabWidth = 4 };
editor.Tokenizer = line => Tokenize(line);                       // -> IReadOnlyList<CodeToken>
editor.CompletionProvider = prefix => Keywords.Where(k => k.StartsWith(prefix)).ToArray();
editor.Text = File.ReadAllText(path);
form.Controls.Add(editor);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `CaretColumn` | `int` | `0` | The caret's zero-based column. |
| `CaretLine` | `int` | `0` | The caret's zero-based line. |
| `CompletionProvider` | `Func<string, IReadOnlyList<string>>?` | `null` | Produces candidates for the identifier prefix before the caret. Invoked on Ctrl+Space and as an identifier is typed; return an empty list to close the list. |
| `HighlightCurrentLine` | `bool` | `true` | Whether the caret's line is tinted. |
| `Lines` | `IReadOnlyList<string>` | one empty line | The document lines. |
| `ShowLineNumbers` | `bool` | `true` | Whether the gutter is drawn. |
| `TabWidth` | `int` | `4` | Tab stop width in spaces; Tab inserts this many. Clamped to 1–16. |
| `Text` | `string` | empty | The whole document. Getting joins lines with `\n`; setting splits on newlines and resets the caret. |
| `Tokenizer` | `Func<string, IReadOnlyList<CodeToken>>?` | `null` | Splits one line into coloured spans. Called per visible line per paint — keep it cheap. |

### Methods

| Name | Description |
|---|---|
| `ShowCompletion()` | Opens or refilters the completion list for the identifier before the caret. |

### `CodeToken`

A span within a single line: `Start`, `Length` and a `CodeTokenKind` of `Comment`, `Keyword`, `Number`, `Plain`, `String` or `Type`, each mapped to a colour.

## Notes

- Editing keys: arrows/Home/End/PageUp/PageDown (with Shift to extend the selection), Ctrl+A, Enter (splits and copies the leading indent), Tab (inserts spaces), Backspace and Delete (merging lines at the boundaries).
- **Tab and Enter are claimed via `IsInputKey`**, so the form's dialog-key chain does not steal Tab for focus navigation or Enter for the accept button.
- The caret, selection and coloured spans are positioned by **measured** substring widths, not a monospace estimate, so they stay aligned under a proportional font.
- The completion list is a grabbing popup whose keys are routed back into the editor; Enter or Tab accepts, Escape closes, and typing keeps filtering.

## Differences from WinForms

No equivalent; `RichTextBox` is the nearest and offers no gutter, tokenizer or completion.

## Not yet implemented

See [docs/PRD.md](../PRD.md) §7.10: undo/redo, find & replace, bracket matching, code folding, and word wrap.
