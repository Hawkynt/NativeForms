# InfoBar & Toast

> An owner-drawn inline message strip — severity stripe and icon, bold title, message, optional action link and close button — plus `Toast`, which floats transient copies in the form's bottom-right corner. The in-window notification surface WinForms never had; its only equivalent is the OS tray [`NotifyIcon`](notifyicon.md).

![InfoBar in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.InfoBar` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var bar = new InfoBar
{
    Bounds = new(16, 164, 500, 40),
    Severity = InfoBarSeverity.Warning,
    Title = "Heads up",
    Message = "Unsaved changes will be lost.",
    ActionText = "Save",
};
bar.ActionClicked += (_, _) => Save();
bar.Closed += (_, _) => form.Controls.Remove(bar);
form.Controls.Add(bar);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `ActionText` | `string` | empty | An action link shown before the close button; empty hides it. |
| `Message` | `string` | empty | The message text following the title. |
| `Opacity` | `double` | `1.0` | Fade level in `[0, 1]`, used by the `Toast` animation. Every drawn colour blends toward the parent background as it drops, so it fades without needing backend alpha compositing. |
| `Severity` | `InfoBarSeverity` | `Info` | `Info` (accent), `Success` (green), `Warning` (amber) or `Error` (red). Sets the stripe, disc icon and tint. |
| `ShowCloseButton` | `bool` | `true` | Whether the trailing × is drawn. |
| `Title` | `string` | empty | The bold leading title. |

### Events

| Name | Description |
|---|---|
| `ActionClicked` | Raised when the action link is clicked. |
| `Closed` | Raised after the × hides the bar. The host decides whether to remove it. |

## Toast

`Toast` is a static helper that shows transient `InfoBar`s stacked in the form's bottom-right corner.

| Name | Description |
|---|---|
| `Toast.Show(form, title, message, severity = Info, durationMs = 3000)` | Fades a toast in above any live ones; it collapses and fades out when the duration elapses or its × is clicked. |

## Notes

- Toasts stack upward, newest at the bottom. The stack is **capped to what the form's height fits**: showing more retires the oldest immediately, so the column never climbs out of the client area.
- A toast exits by collapsing toward the bottom edge while fading, never by sliding past the client area — moving a child outside the form would otherwise grow the layout.
- `Error` uses the same red as the shared warning glyph, so severity reads consistently against the rest of the toolkit.

## Differences from WinForms

No equivalent. `NotifyIcon.ShowBalloonTip` is the closest, and that is an OS tray balloon rather than an in-window surface.

## Not yet implemented

See [docs/PRD.md](../PRD.md) §7.10: per-toast action buttons and a queue that defers rather than retires when the stack is full.
