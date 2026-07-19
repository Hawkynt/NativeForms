# DateTimePicker

> A date field in the ComboBox shape: a closed, owner-drawn field showing `Value` in an invariant format with a drop arrow, opening a light-dismiss popup that hosts the same month page as `MonthCalendar`.

`Hawkynt.NativeForms.DateTimePicker` · strategy: **owner-drawn** (field + popup share the `CalendarCore` engine) · peer: `ICanvasPeer` + `IPopupPeer`

## Usage

```csharp
var picker = new DateTimePicker { Bounds = new(20, 20, 180, 24), Format = DateTimePickerFormat.Short };
picker.ValueChanged += (_, _) => Console.WriteLine(picker.Value);
form.Controls.Add(picker);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Value` | `DateTime` | `DateTime.Now` | The picked date and time, clamped into [`MinDate`, `MaxDate`] on assignment. |
| `MinDate` | `DateTime` | `1753-01-01` | The earliest pickable date; assignments and steps clamp to it. Throws `ArgumentOutOfRangeException` when set later than `MaxDate`. |
| `MaxDate` | `DateTime` | `9998-12-31` | The latest pickable date; assignments and steps clamp to it. Throws `ArgumentOutOfRangeException` when set earlier than `MinDate`. |
| `Format` | `DateTimePickerFormat` | `Long` | How the closed field renders `Value` — always in the invariant culture: `Long` = `dddd, dd MMMM yyyy`, `Short` = `MM/dd/yyyy`, `Time` = `HH:mm:ss`, `Custom` = `CustomFormat`. |
| `CustomFormat` | `string` | `""` | The invariant pattern used while `Format` is `Custom`; empty renders an empty field. |
| `ShowCheckBox` | `bool` | `false` | Whether the field carries a check box in front of the text. |
| `Checked` | `bool` | `true` | Whether the value applies. While `ShowCheckBox` is on and this is off, the text greys and every value-changing gesture is suppressed. |
| `DroppedDown` | `bool` (read-only) | `false` | Whether the calendar popup is currently open. |

### Methods

| Name | Description |
|---|---|
| `OpenDropDown()` | Opens the calendar popup below the field, its page centered on `Value`. A no-op while already open or before realization. |
| `CloseDropDown()` | Closes the popup without committing. A no-op while closed. |

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes, by user gesture or assignment (after clamping). |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **Invariant culture, deliberately**: every format pattern renders with `CultureInfo.InvariantCulture`, so the field paints identically on every machine; the formatted text is cached between value changes to keep painting allocation-free.
- **Popup calendar**: the drop-down hosts the same `CalendarCore` month page as [`MonthCalendar`](monthcalendar.md), so both surfaces stay pixel- and behavior-identical. Clicking a day commits it into `Value` **preserving the time of day** and raises `ValueChanged` once; Escape, F4 or a click elsewhere (light dismissal) cancels without committing. Losing focus also closes the popup.
- **Keyboard**: closed, Up/Down step the day by ±1 (refusing steps that would leave [`MinDate`, `MaxDate`]) and Alt+Down or F4 opens the calendar; open, the calendar owns navigation (arrows, PageUp/PageDown, Home/End) and Enter commits. The classic control steps whichever date part is focused — per-part focus is not implemented yet.
- **ShowCheckBox**: the box paints via the shared `GlyphRenderer`; clicking its zone toggles `Checked` instead of opening the popup. Unchecked, the text paints in `DisabledText` and stepping and popup commits are suppressed (the popup still closes).
- **Clamps interact**: raising `MinDate` above `Value` drags the value up (raising `ValueChanged`); the same for `MaxDate` downward.
- `DateTimePickerTests` pin the defaults, all four formats, the popup position and size, commit-preserves-time, dismissal/Escape, keyboard navigation, day stepping at the clamps, the check-box greying/suppression and the mutual clamping.
- Partially done per [docs/PRD.md](../PRD.md) §7.5: `BoldedDates`, per-part focus and time spinner editing are pending.
