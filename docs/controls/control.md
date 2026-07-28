# Control

> The abstract base of every visual element: WinForms-shaped geometry, `Text`, `Visible`/`Enabled`, ambient `Font`/colors/`Cursor`, `Anchor`/`Dock` layout, tab order and focus events, drag-and-drop, and a re-parenting `Controls` collection — all buffered until the form is shown, then live against the native peer.

![Control in the NativeForms demo](../screenshots/01-basics.png)

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

Set properties whenever you like — order does not matter. Before the form is shown, a control is pure managed state (a few fields, no native handle). When `Application.Run` starts, each control is *realized*: the backend creates its peer and the buffered state is flushed into the native widget (`Bounds`, `Text`, `Enabled`, `Visible`, then any explicitly set appearance). From then on, every property write forwards to the widget immediately, and native events (a button click, a window close) surface as the familiar .NET events. Setting a property to its current value is a no-op: no peer call, no event.

Realization walks the **whole tree** depth-first: any control can parent children (the window peer and every owner-drawn canvas are container peers), so nesting under `Panel`/`GroupBox`/`TabPage` just works. The tree stays live afterwards:

- `Controls.Add` on a realized container realizes the added control **and its subtree** immediately.
- `Controls.Remove`/`Clear` dispose the removed control's peer tree, children first. Its managed state (text, bounds, children) survives, so the control is back to its unrealized shape and re-realizes from the buffer when re-added.
- A child's peer rectangle is the container's mapping of its logical `Bounds` (identity by default; `Panel.AutoScroll` shifts it by the scroll offset), so scrolling moves native widgets without touching anyone's `Bounds`.

## API

### Geometry and identity

| Property | Type | Default | Description |
|---|---|---|---|
| `Bounds` | `Rectangle` | `(0, 0, 0, 0)` | Position and size relative to the parent's client area, in pixels |
| `ContextMenuStrip` | `ContextMenuStrip?` | `null` | The context menu a right-click opens at the cursor. Wired through the owner-drawn mouse pipeline; native-widget controls need right-click peer events first (tracked in [../PRD.md](../PRD.md)) |
| `Controls` | `ControlCollection` | empty | The child controls hosted by this control (allocated on first access) |
| `Enabled` | `bool` | `true` | **Effective** the same way: `false` inside a disabled ancestor; the setter writes only the local flag |
| `Location` / `Size` / `Left` / `Top` / `Width` / `Height` | — | zero | Views over the single `Bounds` field |
| `Name` | `string` | `""` | Programmatic name — designer-style lookup data, never rendered. `null` resets to `""` |
| `Parent` | `Control?` | `null` | The containing control; `null` for a top-level form. Set by `Controls.Add`/`Remove` |
| `Tag` | `object?` | `null` | Arbitrary caller-owned data; the toolkit never reads it |
| `Text` | `string` | `""` | Caption text: button label, form title, label text. `null` is normalized to `""` |
| `Visible` | `bool` | `true` | **Effective**, like WinForms: the getter reports `false` inside a hidden ancestor even while this control's own flag is set; the setter writes only the local flag |

### Layout

| Property | Type | Default | Description |
|---|---|---|---|
| `Anchor` | `AnchorStyles` | `Top \| Left` | Container edges the control is bound to: anchored edges keep their distance to the parent's `DisplayRectangle`, opposing anchors stretch, `None` drifts by half the delta. Assigning resets `Dock` to `None` — last one assigned wins, like WinForms |
| `DisplayRectangle` | `Rectangle` | — | The client rectangle available to content: the client area deflated by `Padding` (containers with chrome, like `GroupBox`, deflate further) |
| `Dock` | `DockStyle` | `None` | The parent edge the control glues itself to; assigning resets `Anchor` to its default. Docked siblings claim edges in **reverse `Controls` order** — the last-added child docks first, so designer-style `Add(fill); Add(toolbar); Add(menu)` stacks the menu topmost, exactly like WinForms — and `Fill` takes the remainder |
| `Margin` | `Padding` | all zero | Spacing layout containers (`FlowLayoutPanel`, `TableLayoutPanel`) keep around this control; plain containers position by `Bounds` alone and ignore it |
| `Padding` | `Padding` | all zero | Interior spacing between the control's edges and its content; honored through `DisplayRectangle`. Not ambient — each control owns its padding |
| `RightToLeft` | `RightToLeft` | `Inherit` | Text direction, resolved up the parent chain (default `No`). Owner-drawn controls mirror their glyph/text painting; containers do not mirror child layout yet ([../PRD.md](../PRD.md) §8) |

