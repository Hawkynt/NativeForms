# MonthCalendar

> An owner-drawn one-month calendar page: a title row with paging arrows, a day-of-week header, and a 6×7 day grid with leading/trailing days greyed, today circled in the accent color, and single- or range-selection by mouse and keyboard. Clicking the title drills out to months, years and decades, so a distant year is three clicks away.

`Hawkynt.NativeForms.MonthCalendar` · strategy: **owner-drawn** (shares the `CalendarCore` engine with `DateTimePicker`'s popup) · peer: `ICanvasPeer`

## Usage

```csharp
var calendar = new MonthCalendar { Bounds = new(20, 20, 140, 176) };
calendar.DateSelected += (_, e) => Console.WriteLine($"{e.Start:d} .. {e.End:d}");
form.Controls.Add(calendar);

calendar.SetSelectionRange(new(2026, 7, 10), new(2026, 7, 12));
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `SelectionStart` | `DateTime` | today | The first selected day. Setting it keeps the end when still valid, collapsing or capping the range onto the new start otherwise; the displayed month follows. |
| `SelectionEnd` | `DateTime` | today | The last selected day. Setting it keeps the start when still valid, collapsing or capping the range onto the new end otherwise. |
| `MaxSelectionCount` | `int` | `7` | The largest number of days a selection may span. Coerced to at least 1; shrinking it trims the current range. |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | The day of week shown in the leftmost column. |
| `MinDate` | `DateTime` | `1753-01-01` | The earliest selectable day; earlier cells paint disabled and reject clicks. Throws `ArgumentOutOfRangeException` when set later than `MaxDate`. |
| `MaxDate` | `DateTime` | `9998-12-31` | The latest selectable day; later cells paint disabled and reject clicks. Throws `ArgumentOutOfRangeException` when set earlier than `MinDate`. |
| `DayBackgroundProvider` | `Func<DateTime, Color?>?` | `null` | Per-day background shading (holidays, deadlines) for in-month days; the selection still paints over it. |
| `DateSelectable` | `Func<DateTime, bool>?` | `null` | A predicate that blocks individual days from being picked (weekends, booked days) on top of `MinDate`/`MaxDate`; a rejected day paints disabled. |
| `DayTooltipProvider` | `Func<DateTime, string?>?` | `null` | Per-day tooltip text, shown on hover. |
| `TodayDate` | `DateTime` | today | The day wearing the accent circle. Settable, like its WinForms namesake, so long-running views and tests stay deterministic. |

### Methods

| Name | Description |
|---|---|
| `SetSelectionRange(DateTime start, DateTime end)` | Selects the range in one call, swapping reversed ends and capping the span at `MaxSelectionCount` days. |

### Events

| Name | Description |
|---|---|
| `DateChanged` | Raised whenever the selected range changes, by gesture or assignment; carries `DateRangeEventArgs` (`Start`, `End`). |
| `DateSelected` | Raised when the user commits a selection: the click gesture ends (mouse up) or Enter/Space lands. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Title drill-down**: the title is a button. Clicking it zooms one level out — day page → the twelve months of the year → the ten years of the decade → the ten decades of the century — and clicking a cell zooms back in one level, so switching to a year a decade away takes three clicks instead of 120 pagings. A drilled-out page drops the day-of-week header and lays twelve cells out 4×3 under the title; the decade and century pages pad with one neighbouring period at each end, greyed. The title arrows and the wheel then page by the shown unit (year, decade, century), the cell holding the selection is highlighted and the one holding today wears the accent circle, and `Min`/`MaxDate` grey and bounce any cell whose whole period lies outside the window. From the keyboard, Ctrl+Up drills out and Ctrl+Down drills back in; on a drilled-out page Left/Right step one cell, Up/Down one row of four, Home/End jump to the first and last cell of the period, PageUp/PageDown page the whole period, and Enter/Space drills into the focused cell. Only the day page selects — the drilled-out levels navigate. [`DateTimePicker`](datetimepicker.md)'s drop-down shares the engine, so it drills identically; it reopens on the day page every time.
- **Grid**: the top-left cell is the `FirstDayOfWeek` on or before the first of the displayed month (`CalendarCore.FirstGridDate`); the page is always 6 rows × 7 columns, with leading/trailing-month days in `DisabledText`. Title ("July 2026") and two-letter day names are invariant-culture strings, cached and materialized once so painting stays allocation-free.
- **Selection**: a click selects one day; Shift+click or dragging grows a range from the anchor, capped at `MaxSelectionCount` days. `DateChanged` fires per range change while the drag is in flight; `DateSelected` fires once when the gesture commits.
- **Min/Max**: out-of-range cells paint disabled and bounce clicks (no events); keyboard focus clamps to the window, and paging that would leave it entirely is refused.
- **Keyboard**: arrows move a focus day (±1 day, ±7 days) without touching the selection, following it across month boundaries; PageUp/PageDown page months, with Ctrl whole years; Home/End jump to the month edges; Enter/Space select the focus day. The mouse wheel and the title arrows page months too (wheel down = forward).
- Painted with the platform `ITheme` (`FieldBackground` page, `SelectionBackground`/`SelectionText` highlight, `Accent` today-circle and focus outline, `HeaderText` day names, `Border` frame). The focus outline shows only while the control has focus.
- Month captions on the drilled-out year page come from `Strings.AbbreviatedMonthNames` (invariant `Jan`–`Dec` by default, settable for localization, like `Strings.AbbreviatedDayNames`). The year and decade captions are built once per shown period and cached, so a drilled-out page repaints allocation-free too; a calendar the user never drills out of never allocates them at all.
- `MonthCalendarTests` pin the defaults, the grid-start math, invariant title/header painting, disabled greying, single/Shift/drag selection with the cap, min/max rejection, the full keyboard set, wheel/arrow paging, selection-follows-month, and the whole drill-down: both directions, the per-level paging unit, the Ctrl+arrow and Enter keyboard paths, the min/max greying and bouncing, and the today/selection marks.
- Done per [docs/PRD.md](../PRD.md) §7.5; bolded dates are not implemented yet.

## Differences from System.Windows.Forms.MonthCalendar

- **Invariant culture, Monday first** — WinForms localizes month/day names and takes the first day of week from the user's locale; here the title and day names render invariantly and `FirstDayOfWeek` defaults to `Monday` (settable per control).
- **No `SelectionRange` type**: the range is the `SelectionStart`/`SelectionEnd` pair plus `SetSelectionRange(start, end)` — same semantics, no wrapper object. Multi-day ranges, `MaxSelectionCount` and `TodayDate` behave as in WinForms. The title drill-down matches the classic control's month/year/decade zoom.
- **Always one month page** — no `CalendarDimensions` multi-month grid, no `ShowToday`/`ShowTodayCircle` toggles (today always wears the accent circle), no `BoldedDates` yet.
