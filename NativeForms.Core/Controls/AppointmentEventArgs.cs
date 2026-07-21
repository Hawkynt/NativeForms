namespace Hawkynt.NativeForms;

/// <summary>Carries the appointment a <see cref="CalendarView"/> gesture acted on — the item the user
/// selected or asked to open for edit.</summary>
public sealed class AppointmentEventArgs(Appointment appointment) : EventArgs
{
    /// <summary>The appointment the gesture landed on.</summary>
    public Appointment Appointment { get; } = appointment;
}
