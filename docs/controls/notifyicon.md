# NotifyIcon

> A system-tray icon, the moral equivalent of `System.Windows.Forms.NotifyIcon`: an ARGB icon and
> hover text in the shell's notification area, with click and double-click events.

`Hawkynt.NativeForms.NotifyIcon` · strategy: **native** (Win32 `Shell_NotifyIconW`) · peer:
`INotifyIconPeer`

## Usage

```csharp
var icon = new NotifyIcon { Text = "Sync running" };
icon.SetIcon(16, 16, argbPixels);            // raw 32-bit ARGB, row-major, no decoder
icon.DoubleClick += (_, _) => form.Visible = true;
icon.Visible = true;                         // first show creates the native peer
// …
icon.Dispose();                              // removes the icon, releases the peer
```

## API

| Member | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | The hover text the shell shows next to the icon. |
| `Visible` | `bool` | `false` | Whether the icon sits in the tray. The first show creates the native peer and flushes the buffered icon and text into it. |
| `SetIcon(int width, int height, ReadOnlySpan<int> argb)` | method | | Replaces the icon from 32-bit ARGB pixels (row-major, length = width × height). Throws `ArgumentOutOfRangeException` for non-positive dimensions, `ArgumentException` when the pixel count does not match. |
| `Click` | event | | Raised when the user clicks the icon with the primary button. |
| `DoubleClick` | event | | Raised when the user double-clicks the icon with the primary button. |
| `Dispose()` | method | | Removes the icon from the tray and releases the native peer. |

`NotifyIcon` is a component (`IDisposable`), not a control — it has no bounds, no parent and no
window.

## Notes

- **Buffered until first shown.** Icon pixels and text set before the first `Visible = true` are
  kept in managed state and flushed into the peer on realization; afterwards changes forward
  straight to the shell. Setting `Visible = true` without a running application loop keeps the wish
  — the peer is created when the property is touched while one runs. Hiding keeps the peer for the
  next show.
- **Windows only, honestly.** The Windows backend implements the peer over `Shell_NotifyIconW`
  with a message-only callback window. The GTK backend **throws `NotSupportedException`** from
  `CreateNotifyIcon`: `GtkStatusIcon` is deprecated and ignored by many desktops, and the
  StatusNotifier (D-Bus) replacement is a tracked follow-up ([docs/PRD.md](../PRD.md) §7.7).
  Failing is more honest than adding an icon no shell will show.
- The decoder-free ARGB pixel contract matches the `ImageList` pipeline — no image codecs, no
  `System.Drawing.Common`.
- Testable headlessly: an internal constructor binds the icon to an explicit backend, and
  `NotifyIconTests` pin buffering, forwarding, validation, shell-event forwarding and disposal
  against the test backend's recording peer.
