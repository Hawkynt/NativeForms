# Button

> A push button backed by the platform's native button widget — it looks and behaves exactly like every other button on the user's desktop.

`Hawkynt.NativeForms.Button` · strategy: **native** · peer: `IButtonPeer`

## Usage

```csharp
var button = new Button { Text = "Click me", Bounds = new(20, 64, 140, 36) };
button.Click += (_, _) => button.Text = "Clicked!";
form.Controls.Add(button);

// A dialog button: clicking sets the owning form's DialogResult, closing it when modal.
var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Image` | `IImage?` | `null` | The image on the button face, or `null` for text-only. Rendered natively — see the platform limits below. |
| `ImageAlign` | `ContentAlignment` | `MiddleCenter` | Where the image anchors within the face. Advisory for now: neither the Win32 nor the GTK button offers free image placement, so no backend renders it; the value is forwarded so a capable backend can honor it. |
| `TextImageRelation` | `TextImageRelation` | `ImageBeforeText` | How image and text share the face. GTK honors the four directional values through the button's image position (`Overlay` renders as `ImageBeforeText`); Win32 push buttons offer no placement control. |
| `DialogResult` | `DialogResult` | `None` | The verdict a click reports to the owning [`Form`](form.md). Anything other than `None` makes a click set `Form.DialogResult`, which closes the form when it is shown modally — the WinForms dialog contract. |

Inherits the common members of [`Control`](control.md). Native activation — mouse click, Space,
Enter — raises the inherited `Click` event; `PerformClick()` raises it programmatically.

## Notes

- The peer is created via `IPlatformBackend.CreateButton()`; the control wires the peer's `Clicked` event to `Click` on realization.
- `Text`, `Bounds`, `Enabled` and `Visible` set before realization are buffered and flushed into the native widget when it is created; writes afterwards forward immediately. The image triple (`Image`, `ImageAlign`, `TextImageRelation`) is buffered the same way and forwarded to the peer as one `SetImage` call.
- **Image platform limits.** GTK shows image and text side by side. A plain Win32 button (`BM_SETIMAGE`/`BS_BITMAP`) renders the bitmap alone while `Text` is empty and needs themed common controls (a visual-styles manifest) to draw image and text together — with classic rendering a captioned button keeps its text only. An owner-drawn image+text fallback is a tracked follow-up.
- Testable headlessly: the test backend's button peer can raise `Clicked` without a display and records the forwarded image triple.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): default/accept styling, `FlatStyle`, and the owner-drawn image+text fallback.
