# CalendarView

> An owner-drawn, virtualized Outlook-style scheduler — the scheduling counterpart of the `MonthCalendar` date *picker*. It shows bound appointments in a Day, Work Week, Week or Month view: the first three paint a vertical time grid (hour rows, a shaded work day, a "now" line, side-by-side overlapping appointments); the month view paints a six-week day grid with a chip per appointment and a "+n more" overflow marker.

`Hawkynt.NativeForms.CalendarView` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Why not `MonthCalendar`?

`MonthCalendar` is a **date picker**: a 6×7 grid of days you select a date or range in. `CalendarView` is a **scheduling surface**: it paints *appointments* along a time grid and switches between Day/Week/Month like Outlook's calendar. They share nothing but the word "calendar", so they are separate controls (and the internal `CalendarCore` engine that drives `MonthCalendar` and the `DateTimePicker` drop-down is untouched).

## Usage

```csharp
var calendar = new CalendarView { Bounds = new(0, 0, 900, 500), ViewMode = CalendarViewMode.Week };

// Reflection-free binding through a selector, exactly like DataGridView's columns.
calendar.SetAppointments(meetings, m => new Appointment(
    m.Subject, m.Start, m.End, allDay: m.AllDay, location: m.Room, color: m.CategoryColor, tag: m));

calendar.SelectionChanged   += (_, _) => Console.WriteLine(calendar.SelectedAppointment?.Subject);
calendar.AppointmentActivate += (_, e) => OpenEditor((Meeting)e.Appointment.Tag!);   // double-click / Enter
calendar.TimeRangeSelected  += (_, e) => NewAppointment(e.Start, e.End);             // drag empty time

// Drag a movable appointment to a new time: the control proposes, the app applies and re-binds.
calendar.AppointmentMoving += (_, e) => e.Cancel = ClashesWithSomething(e.Start, e.End);
calendar.AppointmentMoved  += (_, e) =>
{
    var m = (Meeting)e.Appointment.Tag!;
    m.Start = e.Start; m.End = e.End;   // update your own model
    calendar.SetAppointments(meetings, ToAppointment);   // then re-bind
};

form.Controls.Add(calendar);
```

## The `Appointment` model

`Appointment` is a small **value type**, so the control keeps its bound appointments in one flat array — a hundred thousand of them cost one allocation, not a hundred thousand objects, and painting never boxes.

| Member | Type | Description |
|---|---|---|
| `Subject` | `string` | The one-line title shown on the chip. |
| `Start` / `End` | `DateTime` | The span; `End` is treated as at or after `Start`. |
| `AllDay` | `bool` | When set, the item paints in the all-day band / as a month chip rather than on the time grid. |
| `Location` | `string` | An optional secondary line, shown when the chip is tall enough. |
| `Color` | `Color` | The category colour; `Color.Empty` (the default) paints in the theme accent. Stored internally as a packed ARGB int, so the struct stays ~48 B. |
| `Tag` | `object?` | Whatever the caller carries back through the events — the source model row. |
| `Movable` | `bool` | Whether the user may drag this appointment to a new time. `true` by default (so "move all" is the default); set it `false` on the locked entries (a company holiday, a fixed booking). A non-movable appointment does not drag and paints a small padlock instead of a move affordance. |

## Binding

