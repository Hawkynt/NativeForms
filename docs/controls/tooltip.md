# ToolTip

> Per-control hover text: rest the pointer on a registered control and a small themed popup appears

![ToolTip in the NativeForms demo](../screenshots/01-basics.png)
> near the cursor after `InitialDelay`, hiding again on leave, press or `AutoPopDelay`.

`Hawkynt.NativeForms.ToolTip` · strategy: **the platform's tooltip over a widget, owner-drawn
(native theme) over a canvas** · component, no peer of its own — one shared `IPopupPeer` plus a
`Timer`

## Usage

```csharp
var toolTip = new ToolTip { InitialDelay = 500, AutoPopDelay = 5000 };
toolTip.SetToolTip(panel, "Drop files here");
toolTip.SetToolTip(otherPanel, "Preview area");
// …
toolTip.SetToolTip(panel, null); // unregister
toolTip.Dispose();               // detach everything, release popup and timer
```

## API

| Member | Type | Default | Description |
|---|---|---|---|
| `Active` | `bool` (get) | `false` | Whether the tip popup is currently visible. |
| `AutoPopDelay` | `int` | `5000` | Milliseconds a visible tip stays up before hiding on its own; clamped to at least 1. |
| `Dispose()` | method | | Hides the tip, detaches every observed control and releases the native popup and timer. |
| `GetToolTip(Control control)` | method | | The registered hover text, or an empty string. |
| `Hide()` | method | | Hides the tip and stops any pending delay. |
| `InitialDelay` | `int` | `500` | Milliseconds the pointer must rest on a control before its tip appears; clamped to at least 1. |
| `SetToolTip(Control control, string? text)` | method | | Registers the hover text for a control, or removes the registration for a null/empty text. Backend-free — it may happen long before the control is realized. |

`ToolTip` is a component (`IDisposable`), not a control: one instance serves any number of
controls through a per-control text map, a single shared popup and a single delay timer.

## Notes

- **Lifecycle.** Pointer movement over a registered control (re)arms the `InitialDelay` timer;
  when it elapses the popup shows at the cursor position plus an 18 px vertical offset, sized to
  the text plus 4 px padding, and the timer re-arms with `AutoPopDelay`. The tip hides when the
  pointer leaves, a mouse button goes down, the auto-pop delay elapses, or the popup is dismissed.
  Leaving before the delay cancels the pending tip without ever creating a popup.
- **Every control, on either half.** Registration hooks `Control.PointerMove`, which every peer
  feeds — a canvas as well as a widget — so a registration never silently does nothing, and a
  control that moves between the two halves ([PRD §12](../PRD.md)) keeps its tip across the swap.
  An owner-drawn surface is hooked for its clicks as well, the one channel a canvas has and a
  widget does not.
- **Which surface shows the tip is decided by the half the control is on right now**, not by its
  type: a native widget gets the platform's own tooltip (`IControlPeer.ShowToolTip`), so the tip
  carries the desktop's shape, shadow and animation, while only an owner-drawn surface — which has
  no platform tip of its own — is worth floating a toolkit popup for. Asking the type instead is a
  bug this once had: a promoted `CheckBox`, `ComboBox`, `Label` or `Button` is an
  `OwnerDrawnControl` wearing a real widget, and its tip was hooked to a canvas that does not
  exist. Per-item tips in lists, trees and grids are still tracked in
  [docs/PRD.md](../PRD.md) §7.6.
- The popup paints with the theme's field background, border and control-text colors, so it matches
  the host desktop.
- Testable headlessly: `ToolTipTests` drive the delays through the test backend's controllable
  timer and pin the popup geometry, painting, every hide path and `Dispose`.

## Differences from System.Windows.Forms.ToolTip

- **The surface is `InitialDelay`, `AutoPopDelay`, `SetToolTip`/`GetToolTip`, `Hide`, `Dispose`** —
  there is no `ReshowDelay`, no `AutomaticDelay`, no `ShowAlways`, no balloon/title styling, and no
  manual `Show(text, control, …)` overloads; tips appear only through the hover lifecycle.
- **`SetToolTip` holds a strong reference to the control** in its text map. A `ToolTip` outliving
  its controls keeps them alive until you unregister (`SetToolTip(control, null)`) or `Dispose()` —
  dispose the component with the form it serves.
- **The tip's pixels belong to whoever owns them**: over a native widget it is the platform's own
  tooltip, so `Active` means "a tip was raised", not a promise about what was drawn.
