# ToolTip

> Per-control hover text: rest the pointer on a registered control and a small themed popup appears
> near the cursor after `InitialDelay`, hiding again on leave, press or `AutoPopDelay`.

`Hawkynt.NativeForms.ToolTip` · strategy: **owner-drawn** (native theme) · component, no peer of its
own — one shared `IPopupPeer` plus a `Timer`

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
| `InitialDelay` | `int` | `500` | Milliseconds the pointer must rest on a control before its tip appears; clamped to at least 1. |
| `AutoPopDelay` | `int` | `5000` | Milliseconds a visible tip stays up before hiding on its own; clamped to at least 1. |
| `Active` | `bool` (get) | `false` | Whether the tip popup is currently visible. |
| `SetToolTip(Control control, string? text)` | method | | Registers the hover text for a control, or removes the registration for a null/empty text. Backend-free — it may happen long before the control is realized. |
| `GetToolTip(Control control)` | method | | The registered hover text, or an empty string. |
| `Hide()` | method | | Hides the tip and stops any pending delay. |
| `Dispose()` | method | | Hides the tip, detaches every observed control and releases the native popup and timer. |

`ToolTip` is a component (`IDisposable`), not a control: one instance serves any number of
controls through a per-control text map, a single shared popup and a single delay timer.

## Notes

- **Lifecycle.** Pointer movement over a registered control (re)arms the `InitialDelay` timer;
  when it elapses the popup shows at the cursor position plus an 18 px vertical offset, sized to
  the text plus 4 px padding, and the timer re-arms with `AutoPopDelay`. The tip hides when the
  pointer leaves, a mouse button goes down, the auto-pop delay elapses, or the popup is dismissed.
  Leaving before the delay cancels the pending tip without ever creating a popup.
- **Owner-drawn controls only, for now.** Registration hooks the owner-drawn canvas mouse
  pipeline. Native-widget controls (Button, TextBox, …) register their text, but showing it needs
  either hover events on their peers or the platform tooltip API — tracked in
  [docs/PRD.md](../PRD.md) §7.6, as are per-item tips in lists, trees and grids.
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
- Tips currently show over owner-drawn controls only (see Notes).
