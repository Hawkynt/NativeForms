# ClockFace

> A material-style analog time picker: a themed dial with a ring of hour numbers, a centre hub and a hand you click or drag onto a number, walking through hour → minute → seconds stages.

`Hawkynt.NativeForms.ClockFace` · strategy: **owner-drawn** (surface-agnostic dial engine) · peer: `ICanvasPeer`

`ClockFace` is the analog picker [`TimePicker`](timepicker.md) opens in a popup on a double-click, factored out as a public, reusable control. Like `CalendarCore` behind [`MonthCalendar`](monthcalendar.md)/[`DateTimePicker`](datetimepicker.md), it is deliberately dual: the familiar `OwnerDrawnControl` overrides let it stand alone in a form, while the public `Paint`/`Handle…` engine methods and the `Invalidated`/`Committed`/`Cancelled` callback slots let any host paint it into a light-dismiss popup and route input into it.

## Usage

```csharp
// Stand-alone in a form.
var clock = new ClockFace
{
    Bounds = new(20, 20, ClockFace.PreferredSize(theme).Width, ClockFace.PreferredSize(theme).Height),
    Value = new TimeSpan(9, 30, 0),
    Use24HourClock = false,
    ShowSeconds = true,
};
clock.ValueChanged += (_, _) => Console.WriteLine(clock.Value);
clock.Committed = () => Console.WriteLine($"Picked {clock.Value}");
form.Controls.Add(clock);
```

Hosting the same engine in a popup is exactly what `TimePicker` does — wire `Paint`, the `Handle…` methods and the callback slots to an `IPopupPeer` the way `DateTimePicker` wires `CalendarCore`.

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Value` | `TimeSpan` | the current time of day | The selected time — whole seconds, kept inside a single day. Setting it repaints and raises `ValueChanged` on a real change. |
| `OriginalValue` | `TimeSpan` | `00:00:00` | The value the dial opened on; a host reverts to it on cancel. Assigning it does not raise `ValueChanged`. |
| `Use24HourClock` | `bool` | `true` | Whether the hour ring is the two-ring 00–23 dial rather than a single 01–12 ring with an AM/PM toggle. |
| `ShowSeconds` | `bool` | `false` | Whether a seconds stage follows the minute. Turning it off while the seconds stage is active falls back to the minute. |
| `Stage` | `ClockFaceStage` | `Hour` | The hand the dial is editing — `Hour`, `Minute` or `Second`. Assigning `Second` while `ShowSeconds` is off falls back to `Minute`. |
| `FinalStage` | `ClockFaceStage` | — | The last stage of the current layout: `Second` when shown, else `Minute`. |

### Methods

| Method | Description |
|---|---|
| `Paint(IGraphics, ITheme, Size)` | Paints the whole dial into the given size — the engine hook a popup host calls. |
| `HandleMouseDown` / `HandleMouseMove` / `HandleMouseUp` / `HandleMouseWheel` / `HandleKeyDown` | Surface-agnostic input, taking the theme and size a host lays the dial out in. |
| `ClockFace.PreferredSize(ITheme)` | *(static)* The natural popup size for a dial painted with the theme: a square dial framed by the readout/AM-PM header and the OK footer. |

### Events & callbacks

| Name | Kind | Description |
|---|---|---|
| `ValueChanged` | `event` | Raised when `Value` changes, by gesture or assignment. |
| `StageChanged` | `event` | Raised when `Stage` changes. |
| `Invalidated` | `Action?` | Repaint request for a host surface; left `null` for a stand-alone control that repaints through its own canvas. |
| `Committed` | `Action?` | The user finalized — the OK affordance, or Enter on the final stage. |
| `Cancelled` | `Action?` | The user pressed Escape. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Stages, like the material picker.** A dial gesture on the hour ring sets the hour and, on release, advances to the minute; the minute advances to the seconds when `ShowSeconds` is on; the seconds stage is the last. A header segment (the `hh`/`mm`/`ss` readout, the active one accented) is clickable to jump straight to that stage, and the OK affordance in the footer — or Enter on the final stage — commits.
- **The 24-hour dial has two rings.** They share the twelve clock positions: the **inner** ring holds `00`–`11`, the **outer** ring `12`–`23`, so `00`/`12` sit at the top and `03`/`15` at three o'clock. A click picks the ring by how far from the centre it lands. This is the cleaner of the two obvious 24-hour layouts (the alternative — a single ring that the AM/PM state reinterprets — hides half the hours), and it matches the field's own `Use24HourClock` shape without an AM/PM toggle.
- **Minutes and seconds snap to a single unit.** The rings are labelled at the twelve five-unit marks (`00`, `05`, … `55`) but a click or drag rounds to the nearest single minute/second, so every value is reachable with a fine drag.
- **12-hour dials carry an AM/PM toggle** at the header's right edge; tapping it flips the half day, keeping the shown hour digits.
- **Keyboard**: arrows nudge the active hand one unit (wrapping within the part), Tab cycles the stage, Enter advances then commits on the final stage, Escape cancels.
- **No clamps of its own.** The dial edits a free time of day; a host (`TimePicker`) clamps the previewed/committed value into its own `MinTime`/`MaxTime` window. Keeping the dial unclamped is what lets it be reused with any windowing policy.
- **Zero per-frame allocation.** Every ring-number and readout string comes from shared static tables, the sixty dial directions from a shared sine/cosine table, and the hand endpoint is cached and rebuilt only on a stage/value/size change — so a steady repaint allocates nothing, at every stage. `ClockFaceTests` cover the rings, the stage machine, hit-testing, the AM/PM toggle, OK/commit, the keyboard and cancel; `AllocationBudgetTests` holds construction inside the owner-drawn budget and `PaintAllocationTests` pins zero bytes over 100 repaints at every stage.
- Painted with the platform `ITheme` (`FieldBackground` face, `ControlBackground` dial well, `Accent` hand/hub/selector, `SelectionBackground`/`SelectionText` for the active part, `ControlText`/`Border` for the rest).
- Done per [docs/PRD.md](../PRD.md) §7.5.

## Differences from System.Windows.Forms

WinForms has no analog time picker at all; the nearest thing is `DateTimePicker` with `Format = Time` and `ShowUpDown = true`, a digit spinner. `ClockFace` is a from-scratch owner-drawn dial in the platform theme, reusable stand-alone or hosted in a popup.