The control does **not** own the caller's storage. `SetAppointments<T>(IEnumerable<T>, Func<T, Appointment>)` projects the source into one start-sorted snapshot the same reflection-free way the `DataGridView`'s column selectors do; a `SetAppointments(IEnumerable<Appointment>)` overload takes ready appointments. Mutating the source afterwards has no effect until the next `SetAppointments` call — the snapshot is a deliberate copy, which is what lets the layout and the bounded-lookup index stay valid without re-scanning on every repaint.

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `ViewMode` | `CalendarViewMode` | `Week` | `Day`, `WorkWeek` (Mon–Fri), `Week` (7 days from `FirstDayOfWeek`) or `Month`. |
| `SelectedDate` | `DateTime` | today | The date the view is centred on; assigning navigates there. |
| `TimeScale` | `int` | `30` | Minutes per time-grid slot (clamped 5–120). Hour lines are always drawn every 60 min. |
| `WorkDayStart` / `WorkDayEnd` | `TimeSpan` | 08:00 / 17:00 | The shaded work-day band on the time grid. |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | The leftmost column of the week and month views. |
| `Now` | `DateTime` | `DateTime.Now` | The instant the "now" line and today highlight read; settable, like `MonthCalendar.TodayDate`, so long-running views and tests stay deterministic. |
| `NowLineColor` | `Color` | alert red | The colour of the "now" line. |
| `VisibleDayCount` | `int` | — | The number of day columns the current view spans (1 / 5 / 7 / 7). |
| `FirstVisibleDate` | `DateTime` | — | The first date the current view shows. |
| `AppointmentCount` | `int` | `0` | The number of bound appointments. |
| `SelectedAppointment` | `Appointment?` | `null` | The selected appointment, or `null`. |
| `SelectedAppointmentIndex` | `int` | `-1` | Its index into the start-sorted snapshot. |

### Methods

| Name | Description |
|---|---|
| `SetAppointments<T>(IEnumerable<T>, Func<T, Appointment>)` | Binds by projecting each source item through the selector. |
| `SetAppointments(IEnumerable<Appointment>)` | Binds ready appointments. |
| `Next()` / `Previous()` | Page by the view's own unit — a day, a week, a month. |
| `GoToToday()` | Jump to `Now.Date`. |
| `TryGetAppointmentBounds(int index, out Rectangle)` | The client rectangle of a laid-out appointment (already translated for scroll) — the hook the demo and autopilot aim clicks at. |

### Events

