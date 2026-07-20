using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The <see cref="Strings"/> localization seam (PRD §8): every built-in user-facing string the
/// audit found flows through a settable provider — the search placeholder, the calendar day names
/// and month title, the <see cref="DateTimePicker"/> format provider, the implicit list-view group
/// header and the menu shortcut prefixes. No resources, no reflection; OS dialogs localize
/// themselves.
/// </summary>
[TestFixture]
internal sealed class LocalizationTests
{
    [TearDown]
    public void RestoreDefaults() => Strings.Reset();

    [Test]
    public void SearchBox_uses_the_placeholder_provider()
    {
        Strings.SearchPlaceholder = "Suchen";

        Assert.That(new SearchBox().PlaceholderText, Is.EqualTo("Suchen"));
    }

    [Test]
    public void MonthCalendar_paints_the_provided_day_names()
    {
        Strings.AbbreviatedDayNames = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var calendar = new MonthCalendar { Bounds = new(0, 0, 210, 180) };
        form.Controls.Add(calendar);
        form.RealizeWindow(backend);

        var g = ((HeadlessCanvasPeer)calendar.Peer!).RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Di"), Is.True);
            Assert.That(g.DrewText("Mi"), Is.True);
        });
    }

    [Test]
    public void DateTimePicker_formats_through_the_provider()
    {
        // "MMMM" next to a day number reads the genitive month names, so a localizing app sets both.
        string[] months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember", ""];
        var german = new DateTimeFormatInfo
        {
            DayNames = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"],
            MonthNames = months,
            MonthGenitiveNames = months,
        };
        Strings.DateTimeFormat = german;
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var picker = new DateTimePicker
        {
            Bounds = new(0, 0, 220, 24),
            Format = DateTimePickerFormat.Long,
            Value = new DateTime(2026, 3, 4),
        };
        form.Controls.Add(picker);
        form.RealizeWindow(backend);

        var g = ((HeadlessCanvasPeer)picker.Peer!).RaisePaint();

        Assert.That(g.DrewText("Mittwoch, 04 März 2026"), Is.True);
    }

    [Test]
    public void Menu_shortcut_prefixes_come_from_the_providers()
    {
        Strings.ShortcutControlPrefix = "Strg+";

        Assert.That(ToolStripMenuItem.FormatShortcut(Keys.Control | Keys.S), Is.EqualTo("Strg+S"));
    }

    [Test]
    public void AbbreviatedDayNames_rejects_wrong_counts_and_copies_on_assignment()
    {
        Assert.Throws<ArgumentException>(() => Strings.AbbreviatedDayNames = ["Mo", "Di"]);

        var names = new[] { "a", "b", "c", "d", "e", "f", "g" };
        Strings.AbbreviatedDayNames = names;
        names[0] = "mutated";

        Assert.That(Strings.AbbreviatedDayNames[0], Is.EqualTo("a"), "assignment took a defensive copy");
    }

    [Test]
    public void Reset_restores_every_default()
    {
        Strings.SearchPlaceholder = "x";
        Strings.DefaultListViewGroupHeader = "x";
        Strings.ShortcutControlPrefix = "x";
        Strings.ShortcutShiftPrefix = "x";
        Strings.ShortcutAltPrefix = "x";
        Strings.AbbreviatedDayNames = ["1", "2", "3", "4", "5", "6", "7"];
        Strings.DateTimeFormat = new DateTimeFormatInfo();

        Strings.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(Strings.SearchPlaceholder, Is.EqualTo("Search"));
            Assert.That(Strings.DefaultListViewGroupHeader, Is.EqualTo("Default"));
            Assert.That(Strings.ShortcutControlPrefix, Is.EqualTo("Ctrl+"));
            Assert.That(Strings.ShortcutShiftPrefix, Is.EqualTo("Shift+"));
            Assert.That(Strings.ShortcutAltPrefix, Is.EqualTo("Alt+"));
            Assert.That(Strings.AbbreviatedDayNames, Is.EqualTo(new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" }));
            Assert.That(Strings.DateTimeFormat, Is.EqualTo(CultureInfo.InvariantCulture));
        });
    }
}
