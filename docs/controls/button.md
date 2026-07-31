# Button

> A push button backed by the platform's native button widget — it looks and behaves exactly like every other button on the user's desktop, and owner-drawn in that platform's own theme for the faces no platform button can express.

![Button in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.Button` · strategy: **native, with an owner-drawn half** ([§12](../PRD.md)) · peer: `IButtonPeer`

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
| `Command` | `ICommand?` | `null` | The MVVM command a click executes, passing `CommandParameter`. Attaching one tracks `CanExecute` to drive the button's `Enabled` (and follows the command's `CanExecuteChanged`), delegate-based and reflection-free. |
| `CommandParameter` | `object?` | `null` | The argument passed to `Command.Execute`/`CanExecute`. |
| `DialogResult` | `DialogResult` | `None` | The verdict a click reports to the owning [`Form`](form.md). Anything other than `None` makes a click set `Form.DialogResult`, which closes the form when it is shown modally — the WinForms dialog contract. |
| `FlatStyle` | `FlatStyle` | `Standard` | How the face is drawn. `Standard` and `System` are the platform's own button and keep the widget — the same thing here, since this toolkit never draws over a native one. `Flat` and `Popup` are faces no platform button offers, so they are painted: flat carries no frame, popup grows one under the pointer, and a press frames either. |
| `Image` | `IImage?` | `null` | The image on the button face, or `null` for text-only. An image on its own stays on the widget; an image **beside a caption** is the promotion gate — see below. |
| `ImageAlign` | `ContentAlignment` | `MiddleCenter` | Where the image anchors within the face. Honoured exactly by the painted face; advisory on the widget, which offers no free placement on any backend but is handed the value so a capable one can use it. |
| `TextImageRelation` | `TextImageRelation` | `ImageBeforeText` | How image and text share the face. Honoured exactly by the painted face — the half that draws whenever there is both an image and a caption to arrange. On the widget it is advisory: GTK maps the four directional values onto the button's image position (`Overlay` renders as `ImageBeforeText`), Win32 offers no placement control at all. |

Inherits the common members of [`Control`](control.md). Native activation — mouse click, Space,
Enter — raises the inherited `Click` event; `PerformClick()` raises it programmatically.

## Notes

- The peer is created via `IPlatformBackend.CreateButton()`; the control wires the peer's `Clicked` event to `Click` on realization.
- `Text`, `Bounds`, `Enabled` and `Visible` set before realization are buffered and flushed into the native widget when it is created; writes afterwards forward immediately. The image triple (`Image`, `ImageAlign`, `TextImageRelation`) is buffered the same way and forwarded to the peer as one `SetImage` call.
- **The promotion gate, and who decides it.** An image *on its own* keeps the widget: every platform centres a bare bitmap on a button (GTK `gtk_button_set_image`, Win32 `BM_SETIMAGE`/`BS_BITMAP`, AppKit's image cell). An image **with a caption beside it** is put to the backend (`IPlatformBackend.ButtonRendersImageWithText`) rather than decided by one rule for all three, because the widget is the faster path wherever it can render the face: GTK places the image beside the label and AppKit has `imagePosition`, so both keep the widget; a classic Win32 `BUTTON` renders the bitmap alone and drops the caption, so only there does the button fall back to the painted face. `FlatStyle.Flat` and `Popup` are painted everywhere, since no platform button offers either. The swap runs both ways on a live control and is invisible to the application; `IsNativeWidget` is the only way to tell.
- **So the same button can take different halves on different desktops** — which is the point, not a wrinkle. Two native buttons already look different on two desktops; making one give up a rendering it has, to match one that does not, would buy a uniformity this toolkit never promised and cost the platform behaviour it does.
- **The painted half carries what the widget was doing for it**: focus ring, pressed face, the default-button accent that says which button Enter works, mnemonic underline, Space and Enter on the key *release* (a held key must not auto-repeat), a press cancelled by sliding off the face, and the `DialogResult` walk to the owning form. The frame is rounded to `ITheme.ButtonCornerRadius` — read from the desktop, so a painted face does not give itself away beside a real one.
- **Default/accept styling.** `Form.AcceptButton` marks the button on its peer (`IButtonPeer.SetDefault`), painted by the platform (Win32 `BS_DEFPUSHBUTTON`, GTK `gtk_widget_grab_default` once the window chain is ready — the emphasis is theme-dependent) and by the painted half as the accent frame.
- Testable headlessly: the test backend's button peer can raise `Clicked` without a display and records the forwarded image triple; the painted half is asserted through the recording canvas.
