namespace Hawkynt.NativeForms;

/// <summary>Which part of a <see cref="TimePicker"/> the caret sits on — the part the spinner
/// buttons and the Up/Down keys step.</summary>
public enum TimePickerField
{
    /// <summary>The hour part.</summary>
    Hour,

    /// <summary>The minute part.</summary>
    Minute,

    /// <summary>The second part; only reachable while <see cref="TimePicker.ShowSeconds"/> is on.</summary>
    Second,

    /// <summary>The AM/PM part; only reachable while <see cref="TimePicker.Use24HourClock"/> is off.</summary>
    Meridiem,
}
