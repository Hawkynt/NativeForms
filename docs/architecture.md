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

`Application.Run(Form)` resolves a backend from the registry, realizes the form's entire control tree into native widgets, and blocks in the platform message loop until the window closes.

## The core/peer split

`NativeForms.Core` (`Hawkynt.NativeForms`) contains every public control, the event model, and the data-binding primitives — and zero native code. The only thing it knows about a platform is the contract in `Hawkynt.NativeForms.Backends`:

- `IPlatformBackend` — the factory and service surface. One implementation per windowing system; each creates peers for the natively wrapped controls (`CreateWindow`, `CreateButton`, `CreateLabel`, `CreateTextBox`, `CreateRichTextBox`), the owner-draw surfaces (`CreateCanvas`, `CreatePopup`), and the non-visual sources (`CreateTimer`, `CreateNotifyIcon`); builds images (`CreateImage`); exposes the OS theme, screen size and text measurement (`Theme`, `GetScreenSize`, `MeasureText`); fronts the native common dialogs (`ShowMessageBox`, `ShowFileDialog`, `ShowColorDialog`, `ShowFontDialog`) and the clipboard (`SetClipboardText`); and owns the message loop (`Run`/`Quit`).
- **Peers** — the native side of a single control. A peer owns one platform widget (an HWND, a GtkWidget*) and exposes only what the core needs to keep it in sync: `SetBounds`, `SetText`, `SetVisible`, `SetEnabled`, `PointToScreen`, plus per-kind operations and events (`IButtonPeer.Clicked`, `IWindowPeer.Closed`).
- `IContainerPeer` — a peer that hosts child peers (`AddChild`). The window peer and every canvas peer implement it, so **any control is a potential parent**, exactly like Windows Forms.
- `ICanvasPeer` — a single paintable, focusable surface (and a container). Every owner-drawn control realizes onto one of these, so a backend implements drawing and input once, not once per control. See [custom-controls.md](custom-controls.md).
- `IPopupPeer` — a light-dismiss floating canvas at a screen position: the surface behind drop-downs, menus, tooltips and calendar fly-outs. It dismisses itself on an outside click, grab loss or Escape, and never steals activation from its owner.

Coordinates are parent-client pixels with a top-left origin everywhere, exactly like Windows Forms.

## Backend selection

`BackendRegistry` is a plain static list — no reflection, no assembly scanning. An app registers the backends it ships in `Program.cs`; `Resolve()` returns the first registered backend whose `IsSupported` is true and throws `PlatformNotSupportedException` otherwise. Registration is idempotent per concrete type.

Because registration is an explicit `new Win32Backend()` in user code, the trimmer sees exactly which backends an app carries: register all of them for one binary that runs everywhere (only the supported one is ever realized — see `NativeForms.Demo/Program.cs`), or register one and the other backends never enter the build. Details in [controls/application.md](controls/application.md).

## Realization: buffered, then flushed

A `Control` carries its state (`Bounds`, `Text`, `Visible`, `Enabled`, …) in managed fields with no peer attached. Property writes before realization simply update those fields. When `Application.Run` starts, `Form.RealizeWindow`:

1. applies `StartPosition` by rewriting `Bounds` in the core (centering against `IPlatformBackend.GetScreenSize` or the owner's bounds — the peers never see the placement policy);
2. calls `Control.RealizeSelf`, which asks the backend for the right peer kind and flushes the buffered state into it (`SetBounds`, `SetText`, `SetEnabled`, `SetVisible`, in that order), then gives the subclass its `OnRealized` hook — a `Form` wires the window peer's `Closed`/`BoundsChangedByUser`/`WindowStateChanged` events there and flushes its window-management state;
3. when the new peer is a container (`IContainerPeer`), realizes the **whole subtree** depth-first, handing each child peer to its container via `AddChild` — nesting under `Panel`, `GroupBox`, `TabPage` or any other control just works;
4. calls `IWindowPeer.Show`.

After realization, property writes forward to the peer immediately. There is no shadow widget tree and no dirty-flag machinery — before realization the managed field *is* the state, afterwards the native widget is.

The tree stays live afterwards: `Controls.Add` on a realized container realizes the added control (and its subtree) immediately; `Controls.Remove`/`Clear` dispose the removed control's peer tree, children first. The managed state survives the teardown, so a removed control is back to its unrealized shape and re-realizes from its buffer when re-added.

### The bounds-mapping seam

A child's logical `Bounds` is not always the rectangle its peer occupies. The container maps one to the other (identity by default); `Panel.AutoScroll` shifts the mapping by its scroll offset, so native children physically move while every child's logical `Bounds` stays put. The same seam lets a container veto a child's effective peer visibility (a collapsed expander) without clobbering the child's own `Visible`.

## The message loop

`Application.Run(Form, IPlatformBackend)` hands the realized window peer to `IPlatformBackend.Run`, which enters the platform loop (`GetMessage`/`DispatchMessage` on Win32, the GTK main loop on Linux) and blocks until the main window closes or `Quit` is called. While the loop runs, the backend is the process-wide *current* backend (an internal seam) — `Timer` and `Form.ShowDialog` resolve it there instead of dragging a backend through every constructor. `Application.Exit()` forwards to the running backend's `Quit`. `Run` must be invoked on the thread that created the widgets.

**Modal loops.** `Form.ShowDialog(owner)` realizes the dialog's own window peer and enters a *nested* native loop through `IWindowPeer.RunModal(owner)`: the owner is disabled (Win32) or made transient-modal (GTK) while the nested loop runs, and the call blocks until the dialog closes. On return the dialog's peer tree is disposed and `ShowDialog` reports the `DialogResult`. The outer `Application.Run` loop keeps running underneath.

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
| `CreateWindow()` / `CreateButton()` / `CreateLabel()` / `CreateTextBox()` / `CreateRichTextBox()` | Create an unrealized peer of that native-widget kind |
| `CreateCanvas()` / `CreatePopup()` | Create an owner-draw surface / a light-dismiss popup surface |
| `CreateTimer()` / `CreateNotifyIcon()` | Create a stopped UI-thread timer / a hidden tray icon (throws `NotSupportedException` where the platform has no tray) |
| `CreateImage(int, int, ReadOnlySpan<int>)` | A native bitmap from 32-bit ARGB pixels, decoder-free |
| `GetScreenSize()` | Pixel size of the primary screen (used for `Form.StartPosition` centering) |
| `MeasureText(string, Font)` | Text measurement without a paint surface, same engine as `IGraphics.MeasureText` |
| `ShowMessageBox(…)` / `ShowFileDialog(…)` / `ShowColorDialog(…)` / `ShowFontDialog(…)` | The platform's native common dialogs, application-modal |
| `SetClipboardText(string)` | Places plain text on the system clipboard (write-only seam) |
| `Run(IWindowPeer)` | Enter the message loop; blocks until close or `Quit` |
| `Quit()` | Request the running loop to exit |

Peer contracts (`IControlPeer` is `SetBounds`/`SetText`/`SetVisible`/`SetEnabled`/`PointToScreen`/`Dispose`; `IContainerPeer` adds `AddChild`):

| Interface | Contract |
|---|---|
| `IWindowPeer` | Container + `Show`, `RunModal`, `Close`, `SetBorderStyle`, `SetWindowState`, `SetMinimizeBox`/`SetMaximizeBox`, `SetSizeLimits`, `SetIcon`, `SetTopMost`, `SetOpacity`; `Closed`, `BoundsChangedByUser`, `WindowStateChanged` events |
| `IButtonPeer` | `Clicked` event, `SetImage` |
| `ILabelPeer` | `SetTextAlign`, `SetBorderStyle`, `SetUseMnemonic`, `SetImage` |
| `ITextBoxPeer` | Multiline/placeholder/password/read-only/max-length/selection setters, live `GetText`/`GetSelection`, `TextChangedByUser` event |
| `IRichTextBoxPeer` | `ITextBoxPeer` + selection-formatting commands, URL detection, zoom, RTF in/out, `LinkClicked` event |
| `ICanvasPeer` | Container + paint/mouse/key/focus events, `Invalidate`, `InvalidateAll`, `Focus`, `SetFocusable` — see [custom-controls.md](custom-controls.md) |
| `IPopupPeer` | `ICanvasPeer` + `ShowAt`, `Hide`, `Dismissed` event (light dismiss) |
| `INotifyIconPeer` (`IDisposable` only) | `SetIcon`, `SetToolTip`, `SetVisible`; `Click`/`DoubleClick` events |
| `ITimerPeer` (`IDisposable` only) | `Start(intervalMs)`, `Stop`; `Tick` event on the UI thread |

## Notes

**AOT/trim discipline, as enforced in the build.** `NativeForms.Core` sets `IsAotCompatible=true`, which turns on the trim/AOT/single-file analyzers; the core contains no reflection, no `TypeDescriptor`, and no dynamic code — binding (`PropertyBinding<T>`) is delegate-based. Backends use `[LibraryImport]` source-generated P/Invoke only. Backend selection is explicit construction, so trimming a single-platform build is just registering fewer backends.

**Footprint discipline, as measured by tests.** `NativeForms.Tests/AllocationBudgetTests.cs` asserts an unrealized control allocates under 512 bytes and an owner-drawn control under 768 bytes, and that appending to an unbound `ObservableList<T>` stays allocation-free apart from the event args. Geometry is value types (`Rectangle`/`Point`/`Size`, and `Font` is a struct); event slots are null until subscribed.

**Not yet implemented.** Per-frame zero-allocation steady state, the empty-form realization budget, startup-time and trim-size regression tests are open boxes in [PRD.md](PRD.md) §4; the focus model and `Anchor`/`Dock` layout in §7.1; `IContainerPeer.RemoveChild` (so containers drop bookkeeping for removed children before the container dies) in §7.2; the Cocoa backend in §10 (M9).
