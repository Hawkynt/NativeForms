using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Where a light-dismiss popup actually lands on real GTK, and what putting one up does to the window
/// behind it.
///
/// The headless backend cannot express either. Its popup peer records the anchor it was handed and
/// reports it back unchanged, so a placement assertion against the fake only restates the request; and
/// it has no notion of window state at all, so it cannot notice a toplevel being pushed into its
/// backdrop appearance. Both need a display server that is free to disagree with the toolkit, so these
/// assertions drive real GTK: the form runs on the real loop, each drop-down is opened through its own
/// public API, and the popup's own <c>GdkWindow</c> is then asked where it ended up.
///
/// What is being guarded is a popup having a transient parent. Without one a <c>GTK_WINDOW_POPUP</c> is
/// an unrelated override-redirect toplevel: GDK says it "will not be able to position it on screen",
/// and GTK has no reason to keep the opener looking focused, so pulling down the application's own menu
/// greyed out every widget behind it.
///
/// Without a display the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed partial class GtkPopupPlacementTests
{
    /// <summary>Value of <c>GTK_STATE_FLAG_BACKDROP</c> — the "my toplevel is not the active window"
    /// styling every GTK theme dims its widgets with. The neighbouring bit, <c>DIR_LTR</c>, is set on
    /// essentially every widget, so testing the wrong one reads as backdrop everywhere.</summary>
    private const int _GtkStateFlagBackdrop = 1 << 6;

    /// <summary>How far a popup's reported origin may sit from the anchor it was shown at before the
    /// placement counts as wrong. Not zero: a compositor may nudge a surface to keep it on screen.</summary>
    private const int _AnchorTolerance = 4;

    /// <summary>The form's window title, which is how its toplevel is picked out of a process that
    /// runs several GTK fixtures side by side.</summary>
    private const string _FormTitle = "popup placement";

    private static Observations? _observed;
    private static string? _skipReason;

    /// <summary>What the run on the GTK loop saw; the tests only assert against it.</summary>
    private sealed class Observations
    {
        public bool ComboPopupHasTransientParent;
        public bool PickerPopupHasTransientParent;
        public Point ComboAnchor;
        public Point ComboOrigin;
        public Point PickerAnchor;
        public Point PickerOrigin;
        public bool BackdropBeforeAnyPopup;
        public bool BackdropBeforePickerPopup;
        public bool MainWindowBackdropWhileComboOpen;
        public bool MainWindowBackdropWhilePickerOpen;
        public string? Failure;
    }

    [OneTimeSetUp]
    public void RunTheFormOnce()
    {
        if (!OperatingSystem.IsLinux())
        {
            _skipReason = "GTK is only exercised on Linux.";
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            _skipReason = "No DISPLAY: these assertions need a real GTK display.";
            return;
        }

        BackendRegistry.Register(new GtkBackend());
        var observed = new Observations();

        var form = new Form { Text = _FormTitle, Width = 520, Height = 420 };
        var combo = new ComboBox { Bounds = new Rectangle(20, 200, 180, 26) };
        combo.Items.Add("Mercury");
        combo.Items.Add("Venus");
        combo.Items.Add("Earth");
        combo.SelectedIndex = 0;
        var picker = new DateTimePicker { Bounds = new Rectangle(240, 120, 220, 26) };
        form.Controls.Add(combo);
        form.Controls.Add(picker);

        var timer = new Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Observe(observed, combo, picker);
            }
            catch (Exception exception)
            {
                observed.Failure = exception.ToString();
            }
            finally
            {
                Application.Exit();
            }
        };
        timer.Start();

        var watchdog = new Timer { Interval = 20_000 };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            observed.Failure ??= "The GTK loop never reached the observation tick.";
            Application.Exit();
        };
        watchdog.Start();

        Application.Run(form);
        watchdog.Stop();
        _observed = observed;
    }

    /// <summary>Runs on the GTK loop with the form mapped: opens each drop-down and measures it.</summary>
    private static void Observe(Observations observed, ComboBox combo, DateTimePicker picker)
    {
        Pump();
        Settle();

        // The anchor is read from the control, not hard-coded: it is exactly the screen point the
        // control hands ShowAt, so comparing it against the surface's origin measures the toolkit
        // against itself rather than against a guess about where the window happened to be mapped.
        observed.ComboAnchor = combo.PointToScreen(new Point(0, combo.Height));

        // The form's own toplevel, found by its title. The backend's peer types are not visible from
        // here, so the handle cannot simply be asked for; and picking the toplevel that covers the
        // control would pick the wrong one, because sibling fixtures in this same assembly run their
        // own GTK forms of the very same size at the very same place on this bare display.
        var host = WindowTitled(_FormTitle);
        Assert.That(host, Is.Not.Zero, "the form's own toplevel could not be found");

        // Point the keyboard at the form first. These fixtures run on a bare display with no window
        // manager, where nothing ever takes the focus unless it is asked to, so an untouched window
        // sits in its backdrop state from the start — and a test that only looked at the state while
        // a popup was up would read that as the popup's doing. What is asserted is therefore the
        // change the popup makes, measured against the state immediately before it opened.
        Activate(host);

        observed.BackdropBeforeAnyPopup = IsBackdrop(host);
        var comboPopup = OpenAndCatchPopup(combo.OpenDropDown);
        observed.ComboPopupHasTransientParent = gtk_window_get_transient_for(comboPopup) == host;
        observed.ComboOrigin = OriginOf(comboPopup);
        observed.MainWindowBackdropWhileComboOpen = IsBackdrop(host);
        combo.CloseDropDown();
        Settle();

        Activate(host);
        observed.PickerAnchor = picker.PointToScreen(new Point(0, picker.Height));
        observed.BackdropBeforePickerPopup = IsBackdrop(host);
        var pickerPopup = OpenAndCatchPopup(picker.OpenDropDown);
        observed.PickerPopupHasTransientParent = gtk_window_get_transient_for(pickerPopup) == host;
        observed.PickerOrigin = OriginOf(pickerPopup);
        observed.MainWindowBackdropWhilePickerOpen = IsBackdrop(host);
        picker.CloseDropDown();
        Settle();
    }

    /// <summary>
    /// Runs an opening gesture and returns the popup toplevel it put up, identified as the one that
    /// was not there before.
    /// </summary>
    /// <remarks>
    /// Identified by difference rather than by name or by position in the toplevel list, because
    /// neither distinguishes anything: <c>gtk_widget_get_name</c> answers "GtkWindow" for a popup and
    /// for the form alike, and other fixtures in this assembly run their own GTK forms in the same
    /// process, so "the last toplevel" can belong to someone else entirely.
    /// </remarks>
    private static nint OpenAndCatchPopup(Action open)
    {
        var before = MappedPopups();
        open();
        Settle();

        foreach (var candidate in MappedPopups())
            if (!before.Contains(candidate))
                return candidate;

        Assert.Fail("the gesture put up no popup toplevel");
        return 0;
    }

    /// <summary>Every mapped <c>GTK_WINDOW_POPUP</c> toplevel in the process.</summary>
    private static HashSet<nint> MappedPopups()
    {
        var result = new HashSet<nint>();
        var toplevels = gtk_window_list_toplevels();
        var count = g_list_length(toplevels);
        for (var i = 0u; i < count; ++i)
        {
            var candidate = g_list_nth_data(toplevels, i);
            if (candidate != 0 && gtk_widget_get_mapped(candidate) != 0 && gtk_window_get_window_type(candidate) == _GtkWindowPopup)
                result.Add(candidate);
        }

        g_list_free(toplevels);
        return result;
    }

    /// <summary>
    /// Points the keyboard at a window and turns the loop until it reports itself active, so the
    /// backdrop reading taken next describes a focused window.
    /// </summary>
    /// <remarks>
    /// Asked repeatedly rather than once because there is no window manager on this display: the
    /// focus only ever moves where something explicitly puts it, and the request and the server's
    /// answer are several round trips apart.
    /// </remarks>
    private static void Activate(nint window)
    {
        for (var attempt = 0; attempt < 10 && gtk_window_is_active(window) == 0; ++attempt)
        {
            gtk_window_present(window);
            Settle();
        }
    }

    /// <summary>The mapped, ordinary top-level window carrying a given title, or zero.</summary>
    private static nint WindowTitled(string title)
    {
        var toplevels = gtk_window_list_toplevels();
        var count = g_list_length(toplevels);
        var found = (nint)0;
        for (var i = 0u; i < count && found == 0; ++i)
        {
            var candidate = g_list_nth_data(toplevels, i);
            if (candidate != 0
                && gtk_widget_get_mapped(candidate) != 0
                && gtk_window_get_window_type(candidate) == _GtkWindowToplevel
                && Marshal.PtrToStringUTF8(gtk_window_get_title(candidate)) == title)
                found = candidate;
        }

        g_list_free(toplevels);
        return found;
    }

    /// <summary>The screen position a toplevel's own <c>GdkWindow</c> reports.</summary>
    private static Point OriginOf(nint window)
    {
        var gdk = gtk_widget_get_window(window);
        Assert.That(gdk, Is.Not.Zero, "the popup toplevel has no GdkWindow");
        gdk_window_get_origin(gdk, out var x, out var y);
        return new(x, y);
    }

    /// <summary>Whether GTK is styling a widget as belonging to an inactive window.</summary>
    private static bool IsBackdrop(nint widget) => (gtk_widget_get_state_flags(widget) & _GtkStateFlagBackdrop) != 0;

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the GTK loop produced no observations");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    // --- Every popup names the window it belongs to ----------------------------------------------
    //
    // Before the fix gtk_window_get_transient_for answered zero for every surface and each opening
    // printed "temporary window without parent, application will not be able to position it on
    // screen" to stderr.

    [Test]
    public void ComboBox_drop_down_is_transient_for_the_window_that_opened_it()
        => Assert.That(Result().ComboPopupHasTransientParent, Is.True, "the drop-down floats unowned; GDK cannot anchor it");

    [Test]
    public void DateTimePicker_calendar_is_transient_for_the_window_that_opened_it()
        => Assert.That(Result().PickerPopupHasTransientParent, Is.True, "the calendar floats unowned; GDK cannot anchor it");

    // --- A popup lands where it was asked to -----------------------------------------------------

    [Test]
    public void ComboBox_drop_down_lands_at_the_anchor_it_was_shown_at()
    {
        var observed = Result();
        AssertNear(observed.ComboOrigin, observed.ComboAnchor, "the drop-down");
    }

    [Test]
    public void DateTimePicker_calendar_lands_at_the_anchor_it_was_shown_at()
    {
        var observed = Result();
        AssertNear(observed.PickerOrigin, observed.PickerAnchor, "the calendar");
    }

    private static void AssertNear(Point origin, Point anchor, string what)
        => Assert.That(
            Math.Abs(origin.X - anchor.X) <= _AnchorTolerance && Math.Abs(origin.Y - anchor.Y) <= _AnchorTolerance,
            Is.True,
            $"{what} was shown at {anchor} but its surface reports {origin}");

    // --- An application's own popup does not grey out its window ---------------------------------
    //
    // Before the fix the parentless popup read as a separate application window taking the focus, so
    // GTK put the gallery into GTK_STATE_FLAG_BACKDROP and every themed widget behind the menu — the
    // native labels most visibly — painted itself washed out.

    [Test]
    public void Opening_a_drop_down_does_not_push_its_window_into_the_backdrop_state()
    {
        var observed = Result();
        Assert.That(observed.BackdropBeforeAnyPopup, Is.False, "the form was already drawn as inactive before any popup — the observation is worthless");
        Assert.That(observed.MainWindowBackdropWhileComboOpen, Is.False, "opening the drop-down greyed out the window behind it");
    }

    [Test]
    public void Opening_a_calendar_does_not_push_its_window_into_the_backdrop_state()
    {
        var observed = Result();
        Assert.That(observed.BackdropBeforePickerPopup, Is.False, "the form was already drawn as inactive before the calendar — the observation is worthless");
        Assert.That(observed.MainWindowBackdropWhilePickerOpen, Is.False, "opening the calendar greyed out the window behind it");
    }

    // --- GTK plumbing the fixture needs -----------------------------------------------------------

    private static void Pump()
    {
        for (var i = 0; i < 400 && gtk_events_pending() != 0; ++i)
            gtk_main_iteration_do(0);
    }

    /// <summary>Turns the loop long enough for anything deferred to an idle or a timeout to run.</summary>
    private static void Settle()
    {
        for (var round = 0; round < 20; ++round)
        {
            Pump();
            g_usleep(5000);
        }

        Pump();
    }

    /// <summary>Value of <c>GTK_WINDOW_TOPLEVEL</c> — an ordinary application window.</summary>
    private const int _GtkWindowToplevel = 0;

    /// <summary>Value of <c>GTK_WINDOW_POPUP</c> — an undecorated floating surface.</summary>
    private const int _GtkWindowPopup = 1;

    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";

    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_window(nint widget);
    [LibraryImport(Gtk)] private static partial int gtk_widget_get_mapped(nint widget);
    [LibraryImport(Gtk)] private static partial int gtk_widget_get_state_flags(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_window_get_transient_for(nint window);
    [LibraryImport(Gtk)] private static partial nint gtk_window_get_title(nint window);
    [LibraryImport(Gtk)] private static partial int gtk_window_get_window_type(nint window);
    [LibraryImport(Gtk)] private static partial nint gtk_window_list_toplevels();
    [LibraryImport(Gtk)] private static partial void gtk_window_present(nint window);
    [LibraryImport(Gtk)] private static partial int gtk_window_is_active(nint window);
    [LibraryImport(Gtk)] private static partial int gtk_events_pending();
    [LibraryImport(Gtk)] private static partial int gtk_main_iteration_do(int blocking);

    [LibraryImport(Gdk)] private static partial void gdk_window_get_origin(nint window, out int x, out int y);
    [LibraryImport(Gdk)] private static partial int gdk_window_get_width(nint window);
    [LibraryImport(Gdk)] private static partial int gdk_window_get_height(nint window);

    [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
    [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
    [LibraryImport(GLib)] private static partial void g_usleep(nuint microseconds);
}
