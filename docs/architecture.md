# Architecture

> Core never calls a native API: it drives per-control **peers** through `IPlatformBackend`, buffers state until realization, and pumps the platform message loop through `Application.Run` — one backend project per windowing system.

## Usage

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.Windows;

BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());

var form = new Form { Text = "Hello", Bounds = new(0, 0, 320, 160) };
Application.Run(form);
```

`Application.Run(Form)` resolves a backend from the registry, realizes the form's control tree into native widgets, and blocks in the platform message loop until the window closes.

## The core/peer split

`NativeForms.Core` (`Hawkynt.NativeForms`) contains every public control, the event model, and the data-binding primitives — and zero native code. The only thing it knows about a platform is the contract in `Hawkynt.NativeForms.Backends`:

- `IPlatformBackend` — the factory. One implementation per windowing system; each creates peers (`CreateWindow`, `CreateButton`, `CreateLabel`, `CreateCanvas`), builds images (`CreateImage`), exposes the OS theme (`Theme`), and owns the message loop (`Run`/`Quit`).
- **Peers** (`IControlPeer`, `IWindowPeer`, `IButtonPeer`, `ILabelPeer`, `ICanvasPeer`) — the native side of a single control. A peer owns one platform widget (an HWND, a GtkWidget*) and exposes only what the core needs to keep it in sync: `SetBounds`, `SetText`, `SetVisible`, `SetEnabled`, plus per-kind events (`IButtonPeer.Clicked`, `IWindowPeer.Closed`).
- `ICanvasPeer` — a single paintable, focusable surface. Every owner-drawn control realizes onto one of these, so a backend implements drawing and input once, not once per control. See [custom-controls.md](custom-controls.md).

Coordinates are parent-client pixels with a top-left origin everywhere, exactly like Windows Forms.

## Backend selection

`BackendRegistry` is a plain static list — no reflection, no assembly scanning. An app registers the backends it ships in `Program.cs`; `Resolve()` returns the first registered backend whose `IsSupported` is true and throws `PlatformNotSupportedException` otherwise. Registration is idempotent per concrete type.

Because registration is an explicit `new Win32Backend()` in user code, the trimmer sees exactly which backends an app carries: register all of them for one binary that runs everywhere (only the supported one is ever realized — see `NativeForms.Demo/Program.cs`), or register one and the other backends never enter the build. Details in [controls/application.md](controls/application.md).

## Realization: buffered, then flushed

A `Control` carries its state (`Bounds`, `Text`, `Visible`, `Enabled`) in managed fields with no peer attached. Property writes before realization simply update those fields. When `Application.Run` starts, `Form.RealizeWindow`:

1. calls `Control.RealizeSelf`, which asks the backend for the right peer kind and flushes the buffered state into it (`SetBounds`, `SetText`, `SetEnabled`, `SetVisible`, in that order), then gives the subclass its `OnRealized` hook to wire native events;
2. subscribes the window peer's `Closed` event to raise `Form.FormClosed`;
3. realizes each of the form's **direct** children the same way and re-parents each child peer into the window via `IWindowPeer.AddChild`;
4. calls `IWindowPeer.Show`.

After realization, property writes forward to the peer immediately. There is no shadow widget tree and no dirty-flag machinery — before realization the managed field *is* the state, afterwards the native widget is.

Note the current shape: only the form's direct children are realized. Controls nested inside a `Panel` or `GroupBox` are not yet walked (container layout is tracked in [PRD.md](PRD.md) §7.1/§7.2).

## The message loop

`Application.Run(Form, IPlatformBackend)` hands the realized window peer to `IPlatformBackend.Run`, which enters the platform loop (`GetMessage`/`DispatchMessage` on Win32, the GTK main loop on Linux) and blocks until the main window closes or `Quit` is called. `Application.Exit()` forwards to the running backend's `Quit`. `Run` must be invoked on the thread that created the widgets.

## Backend projects

| Project | Assembly | Binding |
|---|---|---|
| `NativeForms.Backends.Windows` | `Hawkynt.NativeForms.Backends.Windows` | Win32 (user32/GDI) via `[LibraryImport]`; classic `GetMessage` loop; theme from OS metrics |
| `NativeForms.Backends.Gtk` | `Hawkynt.NativeForms.Backends.Gtk` | GTK 3 via `[LibraryImport]`; `gtk_init` exactly once; Cairo/Pango drawing; theme from `GtkStyleContext` |
| `NativeForms.Backends.MacOS` | `Hawkynt.NativeForms.Backends.MacOS` | Cocoa placeholder — `IsSupported` is true on macOS but every factory method throws `PlatformNotSupportedException` with an actionable message |

Every backend type compiles on every OS; `IsSupported` gates it at run time, so a single binary can carry all three.

## API

`IPlatformBackend`:

| Member | Description |
|---|---|
| `Name` | Short diagnostic identifier (`"Win32"`, `"Gtk"`, `"Cocoa"`) |
| `IsSupported` | Whether this backend can run on the current OS |
| `Theme` | The native `ITheme` owner-drawn controls paint with |
| `CreateWindow()` / `CreateButton()` / `CreateLabel()` / `CreateCanvas()` | Create an unrealized peer of that kind |
| `CreateImage(int, int, ReadOnlySpan<int>)` | A native bitmap from 32-bit ARGB pixels, decoder-free |
| `Run(IWindowPeer)` | Enter the message loop; blocks until close or `Quit` |
| `Quit()` | Request the running loop to exit |

Peer interfaces:

| Interface | Adds on top of `IControlPeer` (`SetBounds`/`SetText`/`SetVisible`/`SetEnabled`/`Dispose`) |
|---|---|
| `IWindowPeer` | `AddChild(IControlPeer)`, `Show()`, `Closed` event |
| `IButtonPeer` | `Clicked` event |
| `ILabelPeer` | nothing — static text |
| `ICanvasPeer` | paint/mouse/key/focus events, `Invalidate`, `InvalidateAll`, `Focus`, `SetFocusable` — see [custom-controls.md](custom-controls.md) |

## Notes

**AOT/trim discipline, as enforced in the build.** `NativeForms.Core` sets `IsAotCompatible=true`, which turns on the trim/AOT/single-file analyzers; the core contains no reflection, no `TypeDescriptor`, and no dynamic code — binding (`PropertyBinding<T>`) is delegate-based. Backends use `[LibraryImport]` source-generated P/Invoke only. Backend selection is explicit construction, so trimming a single-platform build is just registering fewer backends.

**Footprint discipline, as measured by tests.** `NativeForms.Tests/AllocationBudgetTests.cs` asserts an unrealized control allocates under 512 bytes and an owner-drawn control under 768 bytes, and that appending to an unbound `ObservableList<T>` stays allocation-free apart from the event args. Geometry is value types (`Rectangle`/`Point`/`Size`, and `Font` is a struct); event slots are null until subscribed.

**Not yet implemented.** Per-frame zero-allocation steady state, the empty-form realization budget, startup-time and trim-size regression tests are open boxes in [PRD.md](PRD.md) §4; nested-container realization and the layout engine in §7.1–§7.2; the Cocoa backend in §10 (M9).
