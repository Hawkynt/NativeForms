# Label

> A non-interactive line of static text, backed by the platform's native label widget, with WinForms-style `AutoSize`, `TextAlign`, `BorderStyle`, mnemonic rendering and an image beside the caption.

![Label in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.Label` · strategy: **native, owner-drawn when it carries an `Image`** · peer: `ILabelPeer` or `ICanvasPeer`

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
| `BorderStyle` | `BorderStyle` | `None` | `None` or `FixedSingle`. Rendered natively on Win32 (`WS_BORDER`); GTK has no native label frame, so the value is not rendered there. |
| `Image` | `IImage?` | `null` | The image shown beside the caption. Assigning one moves the label off the platform static and onto the painter (no platform static renders a bitmap and a caption together); clearing it moves the label back. The swap keeps every property. |
| `ImageAlign` | `ContentAlignment` | `MiddleCenter` | Where the image anchors when it is the only content — a caption-less label places its image by this rather than by `TextAlign`, matching Windows Forms. |
| `TextImageRelation` | `TextImageRelation` | `ImageBeforeText` | Which side of the caption the image takes. Mirrored when the label reads right to left. |
| `TextAlign` | `ContentAlignment` | `TopLeft` | Where the text sits within the bounds. Win32 static controls honor the horizontal component plus a coarse vertical centering only; GTK honors all nine anchors. |
| `UseMnemonic` | `bool` | `true` | Whether `&` in `Text` marks the following character as a mnemonic and renders it underlined (`&&` escapes a literal ampersand). Alt+mnemonic focuses the next tab stop after the label. |

The displayed text is the inherited `Text` property; changes raise `TextChanged`. Inherits the common members of [`Control`](control.md).

## Notes

- The peer is `IPlatformBackend.CreateLabel()` while the label has no image and a canvas once it has one; `IsNativeWidget` says which. `UseNativeWidget = false` pins the label to the painter whatever it is showing. The gate is the image alone rather than image-with-a-caption: the image-only rendering the three widgets *do* offer is three different placements, and the same control has to look the same on every backend.
- `ILabelPeer` adds `ILabelPeer` adds the alignment, border, mnemonic and image setters on top of the base `IControlPeer` surface. All settings are buffered before realization and flushed into the fresh widget; changing them afterwards forwards immediately (Win32 recreates the HWND in place where a creation-time style demands it).
- `Text` is normalized: assigning `null` stores `string.Empty`, and `TextChanged` fires only on actual change.
- `AutoSize` measures through the canvas-free `IPlatformBackend.MeasureText`, so it works headlessly — `LabelTests` pin the resizing on realization, on text change, and when enabled late.
- A common MVVM pattern binds a view-model property onto `Text` with a one-way `PropertyBinding<T>` — see `NativeForms.Demo/MainForm.cs`.
- The painted half draws the caption through the shared [`ContentLayout`](../PRD.md) geometry `Button`, `CheckBox` and `GroupBox` use, with the mnemonic underlined and the ampersand removed — mark-up, not a glyph, so it is stripped before measuring as well as before drawing.
- Nothing here is pending; see [docs/PRD.md](../PRD.md) §7.3.
