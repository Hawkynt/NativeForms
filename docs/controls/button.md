# Button

> A push button backed by the platform's native button widget — it looks and behaves exactly like every other button on the user's desktop.

`Hawkynt.NativeForms.Button` · strategy: **native** · peer: `IButtonPeer`

## Usage

```csharp
var button = new Button { Text = "Click me", Bounds = new(20, 64, 140, 36) };
button.Click += (_, _) => button.Text = "Clicked!";
form.Controls.Add(button);
```

## API

`Button` adds no members of its own. Inherits the common members of [`Control`](control.md). Native activation — mouse click, Space, Enter — raises the inherited `Click` event; `PerformClick()` raises it programmatically.

## Notes

- The peer is created via `IPlatformBackend.CreateButton()`; the control wires the peer's `Clicked` event to `Click` on realization.
- `Text`, `Bounds`, `Enabled` and `Visible` set before realization are buffered and flushed into the native widget when it is created; writes afterwards forward immediately.
- Testable headlessly: the test backend's button peer can raise `Clicked` without a display.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): `DialogResult`, default/accept styling, `FlatStyle`, and image + text (`Image`, `ImageAlign`, `TextImageRelation`).
