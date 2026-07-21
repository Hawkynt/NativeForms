using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The lifetime of a light-dismiss popup on real GTK: it must survive the main loop going idle after
/// it opened, and it must close on a genuine press outside its surface.
///
/// The headless backend cannot express either half. Its popup peer neither takes a grab nor moves the
/// keyboard focus, so the very interaction that used to tear the surface down — GTK synthesising a
/// focus-out on the control underneath the moment <c>gtk_grab_add</c> redirects events to the popup —
/// simply does not exist in the fake. These assertions therefore drive real GTK: the form runs on the
/// real loop, the drop-down is opened through its own public API with no input at all, the loop is
/// then pumped to idle, and dismissal is exercised with a genuine <c>GdkEventButton</c> dispatched
/// through <c>gtk_main_do_event</c> at the main window's own coordinates.
///
/// Without a display the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed partial class GtkPopupLifetimeTests
{
    private const int _GdkButtonPress = 4;

    private static Observations? _observed;
    private static string? _skipReason;

    /// <summary>What the run on the GTK loop saw; the tests only assert against it.</summary>
    private sealed class Observations
    {
        public bool ComboOpenedImmediately;
        public bool ComboSurvivedIdle;
        public bool ComboLostFocusWhileOpening;
        public bool ComboClosedByOutsideClick;
        public bool PickerOpenedImmediately;
        public bool PickerSurvivedIdle;
        public bool PickerClosedByOutsideClick;
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

        var form = new Form { Text = "popup lifetime", Width = 520, Height = 420 };
        var combo = new ComboBox { Bounds = new Rectangle(20, 200, 180, 26) };
        combo.Items.Add("Mercury");
        combo.Items.Add("Venus");
        combo.Items.Add("Earth");
        combo.Items.Add("Mars");
        combo.SelectedIndex = 0;
        var picker = new DateTimePicker { Bounds = new Rectangle(240, 200, 220, 26) };
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

    /// <summary>Runs on the GTK loop with the form mapped: opens each drop-down, idles, then clicks out.</summary>
    private static void Observe(Observations observed, ComboBox combo, DateTimePicker picker)
    {
        Pump();
        var top = FindToplevel("GtkWindow");
        Assert.That(top, Is.Not.Zero, "no GtkWindow toplevel");
        gtk_test_widget_wait_for_draw(top);
        Pump();

        // The field must hold the focus first: it is precisely the focus the popup's grab shadows.
        combo.LostFocus += (_, _) =>
        {
            if (combo.DroppedDown)
                observed.ComboLostFocusWhileOpening = true;
        };

        combo.Focus();
        Settle();

        combo.OpenDropDown();
        observed.ComboOpenedImmediately = combo.DroppedDown;
        Settle();
        observed.ComboSurvivedIdle = combo.DroppedDown;
        // Open-before-and-closed-after, not merely closed: a surface that was already gone would
        // otherwise let the dismissal assertion pass without any dismissal happening.
        var comboWasOpen = combo.DroppedDown;
        ClickOnMainWindow(top);
        observed.ComboClosedByOutsideClick = comboWasOpen && !combo.DroppedDown;
        combo.CloseDropDown();
        Settle();

        picker.Focus();
        Settle();
        picker.OpenDropDown();
        observed.PickerOpenedImmediately = picker.DroppedDown;
        Settle();
        observed.PickerSurvivedIdle = picker.DroppedDown;
        var pickerWasOpen = picker.DroppedDown;
        ClickOnMainWindow(top);
        observed.PickerClosedByOutsideClick = pickerWasOpen && !picker.DroppedDown;
        picker.CloseDropDown();
        Settle();
    }

    /// <summary>
    /// Dispatches a genuine left press near the main window's own origin. That point is far outside
    /// every drop-down this fixture opens, yet in the popup window's own coordinate space it reads as
    /// a small positive offset — the exact geometry in which a naive "is this inside my allocation"
    /// test wrongly answers yes and refuses to dismiss.
    /// </summary>
    private static void ClickOnMainWindow(nint top)
    {
        var window = gtk_widget_get_window(top);
        var e = gdk_event_new(_GdkButtonPress);
        unsafe
        {
            ref var button = ref *(GdkButtonEvent*)e;
            button.Window = g_object_ref(window);
            button.SendEvent = 1;
            button.Time = 9000;
            button.X = 6;
            button.Y = 6;
            button.Button = 1;
            button.Device = gdk_seat_get_pointer(gdk_display_get_default_seat(gdk_display_get_default()));
            gdk_window_get_origin(window, out var rootX, out var rootY);
            button.XRoot = rootX + 6;
            button.YRoot = rootY + 6;
        }

        gtk_main_do_event(e);
        gdk_event_free(e);
        Settle();
    }

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the GTK loop produced no observations");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    // --- The drop-down survives the loop going idle ----------------------------------------------
    //
    // Before the fix both drop-downs reported DroppedDown immediately after OpenDropDown and false
    // again one settle later, with ComboLostFocusWhileOpening true: gtk_grab_add on the popup made
    // GTK synthesise a focus-out on the field, whose OnLostFocus closed the very popup that had just
    // taken the grab.

    [Test]
    public void ComboBox_drop_down_opens_at_once()
        => Assert.That(Result().ComboOpenedImmediately, Is.True);

    [Test]
    public void ComboBox_drop_down_survives_the_loop_going_idle()
        => Assert.That(Result().ComboSurvivedIdle, Is.True, "the drop-down closed itself once the main loop settled");

    [Test]
    public void ComboBox_does_not_close_itself_on_the_focus_change_its_own_grab_causes()
        => Assert.That(Result().ComboLostFocusWhileOpening, Is.False);

    [Test]
    public void DateTimePicker_drop_down_opens_at_once()
        => Assert.That(Result().PickerOpenedImmediately, Is.True);

    [Test]
    public void DateTimePicker_drop_down_survives_the_loop_going_idle()
        => Assert.That(Result().PickerSurvivedIdle, Is.True, "the calendar closed itself once the main loop settled");

    // --- A real outside press still dismisses ----------------------------------------------------

    [Test]
    public void ComboBox_drop_down_closes_on_a_press_over_the_main_window()
        => Assert.That(Result().ComboClosedByOutsideClick, Is.True);

    [Test]
    public void DateTimePicker_drop_down_closes_on_a_press_over_the_main_window()
        => Assert.That(Result().PickerClosedByOutsideClick, Is.True);

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

    private static nint FindToplevel(string typeName)
    {
        var toplevels = gtk_window_list_toplevels();
        var count = g_list_length(toplevels);
        var found = (nint)0;
        for (var i = 0u; i < count; ++i)
        {
            var candidate = g_list_nth_data(toplevels, i);
            if (Marshal.PtrToStringUTF8(gtk_widget_get_name(candidate)) == typeName)
                found = candidate;
        }

        g_list_free(toplevels);
        return found;
    }

    /// <summary>The full <c>GdkEventButton</c>, so an injected press carries root coordinates too.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkButtonEvent
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public uint Time;
        public double X;
        public double Y;
        public nint Axes;
        public uint State;
        public uint Button;
        public nint Device;
        public double XRoot;
        public double YRoot;
    }

    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_window(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_name(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_window_list_toplevels();
    [LibraryImport(Gtk)] private static partial int gtk_test_widget_wait_for_draw(nint widget);
    [LibraryImport(Gtk)] private static partial int gtk_events_pending();
    [LibraryImport(Gtk)] private static partial int gtk_main_iteration_do(int blocking);
    [LibraryImport(Gtk)] private static partial void gtk_main_do_event(nint @event);

    [LibraryImport(Gdk)] private static partial nint gdk_event_new(int type);
    [LibraryImport(Gdk)] private static partial void gdk_event_free(nint @event);
    [LibraryImport(Gdk)] private static partial void gdk_window_get_origin(nint window, out int x, out int y);
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default();
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default_seat(nint display);
    [LibraryImport(Gdk)] private static partial nint gdk_seat_get_pointer(nint seat);

    [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
    [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
    [LibraryImport(GLib)] private static partial void g_usleep(nuint microseconds);
    [LibraryImport(GObject)] private static partial nint g_object_ref(nint instance);
}