`SuspendLayout()`/`ResumeLayout()`/`ResumeLayout(bool)` coalesce bulk changes into one pass (calls nest); `PerformLayout()` runs the container's layout pass immediately.

### Focus and keyboard

| Member | Type | Default | Description |
|---|---|---|---|
| `CanFocus` | `bool` | — | Whether `Focus()` would succeed now: focusable kind, visible, enabled, realized |
| `Focus()` | method | — | Moves keyboard focus via the peer; a no-op while `CanFocus` is `false` |
| `Focused` | `bool` | `false` | Whether the peer currently holds keyboard focus |
| `TabIndex` | `int` | `0` | Position in the container's tab order: siblings ascend by `TabIndex` (ties keep insertion order), depth-first through nested containers — the WinForms traversal |
| `TabStop` | `bool` | per kind | Whether Tab stops here. Until assigned it follows the kind's default: focusable controls are stops; labels, panels, group boxes, picture boxes, progress bars, scroll bars and strips are not; the menu bar opts out (Alt reaches it), matching WinForms |
| `UseNativeWidget` | `bool?` | `null` | Whether this control realizes onto a real platform widget rather than the owner-drawn surface; `null` follows [`Application.PreferNativeWidgets`](application.md) |

### Accessibility

| Name | Type | Default | Description |
|---|---|---|---|
| `AccessibleDescription` | `string?` | `null` | A longer description announced after the name |
| `AccessibleName` | `string?` | `null` | What assistive technology calls the control; unset, the control's `Text` is used |
| `AccessibleRole` | `AccessibleRole` | `Default` | What the control is; unset, each control reports its own kind |

A control with a caption is already named — set `AccessibleName` only where the visible text is not the whole story: a toolbar button showing an icon, a close glyph, a field whose meaning lives in a separate label. Renaming a control renames it for a screen reader too, unless you named it explicitly.

This matters most for **owner-drawn** controls. A real platform widget answers for itself — a promoted [`CheckBox`](checkbox.md) tells the screen reader it is a check box with its caption, unprompted — but an owner-drawn control is a blank drawing surface to an accessibility client however carefully it is painted, so the toolkit publishes the name and role on its behalf. On GTK that goes to the widget's `AtkObject` (verified against ATK itself in `GtkAccessibilityTests`); on Win32 the name becomes the canvas window's text, which is what MSAA reads for a window it knows nothing else about.

