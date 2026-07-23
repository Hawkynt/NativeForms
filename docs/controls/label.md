# Label

> A non-interactive line of static text, backed by the platform's native label widget, with WinForms-style `AutoSize`, `TextAlign`, `BorderStyle` and mnemonic rendering.

![Label in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.Label` · strategy: **native** · peer: `ILabelPeer`

## Usage

```csharp
var label = new Label { Text = "&Hello", AutoSize = true, Bounds = new(20, 20, 1, 1) };
form.Controls.Add(label);

label.Text = "Updated"; // forwarded to the native widget, raises TextChanged, re-runs AutoSize
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `AutoSize` | `bool` | `false` | Sizes the label to fit its text in the theme's default font, via the backend's text measurement — on realization and on every `Text` change. Buffered before realization. |
| `TextAlign` | `ContentAlignment` | `TopLeft` | Where the text sits within the bounds. Win32 static controls honor the horizontal component plus a coarse vertical centering only; GTK honors all nine anchors. |
| `BorderStyle` | `BorderStyle` | `None` | `None` or `FixedSingle`. Rendered natively on Win32 (`WS_BORDER`); GTK has no native label frame, so the value is not rendered there. |
| `UseMnemonic` | `bool` | `true` | Whether `&` in `Text` marks the following character as a mnemonic and renders it underlined (`&&` escapes a literal ampersand). Rendering only for now. |
| `Image` | `IImage?` | `null` | The image shown by the label. Rendered natively only while `Text` is empty (Win32 `SS_BITMAP` static, GTK swaps in a `GtkImage`) — no toolkit renders image and text in one static widget, so a captioned label keeps its text and the image stays pending. |
| `ImageAlign` | `ContentAlignment` | `MiddleCenter` | Where the image anchors within the bounds. Advisory for now: the native image-only renderings ignore it (Win32 pins the bitmap top-left, GTK centers it). |

The displayed text is the inherited `Text` property; changes raise `TextChanged`. Inherits the common members of [`Control`](control.md).

## Notes

- The peer is created via `IPlatformBackend.CreateLabel()`; `ILabelPeer` adds the alignment, border, mnemonic and image setters on top of the base `IControlPeer` surface. All settings are buffered before realization and flushed into the fresh widget; changing them afterwards forwards immediately (Win32 recreates the HWND in place where a creation-time style demands it).
- `Text` is normalized: assigning `null` stores `string.Empty`, and `TextChanged` fires only on actual change.
- `AutoSize` measures through the canvas-free `IPlatformBackend.MeasureText`, so it works headlessly — `LabelTests` pin the resizing on realization, on text change, and when enabled late.
- A common MVVM pattern binds a view-model property onto `Text` with a one-way `PropertyBinding<T>` — see `NativeForms.Demo/MainForm.cs`.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): mnemonic *activation* (focusing the next control — blocked on the §7.1 focus model) and a combined image + text rendering (platform-limited, see `Image` above).
