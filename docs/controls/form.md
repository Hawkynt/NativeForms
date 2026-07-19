# Form

> A top-level window backed by a real native window: `Text` is the title bar, `Bounds` the frame, children realize into the client area, and `FormClosed` fires when the user closes it.

`Hawkynt.NativeForms.Form` · strategy: **native** · peer: `IWindowPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var form = new Form { Text = "Hello", Bounds = new(0, 0, 320, 160) };
var button = new Button { Text = "Click me", Bounds = new(20, 20, 140, 36) };
button.Click += (_, _) => button.Text = "Clicked!";
form.Controls.Add(button);
form.FormClosed += (_, _) => Console.WriteLine("closed");

Application.Run(form);   // shows the window and blocks until it closes
```

Or subclass, as `NativeForms.Demo/MainForm.cs` does — set `Text`/`Bounds` and populate `Controls` in the constructor; `Application.Run(new MainForm())` does the rest.

## What works today

- **Title** — `Text` maps to the native title bar (buffered before realization, live afterwards).
- **Bounds** — position and size of the window frame in pixels.
- **Show** — `Application.Run` realizes the form into an `IWindowPeer`, realizes each direct child, re-parents the child peers into the client area, and calls the native `Show`.
- **Close** — the native close (close button, Alt+F4) ends the message loop and raises `FormClosed`.

There is no public `Show()`/`Close()` on `Form` itself — showing is `Application.Run`'s job, closing is the user's (or `Application.Exit()`).

## API

Everything from [`Control`](control.md) — `Text`, `Bounds`/`Location`/`Size`, `Visible`, `Enabled`, `Controls`, `Click`/`TextChanged` — plus:

| Event | Description |
|---|---|
| `FormClosed` | Raised after the user closes the window |

| Method | Description |
|---|---|
| `OnFormClosed(EventArgs)` | `protected virtual` raiser for subclasses |

## Notes

**Realization.** A `Form` is plain managed state until `Application.Run`; set title, bounds and children in any order beforehand and they flush into the native window at realization (see [control.md](control.md)). Only the form's direct children are realized — nesting under `Panel`/`GroupBox` does not yet realize grandchildren.

**Theming.** The window is fully native (an HWND on Win32, a `GtkWindow` on GTK), so the frame, title bar and background are the platform's own — nothing to configure.

**Testing.** Headless: `Application.Run(form, new HeadlessBackend())` returns immediately; assert against the recorded window peer (title, children, shown) and raise its `Closed` to test `FormClosed` — see `NativeForms.Tests/RealizationTests.cs`.

**Not yet implemented.** Planned per [../PRD.md](../PRD.md) §7.2: `StartPosition`, `FormBorderStyle`, `WindowState` and `MinimizeBox`/`MaximizeBox`; `MinimumSize`/`MaximumSize` and resize events; `AcceptButton`/`CancelButton`; `ShowDialog()` with `DialogResult`; MDI (or a documented non-goal); `Icon`, `TopMost`, `Opacity`.
