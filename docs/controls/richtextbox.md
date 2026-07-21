# RichTextBox

> A rich-text editor: a [`TextBox`](textbox.md) (always multiline) whose selection can carry character styles, color and size, whose paragraphs can be aligned and bulleted, and whose whole document round-trips as RTF — backed by the platform's rich editor so editing, caret and clipboard behave natively.

`Hawkynt.NativeForms.RichTextBox` · strategy: **native** (Win32 `RICHEDIT50W`, GTK tagged `GtkTextView`) · peer: `IRichTextBoxPeer`

## Usage

```csharp
var editor = new RichTextBox { Bounds = new(20, 20, 400, 240) };
editor.LinkClicked += (_, e) => Console.WriteLine(e.LinkText);
form.Controls.Add(editor);

editor.Text = "hello world";
editor.SelectionStart = 6;
editor.SelectionLength = 5;
editor.SelectionBold = true;                 // formats "world" in the live widget
editor.SelectionColor = Color.FromArgb(255, 32, 64, 128);

var rtf = editor.Rtf;                        // the whole document as RTF
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `SelectionBold` / `SelectionItalic` / `SelectionUnderline` / `SelectionStrikeout` | `bool` | `false` | Whether the current selection carries the style. Writing formats the selection. |
| `SelectionColor` | `Color` | `Color.Empty` | Text color of the current selection; `Color.Empty` restores the default. |
| `SelectionFontSize` | `float` | `0` | Font size of the current selection, in points; 0 means the default. |
| `SelectionAlignment` | `ContentAlignment` | `TopLeft` | Alignment of the paragraphs the selection touches. Only the horizontal component (left/center/right) is meaningful. |
| `SelectionBullet` | `bool` | `false` | Whether the paragraphs the selection touches are bulleted list items. |
| `DetectUrls` | `bool` | `true` | Whether URLs in the text are detected, rendered as links and raise `LinkClicked`. |
| `ZoomFactor` | `float` | `1.0` | Display scale of the text — a pure view setting, not part of the document. Buffered before realization. |
| `Rtf` | `string` | — | The whole document as RTF (the NativeForms subset). Buffered before realization; afterwards both directions go straight through the native widget. |

### Events

| Name | Description |
|---|---|
| `LinkClicked` | Raised when the user activates an auto-detected link; `LinkClickedEventArgs.LinkText` carries the URL as it appears in the document. |

Inherits the members of [`TextBox`](textbox.md) and the common members of [`Control`](control.md). The constructor sets `Multiline = true`; assigning `Text` discards any RTF buffered for realization — last writer wins.

## Notes

- **`Selection…` properties are write-through commands, not state.** Setting one formats whatever the widget has selected *right now*; the getters return the last value written, not the format under the caret (reading mixed-selection state back is not part of the peer contract). They only take effect while the control is realized — there is no meaningful selection formatting to buffer into a widget that does not exist yet. `Rtf` *is* buffered: assigned before realization it is pushed into the fresh widget after the plain `Text`, so the richer of the two wins.
- **The RTF subset** (`Hawkynt.NativeForms.Text.RtfSerializer` over the platform-neutral `RichDocument` model): character styles (bold/italic/underline/strikeout), text color, font size, paragraph alignment and bullets over plain text runs. The writer emits plain standard RTF (WordPad and Word open it); the reader is a tolerant subset parser that skips unknown control words, so it also digests the richer documents a native Win32 rich edit produces. Fonts are not part of the subset — everything maps to the single default font. `RtfSerializerTests` pin the round trip, escaping and merging.
- Setting `Rtf` on a realized control hands the string to the widget, which reports the resulting plain text back like a user edit — `Text` and `TextChanged` follow. On unrealization the peer's last RTF is captured so a re-realized box keeps its formatting instead of flattening to plain text.
- **GTK limitations, all documented in the peer.** GTK has no native RTF, so both directions round-trip through the core serializer; bullets are a literal `"• "` paragraph prefix (GTK text views have no list model), so they are part of the reported text; paragraph alignment is a text tag over the paragraph's characters, which an empty paragraph cannot hold; URL detection re-tags `http(s)://…` and `www.…` tokens after every change. Placeholder, password masking and max length remain single-line-entry features and are no-ops here.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): `PlaceholderText` rendering (multiline), a per-selection font family (`SelectionFont`), paragraph indent, and `LoadFile`/`SaveFile`.

## Differences from System.Windows.Forms.RichTextBox

- **No `SelectionFont`.** WinForms formats the selection by assigning a whole `Font`; here the individual `SelectionBold`/`SelectionItalic`/`SelectionUnderline`/`SelectionStrikeout`/`SelectionFontSize` properties do it piecewise — a font *family* per selection is not part of the RTF subset yet.
- **`SelectionAlignment` is a `ContentAlignment`**, not WinForms' `HorizontalAlignment`; only its horizontal component is meaningful.
- **The `Selection…` getters return the last value written**, not the state under the caret — they are write-through commands, effective only while realized (see Notes).
- `LinkClicked` matches WinForms; there is no `SelectionIndent`/`SelectionHangingIndent`, no `Find`, no `LoadFile`/`SaveFile` yet.
