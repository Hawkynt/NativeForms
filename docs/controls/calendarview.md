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

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Views**: Day/WorkWeek/Week paint a vertical time grid — an hour gutter, half-hour (or `TimeScale`) grid lines, the `WorkDayStart`–`WorkDayEnd` band on the field colour with off-hours shaded, an all-day banner band under the day headers, and a red "now" line (dot + line) on today's column. Month paints a 6×7 day grid with the day-of-week header, out-of-month days greyed, today's number highlighted, and appointment chips per cell.
- **Overlap packing**: within a day column, appointments that overlap in time are laid **side by side** — the classic Outlook column packing. Each maximal cluster of mutually overlapping appointments is swept once, every appointment takes the first free column, and all members share the cluster's column count as their width. All-day appointments stack into the band on the first free row across the days they span.
- **Month overflow**: a day cell shows up to four chips; the rest collapse into a "+n more" marker so a busy day never spills its cell.
- **Interaction**: a click selects an appointment (`SelectionChanged`); a double-click or Enter raises `AppointmentActivate`; a click-drag on empty time (or empty month days) selects a range and raises `TimeRangeSelected`. The toolbar in the demo switches views and navigates; `SelectedDate`, `Next`/`Previous` and `GoToToday` do it in code. The keyboard moves the day/period (Left/Right, Up/Down, PageUp/PageDown, Home = today) and the wheel scrolls the time grid in Day/Week or pages the Month.
- **Virtualization & footprint**: appointments are held in one flat, start-sorted array; only the appointments intersecting the visible days are ever laid out, pulled by a binary search bounded by the widest appointment, so a set of a hundred thousand costs the same per frame as a set of ten. The overlap packing and pixel geometry are cached and rebuilt only when the data, the shown period, the view mode or the size changes — a plain repaint (the "now" line ticking, a hover, a vertical scroll) reuses the cached layout and every display string, so it allocates **nothing**. Measured: an empty control ≈ 624 B; an `Appointment` ≈ 48 B; 100 steady-state repaints of a populated week or month allocate 0 B; a 100 000-appointment scroll traversal stays bounded.
- Painted with the platform `ITheme` (`FieldBackground` work hours, `HeaderBackground` band, `SelectionBackground`/`SelectionText` today, `Border`/`GridLine` grid, category colours blended toward the field for the chip faces, `Accent` for the selection outline and uncategorized chips).
- `CalendarViewTests` pin the defaults, the snapshot/sort, the selector binding, the view geometry, click selection and clear, double-click and Enter activation, the overlap packing, the empty-time drag range, Day/Week/Month navigation, the wheel behaviour, the time-grid and month painting, and the bounded layout for 100 000 appointments.
- Done per [docs/PRD.md](../PRD.md) §7.5.

## Differences from Outlook / `System.Windows.Forms`

- **No WinForms equivalent** — Windows Forms ships no appointment/scheduling view, so this is a new control shaped to fit the toolkit rather than a port. The binding follows the toolkit's own reflection-free selector idiom (`DataGridView`) rather than a `BindingSource`.
- **Snapshot binding**: appointments are copied into a start-sorted snapshot on `SetAppointments`; there is no live two-way `Items` collection. Re-call `SetAppointments` after the source changes.
- **Invariant, Monday-first** — day names come from `Strings.AbbreviatedDayNames` and `FirstDayOfWeek` defaults to `Monday`, like the rest of the toolkit; recurring appointments, reminders and drag-to-move of existing appointments are not implemented.
