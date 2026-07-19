namespace Hawkynt.NativeForms;

/// <summary>Carries the selected day range of a <see cref="MonthCalendar"/>, matching
/// <c>System.Windows.Forms.DateRangeEventArgs</c>.</summary>
public sealed class DateRangeEventArgs(DateTime start, DateTime end) : EventArgs
{
    /// <summary>The first selected day.</summary>
    public DateTime Start { get; } = start;

    /// <summary>The last selected day.</summary>
    public DateTime End { get; } = end;
}
