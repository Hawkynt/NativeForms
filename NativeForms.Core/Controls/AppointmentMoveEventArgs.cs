namespace Hawkynt.NativeForms;

/// <summary>
/// Proposes a reschedule of an appointment a <see cref="CalendarView"/> drag produced — a move or an
/// edge resize. It carries the appointment being moved (with its original bounds) and the proposed
/// new <see cref="Start"/>/<see cref="End"/>, snapped to the view's grid. The control owns no storage,
/// so it never mutates the appointment itself: it proposes the change through
/// <see cref="CalendarView.AppointmentMoving"/> (which a handler may <see cref="Cancel"/>) and, if the
/// move stands, reports it through <see cref="CalendarView.AppointmentMoved"/>; the application updates
/// its own model item and re-binds through <see cref="CalendarView.SetAppointments{T}"/> — the same
/// setter/validation idiom the grid uses for an edited cell.
/// </summary>
public sealed class AppointmentMoveEventArgs(Appointment appointment, DateTime start, DateTime end) : EventArgs
{
    /// <summary>The appointment the drag acted on, still carrying its <em>original</em> bounds and its
    /// <see cref="Appointment.Tag"/> back to the caller's model.</summary>
    public Appointment Appointment { get; } = appointment;

    /// <summary>Where the appointment started before the drag — a shorthand for
    /// <see cref="Appointment"/>.<see cref="Appointment.Start"/>.</summary>
    public DateTime OriginalStart => this.Appointment.Start;

    /// <summary>Where the appointment ended before the drag — a shorthand for
    /// <see cref="Appointment"/>.<see cref="Appointment.End"/>.</summary>
    public DateTime OriginalEnd => this.Appointment.End;

    /// <summary>The proposed new start instant, snapped to the view's grid.</summary>
    public DateTime Start { get; } = start;

    /// <summary>The proposed new end instant, snapped to the view's grid; the duration is preserved on a
    /// move and changed on an edge resize.</summary>
    public DateTime End { get; } = end;

    /// <summary>Set by an <see cref="CalendarView.AppointmentMoving"/> handler to veto the reschedule.
    /// Ignored for <see cref="CalendarView.AppointmentMoved"/>, which reports a move that already
    /// stands.</summary>
    public bool Cancel { get; set; }
}