**Not yet:** the role is not published on Win32 — MSAA infers it from the window class, and overriding that needs a `WM_GETOBJECT` provider. A screen reader there announces the right name and a generic kind. See [PRD §8](../PRD.md#8-cross-cutting-features).

### Keyboard navigation

Tab and Shift+Tab walk the tab order described above. `Ctrl+Tab`/`Ctrl+Shift+Tab` switch the pages of the innermost [`TabControl`](tabcontrol.md) containing the focused control — from anywhere inside a page, not only when the header has focus — and are left alone when there is no tab control above, so they stay available to an application that wants them.

`SelectNextControl(Control?, bool forward, bool tabStopOnly, bool nested, bool wrap)` on [`Form`](form.md) drives the same traversal from code, so a program moving focus itself and a user pressing the key cannot disagree. `nested` is accepted for source compatibility and always behaves as `true`: this toolkit has no container that hides its children from the tab order.

### Native-widget promotion

`UseNativeWidget` is a preference, never an override of the truth: a control is promoted only while its properties stay inside what the platform widget can express, and a backend without such a widget declines regardless — the owner-drawn painter is always the fallback. Controls with no platform counterpart ignore it entirely.

Assigning it on a realized control does **not** swap the peer: a form already on screen should not change its rendering out from under the user. A property change that crosses the eligibility line does, because there the alternative is drawing something wrong; that swap is state transparent, keyboard focus included. Which controls take part, and the gate for each, is in [PRD §12](../PRD.md#12-native-peer-promotion-opt-into-real-widgets-where-the-platform-has-one).

Setting it to `false` is the escape hatch for a host that imposes a size a platform widget will not accept — [`ToolStripControlHost`](toolstrip.md) does exactly that, because a toolbar row is shorter than several desktops will draw a combo box in.

### Appearance (ambient)

`Font`, `ForeColor`, `BackColor` and `Cursor` are **ambient**, exactly like WinForms: an unset control inherits the nearest ancestor's explicit value and finally the theme (cursor: `Cursors.Arrow`). `ResetFont()`/`ResetForeColor()`/`ResetBackColor()`/`ResetCursor()` return an explicitly set control to the ambient value. The rarely-set slots live in one lazily allocated object, so all-default controls pay a single null reference.

### Painting

| Method | Description |
|---|---|
| `Invalidate()` / `Invalidate(Rectangle)` | Requests a repaint (of a sub-region). Owner-drawn controls forward to their canvas; native-widget controls repaint through the platform and treat this as a no-op |
| `Refresh()` | Invalidates the whole control. Unlike WinForms the repaint is **not forced synchronously** — it arrives with the platform's next paint cycle |

### Drag and drop

| Member | Description |
|---|---|
| `AllowDrop` (`bool`, default `false`) | Opts the control in as a drop target; only opted-in controls are hit-tested and raise the drag events |
| `DoDragDrop(object data, DragDropEffects allowedEffects)` | Starts an in-process drag with this control as the source. **Returns `void` and returns immediately** — unlike WinForms there is no nested message loop and no final-effect return value; the drag rides the source's captured mouse stream. The source must be a realized owner-drawn control; OS-level (cross-process) drag-and-drop is tracked in [../PRD.md](../PRD.md) §8 |
| `DragEnter` / `DragOver` / `DragLeave` / `DragDrop` | The target-side events, carrying `DragEventArgs` (`Data`, `AllowedEffect`, settable `Effect`, `X`/`Y`) |

### Threading

| Member | Description |
|---|---|
| `BeginInvoke(Action)` | Queues the action onto the UI thread and returns immediately; throws `InvalidOperationException` when no loop is running and the control is unrealized |
| `Invoke(Action)` | Runs the action on the UI thread and blocks; inline when already there (or no loop). Exceptions propagate to the caller |
| `InvokeRequired` | `true` only while a message loop runs on another thread; `false` outside `Application.Run`, matching the WinForms convention for a handle-less control |

### Other members

| Member | Description |
|---|---|
| `FindForm()` | The form this control sits on (itself for a form), or `null` while unparented |
| `LogicalToDevice(int)` / `LogicalToDevice(Size)` | Scales logical (96-DPI) pixels by the backend's DPI scale; identity before realization |
| `PerformClick()` | Raises `Click` programmatically — a **no-op while the control is not effectively `Enabled` and `Visible`**, the WinForms contract a disabled dialog button relies on |
| `PointToScreen(Point)` | Maps a client-space point to screen coordinates; throws `InvalidOperationException` before realization |

### Events

| Event | Description |
|---|---|
| `Click` | Raised when the control is activated by the user (native click, checkbox toggle, `PerformClick`) |
| `DragEnter` / `DragOver` / `DragLeave` / `DragDrop` | See drag and drop above |
| `Enter` / `GotFocus` | Raised when focus arrives, in that order (the WinForms order). Containers along the way raise `Enter` when focus enters their subtree from outside, outermost first |
| `LostFocus` / `Leave` | Raised when focus departs — **`LostFocus` first, then `Leave`**, mirroring the WinForms firing order on the departing control. Containers raise `Leave` when focus leaves their subtree entirely, innermost first |
| `MouseDoubleClick` / `DoubleClick` | Raised on a double-click over an owner-drawn control (detected from press timing and slop) |
| `MouseDown` / `MouseUp` / `MouseWheel` | Raised on button presses, releases and wheel turns over an **owner-drawn** control; native widgets consume these themselves |
| `MouseMove` / `MouseEnter` / `MouseLeave` | Raised as the pointer moves over, enters and leaves the control — for every control, native or owner-drawn |
| `TextChanged` | Raised after `Text` changes to a different value (realized or not) |

`OnClick`/`OnTextChanged`/`OnGotFocus`/`OnLostFocus`/`OnEnter`/`OnLeave`/`OnDrag…` are the `protected virtual` raisers for subclasses.

### Support types

`Padding` (a 16-byte `readonly record struct`, used by `Margin` and `Padding`):

| Member | Description |
|---|---|
| `All` | The uniform value when all sides agree, otherwise `-1` |
| `Horizontal` / `Vertical` | `Left + Right` / `Top + Bottom` |
| `Left` / `Top` / `Right` / `Bottom` | The four sides |
| `Padding(int left, int top, int right, int bottom)` / `Padding(int all)` | Per-side or uniform spacing in pixels |

`ControlCollection` (implements `IReadOnlyList<Control>`):

| Member | Description |
|---|---|
| `Add(Control)` | Appends and sets the child's `Parent` to the owner; realizes the child's subtree immediately when the owner is already live |
| `AddRange(params Control[])` | Adds several controls in order |
| `Clear()` | Removes every child, clearing parents and disposing their peer trees |
| `Contains(Control)` | Whether the control is a direct child |
| `Count` / `this[int]` | Number of children / child at index |
| `Remove(Control)` | Removes, clears `Parent` and disposes the child's peer tree; returns whether it was present |

Input and event-argument types (used chiefly by [owner-drawn controls](../custom-controls.md)):

| Type | Description |
|---|---|
| `PaintEventArgs` | `Graphics` (`IGraphics` surface) + `ClipRectangle` |
| `MouseEventArgs` | `Button`, `X`/`Y`/`Location` (client space), wheel `Delta`, plus `Modifiers` with `Shift`/`Control`/`Alt` helpers for selection gestures |
| `KeyEventArgs` | `KeyCode`, `Modifiers` (+ `Shift`/`Control`/`Alt` helpers), combined `KeyData`, settable `Handled` |
| `KeyPressEventArgs` | Typed `KeyChar`, settable `Handled` |
| `MouseButtons` / `KeyModifiers` | Flags enums: `Left`/`Right`/`Middle`; `Shift`/`Control`/`Alt` |
| `Keys` | Virtual keys the toolkit reacts to (Win32 virtual-key numbers): navigation, `Space`, `Enter`, `Escape`, `Back`, `Tab`, `Insert`, `Delete`, digits, letters, function keys and numpad `Multiply`/`Add`/`Subtract` |

## Differences from System.Windows.Forms.Control

- **Mouse events, partly.** `Control` exposes `MouseMove`/`MouseEnter`/`MouseLeave` for every control, and `MouseDown`/`MouseUp`/`MouseWheel`/`MouseDoubleClick`/`DoubleClick` for owner-drawn controls; native widgets consume their own button/wheel input, so those do not surface for them (the same limit as native key preview). Public keyboard events (`KeyDown`/`KeyUp`/`KeyPress`) remain owner-drawn-only, as `protected virtual` `OnKey…` overrides on `OwnerDrawnControl` (see [../custom-controls.md](../custom-controls.md)).
- **No `MouseDoubleClick` and no click counts** — `MouseEventArgs` carries no `Clicks`; controls that need a double-click gesture (e.g. the grid) detect it themselves.
- **No validation pipeline**: `Validating`/`Validated`/`CausesValidation` do not exist. Controls with a commit step (grid editing, spin boxes) offer their own `…Validating` events instead.
- **No `Handle`, no `CreateControl()`** — there is no exposed native handle and no manual handle creation; the realization lifecycle above replaces both.
- **`Refresh()` is not synchronous** and `Invalidate` is a no-op on native-widget controls (see painting, above).
- **`DoDragDrop` is `void` and non-blocking** — no nested loop, no returned effect (see drag and drop, above).

## Notes

Geometry members are views over the single `Bounds` field — `Left = 10` rewrites `Bounds`, so one field and one `SetBounds` call cover all seven members. All geometry is value-typed (`Rectangle`/`Point`/`Size`); an unrealized control is budget-tested to stay under 512 bytes (`AllocationBudgetTests`) — which is why flags pack into one word and the appearance slots allocate lazily.

Controls raise events only through null-checked handler slots, so an unsubscribed event costs nothing.

Behavior is fully testable without a display: realize against the headless backend (`Application.Run(form, new HeadlessBackend())`) and assert against the recorded peers — see [../custom-controls.md](../custom-controls.md) and `NativeForms.Tests/RealizationTests.cs`.
