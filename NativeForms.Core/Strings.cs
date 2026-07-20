using System.Globalization;

namespace Hawkynt.NativeForms;

/// <summary>
/// The built-in user-facing strings the toolkit itself renders, gathered behind settable providers
/// so applications can localize without resources, satellite assemblies or reflection (PRD §8).
/// Dialog and message-box buttons come from the OS and need no translation here; what remains is
/// the audit result below. Defaults match the toolkit's historical (invariant English) values, and
/// providers are read at render time — set them before building the UI. Repository-wide
/// <c>InvariantGlobalization</c> means <see cref="DateTimeFormat"/> defaults to the invariant
/// culture; supply a hand-built <see cref="DateTimeFormatInfo"/> to localize month and day names.
/// </summary>
public static class Strings
{
    private static readonly string[] _DefaultDayNames = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    /// <summary>The placeholder a <see cref="SearchBox"/> shows while empty (read at construction).</summary>
    public static string SearchPlaceholder
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "Search";

    /// <summary>The header of the implicit <see cref="ListView"/> group holding ungrouped items.</summary>
    public static string DefaultListViewGroupHeader
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "Default";

    /// <summary>The Control-key prefix in rendered menu shortcut chords ("Ctrl+S").</summary>
    public static string ShortcutControlPrefix
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "Ctrl+";

    /// <summary>The Shift-key prefix in rendered menu shortcut chords.</summary>
    public static string ShortcutShiftPrefix
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "Shift+";

    /// <summary>The Alt-key prefix in rendered menu shortcut chords.</summary>
    public static string ShortcutAltPrefix
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "Alt+";

    /// <summary>
    /// The seven abbreviated day names the calendar header paints, indexed by
    /// <see cref="DayOfWeek"/> (Sunday first). Assigning copies the array, so later caller mutations
    /// do not leak into painting.
    /// </summary>
    /// <exception cref="ArgumentException">The array does not hold exactly seven names.</exception>
    public static string[] AbbreviatedDayNames
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 7)
                throw new ArgumentException($"Expected 7 day names (Sunday first), got {value.Length}.", nameof(value));

            field = (string[])value.Clone();
        }
    } = _DefaultDayNames;

    /// <summary>
    /// The format provider behind every date/time string the toolkit renders (the calendar's month
    /// title, <see cref="DateTimePicker"/> text). Typically a hand-built
    /// <see cref="DateTimeFormatInfo"/> carrying localized month/day names.
    /// </summary>
    public static IFormatProvider DateTimeFormat
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = CultureInfo.InvariantCulture;

    /// <summary>Restores every provider to its built-in default.</summary>
    public static void Reset()
    {
        SearchPlaceholder = "Search";
        DefaultListViewGroupHeader = "Default";
        ShortcutControlPrefix = "Ctrl+";
        ShortcutShiftPrefix = "Shift+";
        ShortcutAltPrefix = "Alt+";
        AbbreviatedDayNames = _DefaultDayNames;
        DateTimeFormat = CultureInfo.InvariantCulture;
    }
}
