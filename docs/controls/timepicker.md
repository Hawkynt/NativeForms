# TimePicker

> A time-of-day editor in the classic spinner shape: an owner-drawn field paints `Value` part by part with the caret's part highlighted, and the themed spinner column at the right edge steps exactly that part.

`Hawkynt.NativeForms.TimePicker` · strategy: **owner-drawn** (field + shared `SpinnerRenderer` button column) · peer: `ICanvasPeer`

## Usage

```csharp
var time = new TimePicker
{
    Bounds = new(20, 20, 160, 26),
    Value = new TimeSpan(9, 30, 0),
    MinTime = new TimeSpan(8, 0, 0),
    MaxTime = new TimeSpan(18, 0, 0),
};
time.ValueChanged += (_, _) => Console.WriteLine(time.Value);
form.Controls.Add(time);

time.SelectedField = TimePickerField.Minute;
time.UpButton();   // 09:30:00 -> 09:31:00
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Value` | `TimeSpan` | the current time of day | The picked time — whole seconds, clamped into [`MinTime`, `MaxTime`] and into a single day. |
| `MinTime` | `TimeSpan` | `00:00:00` | The earliest pickable time; assignments clamp to it and steps below it are refused. Throws `ArgumentOutOfRangeException` when negative, longer than a day, or later than `MaxTime`. |
| `MaxTime` | `TimeSpan` | `23:59:59` | The latest pickable time. Throws `ArgumentOutOfRangeException` when negative, longer than a day, or earlier than `MinTime`. |
| `ShowSeconds` | `bool` | `true` | Whether the field carries a seconds part. Turning it off drops the seconds from `Value` and moves a caret parked on them back to the minutes. |
| `Use24HourClock` | `bool` | `true` | Whether the hour part runs `00`–`23` rather than `01`–`12` with an AM/PM part. |
| `SelectedField` | `TimePickerField` | `Hour` | The part the caret sits on — what the spinner buttons, the Up/Down keys and the wheel step. Assigning a part the current layout hides falls back to `Hour`. |

### Methods

| Method | Description |
|---|---|
| `UpButton()` / `DownButton()` | Steps `SelectedField` one increment up/down. |
| `SelectPreviousField()` / `SelectNextField()` | Moves the caret one visible part left/right, stopping at the ends. |

### Events

| Name | Description |
|---|---|
| `ValueChanged` | Raised when `Value` changes, by stepping or assignment. Assigning the current value again does not re-raise. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Notes

- **No hosted editor, on purpose.** [`NumericUpDown`](numericupdown.md) hosts a native `TextBox` so caret and clipboard stay platform-native; a per-part caret cannot work that way, because a hosted native editor never reports back *where inside the text* a click landed. The field is therefore drawn and hit-tested here, part by part. The upside is that there is no free-form typed edit to validate: every change goes through a step, so `Value` is always well-formed and `ValueChanged` never fires for garbage.
- **Layout.** Parts sit left to right at the field's padding — `HH`, `:`, `mm`, then `:` `ss` while `ShowSeconds`, then a blank and `AM`/`PM` while `Use24HourClock` is off. Widths come from the platform text engine (`MeasureText`), which the peer contract binds to the same engine painting uses, so hit-testing and painting agree pixel for pixel. The two-digit strings `00`–`59` are materialized once statically, so painting allocates nothing per frame.
- **Stepping wraps within the part, without a carry** — `23:59` stepped up on the minute becomes `23:00`, not `00:00` of the next day — matching the Win32 date/time control. Stepping the AM/PM part moves the value by twelve hours either way.
- **Clamps.** Assignments clamp into [`MinTime`, `MaxTime`]; a *step* that would leave the window is **refused** rather than clamped, because a clamped step would leave the part showing a digit the user did not ask for. Home and End jump to the two edges of the window.
- **Autorepeat.** Clicking a spinner button steps once; holding it repeats through the shared `AutoRepeat` engine — 500 ms initial delay, then every 50 ms — exactly like `NumericUpDown`. Release, or the pointer leaving the control, stops it. The button column itself is the shared `Drawing.SpinnerRenderer` (`ScrollBarSize + 1` px wide), so it is pixel-identical to the up-down spinners.
- **Keyboard**: Left/Right move the caret between the visible parts (skipping hidden seconds or AM/PM, stopping at the ends), Up/Down step the selected part, Home/End jump to `MinTime`/`MaxTime`. The wheel steps the selected part too.
- **24-hour by default, explicitly.** The repository builds with `InvariantGlobalization`, and the invariant culture's short-time pattern is the 24-hour `HH:mm`, so `Use24HourClock` defaults to `true`. The default is spelled out in the control rather than probed from `Strings.DateTimeFormat`, so swapping that provider after a control was built cannot silently reshape an existing field.
- Painted with the platform `ITheme` (`FieldBackground` field, `SelectionBackground`/`SelectionText` for the part under the caret, `ControlText`/`DisabledText` for the rest, `Border` frame and seams). The part highlight shows only while the control has focus.
- `TimePickerTests` pin the defaults, the 12/24-hour and seconds layouts, part hit-testing, spinner stepping and autorepeat, the caret keys, wrap-without-carry, the clamp refusal, Home/End, the wheel and the hidden-part fallback. `AllocationBudgetTests` holds construction inside the owner-drawn budget and `PaintAllocationTests` pins zero bytes over 100 steady-state repaints.
- Done per [docs/PRD.md](../PRD.md) §7.5.

## Differences from System.Windows.Forms

WinForms has no `TimePicker`: the nearest thing is `DateTimePicker` with `Format = Time` and `ShowUpDown = true`, which edits the time part of a `DateTime`.

- **`Value` is a `TimeSpan`**, not a `DateTime` — a time of day has no date, and binding it to one only invites time-zone and DST questions the control cannot answer. Use [`DateTimePicker`](datetimepicker.md) when you need the date, or the [`DataGridViewColumnKind.TimePicker`](datagridview.md) column when you need a time per row.
- **No free-form `Format` string.** Per-part caret editing needs a layout the control can hit-test, so the pattern is derived from `ShowSeconds` and `Use24HourClock` instead of a `CustomFormat`. Everything WinForms' `Format = Time` can render, these two flags can render.
- **A step out of range is refused, not clamped** (WinForms clamps), and the parts wrap without carrying — the Win32 behavior, which WinForms' managed wrapper also exposes.
- No `ShowCheckBox`/`Checked` yet, and no `MinDate`/`MaxDate` — the window is `MinTime`/`MaxTime`.
