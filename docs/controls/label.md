# Label

> A non-interactive line of static text, backed by the platform's native label widget.

`Hawkynt.NativeForms.Label` · strategy: **native** · peer: `ILabelPeer`

## Usage

```csharp
var label = new Label { Text = "Hello", Bounds = new(20, 20, 280, 24) };
form.Controls.Add(label);

label.Text = "Updated"; // forwarded to the native widget, raises TextChanged
```

## API

`Label` adds no members of its own. Inherits the common members of [`Control`](control.md). The displayed text is the inherited `Text` property; changes raise `TextChanged`.

## Notes

- The peer is created via `IPlatformBackend.CreateLabel()`; `ILabelPeer` adds nothing beyond the base `IControlPeer` surface (bounds, text, visibility, enabled).
- `Text` is normalized: assigning `null` stores `string.Empty`, and `TextChanged` fires only on actual change.
- A common MVVM pattern binds a view-model property onto `Text` with a one-way `PropertyBinding<T>` — see `NativeForms.Demo/MainForm.cs`.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): `AutoSize`, `TextAlign`, `BorderStyle`, mnemonics, and `Image` + `ImageAlign`.
