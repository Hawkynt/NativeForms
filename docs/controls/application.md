# Application

> The static entry point: register the backends the build ships, hand `Run` the main form, and it resolves a backend, realizes the control tree, and pumps the native message loop until the window closes.

`Hawkynt.NativeForms.Application` · static class · selects an `IPlatformBackend` via `BackendRegistry`

## Usage

From `NativeForms.Demo/Program.cs`:

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.MacOS;
using Hawkynt.NativeForms.Backends.Windows;

BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());
BackendRegistry.Register(new CocoaBackend());

var form = new Form { Text = "Hello", Bounds = new(0, 0, 320, 160) };
Application.Run(form);
```

Tests bypass the registry and pass a backend directly — `Application.Run(form, new HeadlessBackend())` returns immediately because the headless `Run` does not block.

## API

`Application`:

| Method | Description |
|---|---|
| `Run(Form mainForm)` | Resolves a backend via `BackendRegistry.Resolve()`, realizes and shows `mainForm` (applying its `StartPosition` against the backend's screen size), and blocks in the message loop until it closes |
| `Run(Form mainForm, IPlatformBackend backend)` | Same, on an explicit backend — the seam tests use for the headless backend |
| `Exit()` | Requests the running message loop to exit (forwards to the backend's `Quit`) |

`BackendRegistry` (`Hawkynt.NativeForms.Backends`):

| Member | Description |
|---|---|
| `Register(IPlatformBackend)` | Adds a backend. Idempotent per concrete type — registering the same type twice keeps the first |
| `Registered` | All registered backends, in registration order |
| `Resolve()` | The first registered backend whose `IsSupported` is true; throws `PlatformNotSupportedException` when nothing is registered or nothing matches the OS |
| `Clear()` | Removes every backend. Intended for tests |

## Notes

**Run blocks.** `Run` realizes the form (buffered properties flush into native widgets — see [control.md](control.md)) and then enters the platform loop on the calling thread; it returns when the main window closes or `Exit()` is called. Call it on the thread that built the controls.

**The current backend.** While `Run` is active, its backend is the process-wide *current* backend (an internal seam, cleared when `Run` returns). Components that need a backend after startup resolve it there instead of taking one in their constructor: `Timer` arms its native source against it, and `Form.ShowDialog()` uses it for the modal loop — which is why `ShowDialog` throws `InvalidOperationException` outside a running `Run`. The backend also supplies the primary screen size that `Form.StartPosition` centering is computed against.

**Modal interplay.** `Form.ShowDialog` runs a *nested* native loop inside the one `Run` pumps; the outer loop keeps running underneath and `Run` does not return while a dialog is up. See [form.md](form.md).

**Exit/Quit semantics.** `Application.Exit()` is a *request*: it calls `IPlatformBackend.Quit()` on the backend the current `Run` selected, which posts a quit to the native loop. Before any `Run` (or after it returned), `Exit()` is a harmless no-op. Closing the main window ends the loop on its own — `Exit` is only needed to end it programmatically.

**Threading.** The thread that calls `Run` becomes the UI thread: its id anchors `Control.InvokeRequired`, and a `NativeFormsSynchronizationContext` is installed on it for the duration of the loop — `Post` queues onto the loop, `Send` runs inline on the loop thread or blocks from any other, so `await` continuations inside event handlers resume on the UI thread. From worker threads, marshal through `Control.Invoke(Action)` (blocking, exception-propagating) or `Control.BeginInvoke(Action)` (fire-and-forget) — see [control.md](control.md). `Run` also arms any `Timer` started before the loop existed.

**One binary, many platforms — or trim to one.** Registration is explicit construction, never reflection, so the trimmer sees exactly which backends ship. Register all three and only the backend whose `IsSupported` matches the running OS is ever realized (the macOS entry currently registers but throws on use — the Cocoa backend is a placeholder). Register only `Win32Backend` and a Windows build carries no GTK code at all. Resolution order is registration order: put the preferred backend first.

## Differences from System.Windows.Forms.Application

- `Run` always takes a `Form` — there is no form-less `Application.Run()` overload and no `ApplicationContext`.
- No `DoEvents()` (the loop is never pumped manually), no `Idle` event, no `OpenForms` collection, no `ApplicationExit`/`ThreadExit` events, no `EnableVisualStyles`/`SetHighDpiMode` bootstrap calls.
- `Exit()` only requests the loop to quit; it raises no events and does not close forms individually.
- The Cocoa backend is a placeholder — tracked in [../PRD.md](../PRD.md) §10 (M9).
