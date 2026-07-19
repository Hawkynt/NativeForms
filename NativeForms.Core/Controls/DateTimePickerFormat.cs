namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="DateTimePicker"/> renders its value, matching
/// <c>System.Windows.Forms.DateTimePickerFormat</c>. All patterns use the invariant culture.</summary>
public enum DateTimePickerFormat
{
    /// <summary>The long date pattern: "dddd, dd MMMM yyyy".</summary>
    Long = 1,

    /// <summary>The short date pattern: "MM/dd/yyyy".</summary>
    Short = 2,

    /// <summary>The long time pattern: "HH:mm:ss".</summary>
    Time = 4,

    /// <summary>The pattern given by <see cref="DateTimePicker.CustomFormat"/>.</summary>
    Custom = 8,
}
