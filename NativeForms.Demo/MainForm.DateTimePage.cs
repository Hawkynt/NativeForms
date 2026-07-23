using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Date &amp; Time page: a <see cref="MonthCalendar"/> driving all three per-day delegates
    /// (holiday shading, blocked weekends, hover tooltips), the <see cref="DateTimePicker"/> formats
    /// including one whose drop-down shades holidays, and the <see cref="TimePicker"/> precisions.
    /// </summary>
    private TabPage BuildDateTimePage()
    {
        var page = new TabPage("Date & Time") { ImageIndex = _IconPurple };

        // A shared holiday set and a weekend blackout, reused by the calendar and a picker.
        var holidays = new HashSet<(int Month, int Day)> { (7, 4), (7, 25), (12, 25) };
        static bool IsWeekend(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // --- Column 1: MonthCalendar with all three delegates ------------------------------------
        var calendar = new MonthCalendar { Bounds = new(16, 36, 232, 190) };
        calendar.DayBackgroundProvider = d => holidays.Contains((d.Month, d.Day)) ? Color.FromArgb(255, 255, 220, 220) : null;
        calendar.DateSelectable = d => !IsWeekend(d);
        calendar.DayTooltipProvider = d => holidays.Contains((d.Month, d.Day)) ? "Public holiday" : IsWeekend(d) ? "Weekend — not selectable" : null;
        calendar.DateSelected += (_, e) => this.SetStatus($"Calendar: {e.Start:yyyy-MM-dd} picked.");

        // --- Column 2: DateTimePicker formats ----------------------------------------------------
        var dtShort = new DateTimePicker { Bounds = new(340, 36, 200, 26), Format = DateTimePickerFormat.Short };
        dtShort.ValueChanged += (_, _) => this.SetStatus($"DateTimePicker: {dtShort.Value:d}.");
        var dtLong = new DateTimePicker { Bounds = new(340, 92, 240, 26), Format = DateTimePickerFormat.Long };
        var dtTime = new DateTimePicker { Bounds = new(340, 148, 140, 26), Format = DateTimePickerFormat.Time };

        var dtHoliday = new DateTimePicker { Bounds = new(340, 232, 200, 26), Format = DateTimePickerFormat.Short };
        dtHoliday.DayBackgroundProvider = d => holidays.Contains((d.Month, d.Day)) ? Color.FromArgb(255, 255, 220, 220) : null;
        dtHoliday.DateSelectable = d => !IsWeekend(d);
        dtHoliday.DayTooltipProvider = d => holidays.Contains((d.Month, d.Day)) ? "Public holiday" : null;

        // --- Column 3: TimePicker precisions -----------------------------------------------------
        var tFull = new TimePicker { Bounds = new(664, 36, 160, 26), Value = new(9, 30, 15) };
        var tNoSec = new TimePicker { Bounds = new(664, 92, 160, 26), Value = new(9, 30, 0), ShowSeconds = false };
        var tHours = new TimePicker { Bounds = new(664, 148, 120, 26), Value = new(8, 0, 0), ShowMinutes = false };
        var t12 = new TimePicker { Bounds = new(664, 204, 160, 26), Value = new(14, 15, 0), Use24HourClock = false, ShowSeconds = false };

        page.Controls.AddRange(
            Caption("MonthCalendar (holidays · no weekends · tips)", 16, 12, 300),
            calendar,
            Caption("DateTimePicker (Short / Long / Time)", 340, 12, 300),
            dtShort, dtLong, dtTime,
            Caption("DateTimePicker (holiday-shaded drop-down)", 340, 208, 300),
            dtHoliday,
            Caption("TimePicker (full / no seconds / hours / 12h)", 664, 12, 300),
            tFull, tNoSec, tHours, t12);

        this.Publish("datetime.calendar", calendar);
        this.Publish("datetime.short", dtShort);
        this.Publish("datetime.hoursOnly", tHours);
        return page;
    }
}
