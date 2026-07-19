# Timer

> A recurring timer whose `Tick` fires on the UI thread, driven by the platform message loop — the moral equivalent of `System.Windows.Forms.Timer`. Marquee progress, caret blink, tooltip delays and key autorepeat are all built on it.

`Hawkynt.NativeForms.Timer` · not a control — a `sealed class : IDisposable` over an `ITimerPeer` (`WM_TIMER` on Win32, `g_timeout` on GTK)

## Usage

```csharp
var timer = new Timer { Interval = 250 };
timer.Tick += (_, _) => label.Text = DateTime.Now.ToString("HH:mm:ss");
timer.Start(); // == Enabled = true
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Interval` | `int` | `100` | The tick period in milliseconds. Throws `ArgumentOutOfRangeException` below 1. Setting it while the timer runs restarts the period at the new value. |
| `Enabled` | `bool` | `false` | Whether the timer is ticking. Setting the same value again is a no-op (no peer restart). |

### Methods

| Name | Description |
|---|---|
| `Start()` | Identical to `Enabled = true`. |
| `Stop()` | Identical to `Enabled = false`. |
| `Dispose()` | Stops the timer and releases the native timer source. |

### Events

| Name | Description |
|---|---|
| `Tick` | Raised on the UI thread every `Interval` milliseconds while `Enabled`. Sender is the timer, args is `EventArgs.Empty` — the forwarding path allocates nothing. |

## Notes

- **Deferred arm**: the native timer source comes from the backend the application runs on, so a timer enabled before `Application.Run` has started cannot tick yet — the enabled wish is remembered and the source is armed on the next `Enabled`, `Interval` or `Start` touch while the loop is running, which is exactly when a handler inside that loop first pokes the timer.
- **Interval restart**: assigning `Interval` while enabled re-starts the peer at the new period immediately; while stopped it only stores the value and creates nothing.
- The native peer is created once on first arm and reused across `Start`/`Stop` cycles; `Dispose` unhooks and releases it.
- `TimerTests` pin the default and validation, start/stop mirroring `Enabled`, tick forwarding, the running restart vs. the stopped no-op, idempotent enabling, peer reuse, dispose, the deferred arm through a real `Application.Run` and a zero-allocation tick path.
- Done per [docs/PRD.md](../PRD.md) §7.1 (fireable headless fake included for tests); no open items.
