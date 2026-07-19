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
| `Run(Form mainForm)` | Resolves a backend via `BackendRegistry.Resolve()`, realizes and shows `mainForm`, and blocks in the message loop until it closes |
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

**Exit/Quit semantics.** `Application.Exit()` is a *request*: it calls `IPlatformBackend.Quit()` on the backend the current `Run` selected, which posts a quit to the native loop. Before any `Run`, `Exit()` is a harmless no-op. Closing the main window ends the loop on its own — `Exit` is only needed to end it programmatically.

**One binary, many platforms — or trim to one.** Registration is explicit construction, never reflection, so the trimmer sees exactly which backends ship. Register all three and only the backend whose `IsSupported` matches the running OS is ever realized (the macOS entry currently registers but throws on use — the Cocoa backend is a placeholder). Register only `Win32Backend` and a Windows build carries no GTK code at all. Resolution order is registration order: put the preferred backend first.

**Not yet implemented.** UI-thread affinity helpers (`Control.Invoke`/`BeginInvoke`, `SynchronizationContext`) are planned — [../PRD.md](../PRD.md) §8; the Cocoa backend is tracked in §10 (M9).
