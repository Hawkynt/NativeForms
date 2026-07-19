# Control

> The abstract base of every visual element: WinForms-shaped geometry, `Text`, `Visible`/`Enabled`, a re-parenting `Controls` collection, `Click`/`TextChanged` events — all buffered until the form is shown, then live against the native peer.

`Hawkynt.NativeForms.Control` · strategy: **base class** (subclasses wrap a native widget or owner-draw) · peer: `IControlPeer`

## Usage

```csharp
using Hawkynt.NativeForms;

var form = new Form { Text = "Demo", Bounds = new(0, 0, 320, 160) };
var button = new Button { Text = "OK", Bounds = new(20, 20, 140, 36), Enabled = false };
button.Click += (_, _) => button.Text = "Clicked";
form.Controls.Add(button);

button.Enabled = true;   // still buffered — no native widget exists yet

Application.Run(form);   // realizes the tree, flushes buffered state, shows the window
```

## Realization lifecycle

Set properties whenever you like — order does not matter. Before the form is shown, a control is pure managed state (a few fields, no native handle). When `Application.Run` starts, each control is *realized*: the backend creates its peer and the buffered state is flushed into the native widget (`Bounds`, `Text`, `Enabled`, `Visible`, in that order). From then on, every property write forwards to the widget immediately, and native events (a button click, a window close) surface as the familiar .NET events. Setting a property to its current value is a no-op: no peer call, no event.

Today the window realizes its **direct** children only; controls nested inside a `Panel`/`GroupBox` are not yet walked (see [../PRD.md](../PRD.md) §7.2).

## API

Properties:

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `""` | Caption text: button label, form title, label text. `null` is normalized to `""` |
| `Bounds` | `Rectangle` | `(0, 0, 0, 0)` | Position and size relative to the parent's client area, in pixels |
| `Location` | `Point` | `(0, 0)` | The top-left corner of `Bounds` |
| `Size` | `Size` | `(0, 0)` | The size of `Bounds` |
| `Left` / `Top` | `int` | `0` | X / Y of the top-left corner |
| `Width` / `Height` | `int` | `0` | Extent in pixels |
| `Visible` | `bool` | `true` | Whether the widget is shown |
| `Enabled` | `bool` | `true` | Whether the widget accepts user interaction |
| `Parent` | `Control?` | `null` | The containing control; `null` for a top-level form. Set by `Controls.Add`/`Remove` |
| `Controls` | `ControlCollection` | empty | The child controls hosted by this control |

Events:

| Event | Description |
|---|---|
| `Click` | Raised when the control is activated by the user (native button click, checkbox toggle, `PerformClick`) |
| `TextChanged` | Raised after `Text` changes to a different value |

Methods:

| Method | Description |
|---|---|
| `PerformClick()` | Programmatically raises `Click` |
| `OnClick(EventArgs)` / `OnTextChanged(EventArgs)` | `protected virtual` raisers for subclasses |

`ControlCollection` (implements `IReadOnlyList<Control>`):

| Member | Description |
|---|---|
| `Count` / `this[int]` | Number of children / child at index |
| `Add(Control)` | Appends and sets the child's `Parent` to the owner |
| `AddRange(params Control[])` | Adds several controls in order |
| `Remove(Control)` | Removes and clears `Parent`; returns whether it was present |
| `Contains(Control)` | Whether the control is a direct child |
| `Clear()` | Removes every child and clears their parents |

Input and event-argument types (used chiefly by [owner-drawn controls](../custom-controls.md)):

| Type | Description |
|---|---|
| `PaintEventArgs` | `Graphics` (`IGraphics` surface) + `ClipRectangle` |
| `MouseEventArgs` | `Button`, `X`/`Y`/`Location` (client space), wheel `Delta` |
| `KeyEventArgs` | `KeyCode`, `Modifiers` (+ `Shift`/`Control`/`Alt` helpers), settable `Handled` |
| `KeyPressEventArgs` | Typed `KeyChar`, settable `Handled` |
| `MouseButtons` / `KeyModifiers` | Flags enums: `Left`/`Right`/`Middle`; `Shift`/`Control`/`Alt` |
| `Keys` | Virtual keys the toolkit reacts to (Win32 virtual-key numbers): navigation, `Space`, `Enter`, `Escape`, `Back`, `Tab`, `Insert`, `Delete` |

## Notes

Geometry members are views over the single `Bounds` field — `Left = 10` rewrites `Bounds`, so one field and one `SetBounds` call cover all seven members. All geometry is value-typed (`Rectangle`/`Point`/`Size`); an unrealized control is budget-tested to stay under 512 bytes (`AllocationBudgetTests`).

Controls raise events only through null-checked handler slots, so an unsubscribed event costs nothing. `TextChanged` fires whether or not the control is realized.

Behavior is fully testable without a display: realize against the headless backend (`Application.Run(form, new HeadlessBackend())`) and assert against the recorded peers — see [../custom-controls.md](../custom-controls.md) and `NativeForms.Tests/RealizationTests.cs`.

**Not yet implemented.** Focus model (`TabIndex`, `TabStop`, focus events on `Control` itself), keyboard/mouse events on arbitrary controls, `Font`/`ForeColor`/`BackColor`/`Padding`/`Margin`/`Anchor`/`Dock`, `Cursor`, and the layout engine are planned — [../PRD.md](../PRD.md) §7.1.