| Name | Description |
|---|---|
| `SelectionChanged` | Raised when `SelectedAppointment` changes, by click or navigation. |
| `AppointmentActivate` | Raised on a double-click or Enter on a selected appointment — the open-for-edit hook; carries `AppointmentEventArgs.Appointment`. The control hosts no editor, it only reports the model. |
| `TimeRangeSelected` | Raised when the user click-drags across empty time (or empty month days), carrying the span as a `DateRangeEventArgs` (`Start`, `End`) — the "new appointment here" hook. |
| `AppointmentMoving` | Raised while a drag proposes moving or resizing a `Movable` appointment, before it is applied — **cancelable**. Carries the appointment, its `OriginalStart`/`OriginalEnd` and the snapped proposed `Start`/`End` (`AppointmentMoveEventArgs`). Set `Cancel = true` to veto; the snapshot then stays put. |
| `AppointmentMoved` | Raised after `AppointmentMoving` was not cancelled — the reschedule stands. Apply the proposed `Start`/`End` to your model item and re-bind through `SetAppointments`. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Views**: Day/WorkWeek/Week paint a vertical time grid — an hour gutter, half-hour (or `TimeScale`) grid lines, the `WorkDayStart`–`WorkDayEnd` band on the field colour with off-hours shaded, an all-day banner band under the day headers, and a red "now" line (dot + line) on today's column. Month paints a 6×7 day grid with the day-of-week header, out-of-month days greyed, today's number highlighted, and appointment chips per cell.
- **Overlap packing**: within a day column, appointments that overlap in time are laid **side by side** — the classic Outlook column packing. Each maximal cluster of mutually overlapping appointments is swept once, every appointment takes the first free column, and all members share the cluster's column count as their width. All-day appointments stack into the band on the first free row across the days they span.
- **Month overflow**: a day cell shows up to four chips; the rest collapse into a "+n more" marker so a busy day never spills its cell.
- **Interaction**: a click selects an appointment (`SelectionChanged`); a double-click or Enter raises `AppointmentActivate`; a click-drag on empty time (or empty month days) selects a range and raises `TimeRangeSelected`. The toolbar in the demo switches views and navigates; `SelectedDate`, `Next`/`Previous` and `GoToToday` do it in code. The keyboard moves the day/period (Left/Right, Up/Down, PageUp/PageDown, Home = today) and the wheel scrolls the time grid in Day/Week or pages the Month.
- **Moving appointments**: press on a `Movable` appointment's body and drag past a small threshold to reschedule it — a live translucent ghost shows where it will land, snapped to `TimeScale` in Day/Week and to the whole day in Month, with the original left in place until the drop. In Day/Week the top or bottom **edge** resizes the start or the end instead (Outlook-style), and hovering a resizable edge shows the north-south resize cursor. An edge follows the pointer's **day column** as well as its time, so in Week views dragging the start or end sideways carries it onto another day (the span is kept at least one slot). On drop the control raises the cancelable `AppointmentMoving` (a handler may veto or validate), then `AppointmentMoved`; **Escape** during the drag cancels, and a press that never crosses the threshold is a plain click that still selects. The control owns no storage, so it **never mutates the snapshot** — a move is a proposal the app applies to its model and re-binds with `SetAppointments`, exactly the grid's setter/validation idiom. A locked (`Movable = false`) appointment does not drag and shows a small padlock.
- **Multi-day & out-of-view appointments**: a timed appointment that spans more than one day is laid out on **every** day it overlaps, its box clamped to that day's `00:00`..`24:00`; a day where its real start or end falls off-view (an earlier or later day) paints a continuation **chevron** at the clamped edge. Only the box's **real** start/end edge offers a resize — a clamped continuation edge is dragged as a whole move, not an edge resize, so pulling the visible edge of a multi-day appointment never disturbs the end that sits off-screen. The same holds vertically: an appointment scrolled partly out of the time grid keeps its real bounds and only its on-screen edge is grabbable.
- **Virtualization & footprint**: appointments are held in one flat, start-sorted array; only the appointments intersecting the visible days are ever laid out, pulled by a binary search bounded by the widest appointment, so a set of a hundred thousand costs the same per frame as a set of ten. The overlap packing and pixel geometry are cached and rebuilt only when the data, the shown period, the view mode or the size changes — a plain repaint (the "now" line ticking, a hover, a vertical scroll) reuses the cached layout and every display string, so it allocates **nothing**. A live move/resize drag is allocation-free too: the ghost's geometry is recomputed each frame from reused value-type preview fields, and the drag's only allocation is the one `AppointmentMoveEventArgs` created on the drop. Measured: an empty control ≈ 624 B; an `Appointment` ≈ 48 B (the `Movable` flag packs into the struct's existing padding); 100 steady-state repaints of a populated week or month — with the move preview inactive **and** with an active drag preview on screen — allocate 0 B; a 100 000-appointment scroll traversal stays bounded.
- Painted with the platform `ITheme` (`FieldBackground` work hours, `HeaderBackground` band, `SelectionBackground`/`SelectionText` today, `Border`/`GridLine` grid, category colours blended toward the field for the chip faces, `Accent` for the selection outline and uncategorized chips).
- `CalendarViewTests` pin the defaults, the snapshot/sort, the selector binding, the view geometry, click selection and clear, double-click and Enter activation, the overlap packing, the empty-time drag range, the movable-appointment move (snapped `AppointmentMoving`/`AppointmentMoved` in Day, Week and day-granularity Month, plus a bottom-edge resize), the multi-day continuation clamping (start/middle/end-day clip flags, resizing a real edge while the off-view edge holds, a continuation edge moving rather than resizing, and an appointment starting before the visible range) with the edge resize cursor, the locked appointment refusing to drag, a cancelled proposal leaving the snapshot untouched, a sub-threshold press still selecting, Day/Week/Month navigation, the wheel behaviour, the time-grid and month painting, and the bounded layout for 100 000 appointments.
- Done per [docs/PRD.md](../PRD.md) §7.5.

## Differences from Outlook / `System.Windows.Forms`

- **No WinForms equivalent** — Windows Forms ships no appointment/scheduling view, so this is a new control shaped to fit the toolkit rather than a port. The binding follows the toolkit's own reflection-free selector idiom (`DataGridView`) rather than a `BindingSource`.
- **Snapshot binding**: appointments are copied into a start-sorted snapshot on `SetAppointments`; there is no live two-way `Items` collection. Re-call `SetAppointments` after the source changes.
- **Invariant, Monday-first** — day names come from `Strings.AbbreviatedDayNames` and `FirstDayOfWeek` defaults to `Monday`, like the rest of the toolkit. Existing appointments **can** be moved (and edge-resized) by drag, per-entry lockable through `Appointment.Movable`; recurring appointments and reminders are not implemented.
