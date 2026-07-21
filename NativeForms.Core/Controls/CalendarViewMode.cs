namespace Hawkynt.NativeForms;

/// <summary>
/// Which scheduling surface a <see cref="CalendarView"/> shows, matching the view switch of an
/// Outlook-style calendar. <see cref="Day"/>, <see cref="WorkWeek"/> and <see cref="Week"/> paint a
/// vertical time grid (hour rows, a "now" line, side-by-side overlapping appointments);
/// <see cref="Month"/> paints a day grid with appointment chips per cell.
/// </summary>
public enum CalendarViewMode
{
    /// <summary>One day as a vertical time grid.</summary>
    Day,

    /// <summary>The five work days of the week as adjacent time-grid columns.</summary>
    WorkWeek,

    /// <summary>Seven days from <see cref="CalendarView.FirstDayOfWeek"/> as time-grid columns.</summary>
    Week,

    /// <summary>The six-week day grid of one month, with appointment chips per day.</summary>
    Month,
}
