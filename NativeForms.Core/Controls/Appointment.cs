using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// One scheduled item a <see cref="CalendarView"/> paints: a subject, a start/end instant (or an
/// all-day span), an optional location, a category <see cref="Color"/> and an opaque
/// <see cref="Tag"/> back to the caller's own model. A value type on purpose — the control keeps its
/// bound appointments in one flat array, so a hundred thousand of them cost one allocation rather
/// than a hundred thousand objects, and painting never boxes. The category colour is held as a packed
/// ARGB integer so the struct stays small; <see cref="Color"/> is the value-shaped face of it.
/// </summary>
public readonly struct Appointment
{
    private readonly int _argb;

    /// <summary>Creates an appointment. When <paramref name="allDay"/> the time-of-day parts of the
    /// bounds are ignored and the item paints in the all-day band / as a month chip. When
    /// <paramref name="movable"/> is <see langword="false"/> the appointment cannot be dragged to a new
    /// time and shows no move affordance — the hook for a locked entry such as a company holiday.</summary>
    public Appointment(
        string subject,
        DateTime start,
        DateTime end,
        bool allDay = false,
        string? location = null,
        Color color = default,
        object? tag = null,
        bool movable = true)
    {
        this.Subject = subject ?? string.Empty;
        this.Start = start;
        this.End = end;
        this.AllDay = allDay;
        this.Location = location ?? string.Empty;
        _argb = color.ToArgb();
        this.Tag = tag;
        this.Movable = movable;
    }

    /// <summary>The one-line title shown on the chip.</summary>
    public string Subject { get; init; }

    /// <summary>When the appointment begins.</summary>
    public DateTime Start { get; init; }

    /// <summary>When the appointment ends; treated as at or after <see cref="Start"/>.</summary>
    public DateTime End { get; init; }

    /// <summary>Whether the appointment spans whole days rather than a time range.</summary>
    public bool AllDay { get; init; }

    /// <summary>Whether the user may drag this appointment to a new time. <see langword="true"/> by
    /// default; set it <see langword="false"/> on the entries that must not move — a company holiday, a
    /// locked booking — so "move all" is the default and "only certain entries" is a matter of locking
    /// the rest. A non-movable appointment does not drag and paints no move affordance.</summary>
    public bool Movable { get; init; } = true;

    /// <summary>An optional secondary line (a room, a place) shown when the chip is tall enough.</summary>
    public string Location { get; init; }

    /// <summary>The category colour of the chip. <see cref="Color.Empty"/> (the default) paints in the
    /// theme accent, so an appointment without a category still reads as one.</summary>
    public Color Color
    {
        get => _argb == 0 ? Color.Empty : Color.FromArgb(_argb);
        init => _argb = value.ToArgb();
    }

    /// <summary>Whatever the caller wants to carry back through the events — the source model row.</summary>
    public object? Tag { get; init; }

    /// <summary>The clamped, non-negative duration of the appointment.</summary>
    internal TimeSpan Duration => this.End > this.Start ? this.End - this.Start : TimeSpan.Zero;
}
