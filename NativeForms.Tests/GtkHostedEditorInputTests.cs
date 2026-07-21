using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Regressions for the two things the headless backend cannot express about a control that hosts a
/// <em>native</em> editor inside an owner-drawn surface: where <see cref="Control.Focus"/> actually
/// lands, and whether a key the editor would otherwise swallow reaches the owning control.
///
/// Both only exist because focus is a platform concept. The headless fake has no focus chain and no
/// key routing of its own, so asserting either against it would only assert the fake. These tests
/// therefore drive real GTK: the form runs on the GTK loop, and input is injected in-process as
/// genuine <c>GdkEvent</c>s dispatched through <c>gtk_main_do_event</c>, which is exactly the entry
/// point the GDK X11 backend calls once it has translated an X event.
///
/// The fixture also pins the platform detail the masked box's edit derivation depends on: which
/// caret a <c>GtkEntry</c> reports while it is emitting "changed" for an insertion.
///
/// Without a display the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed partial class GtkHostedEditorInputTests
{
    private const uint _KeyReturn = 0xFF0D;
    private const uint _KeyHome = 0xFF50;
    private const uint _KeyEnd = 0xFF57;

    private static Observations? _observed;
    private static string? _skipReason;

    /// <summary>Everything the run on the GTK loop recorded; the tests only assert against it.</summary>
    private sealed class Observations
    {
        public string? Failure;

        /// <summary>What the search box holds after <c>Focus()</c> and four typed characters.</summary>
        public string SearchTextAfterFocus = "<not run>";

        /// <summary>How often Enter typed inside the hosted editor committed the search.</summary>
        public int CommitsFromTheEditor;

        /// <summary>Whether the character Enter was pressed on stayed out of the editor's content.</summary>
        public string SearchTextAfterEnter = "<not run>";

        /// <summary>The caret a GtkEntry reports while emitting "changed" for one inserted character
        /// typed at offset 1 of "abc" — 1 if it is the caret from before the edit, 2 if from after.</summary>
        public int CaretReportedDuringInsert = -1;

        /// <summary>The masked box after clicking to the end and typing ten digits.</summary>
        public string MaskedAfterTypingFromTheEnd = "<not run>";

        /// <summary>The masked box after going Home and typing the same ten digits again.</summary>
        public string MaskedAfterTypingFromHome = "<not run>";

        /// <summary>Whether the mask reported itself complete at the end.</summary>
        public bool MaskCompleted;
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
        var observations = new Observations();

        var form = new Form { Text = "gtk hosted editors", Width = 520, Height = 260 };

        var search = new SearchBox { Bounds = new Rectangle(10, 10, 300, 26) };
        search.SearchCommitted += (_, _) => ++observations.CommitsFromTheEditor;
        form.Controls.Add(search);

        var masked = new MaskedTextBox { Bounds = new Rectangle(10, 50, 300, 26), Mask = "(000) 000-0000" };
        form.Controls.Add(masked);

        var plain = new TextBox { Bounds = new Rectangle(10, 90, 300, 26), Text = "abc" };
        plain.TextChanged += (_, _) =>
        {
            if (observations.CaretReportedDuringInsert < 0)
                observations.CaretReportedDuringInsert = plain.SelectionStart;
        };
        form.Controls.Add(plain);

        var timer = new Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Exercise(observations, search, masked, plain);
            }
            catch (Exception exception)
            {
                observations.Failure = exception.ToString();
            }
            finally
            {
                Application.Exit();
            }
        };
        timer.Start();

        // A watchdog, so a machine where the window never maps ends the loop instead of hanging.
        var watchdog = new Timer { Interval = 20_000 };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            observations.Failure ??= "The GTK loop never reached the exercise tick.";
            Application.Exit();
        };
        watchdog.Start();

        Application.Run(form);
        watchdog.Stop();
        _observed = observations;
    }

    /// <summary>Runs on the GTK loop with the form on screen: focuses, types and records.</summary>
    private static void Exercise(Observations observations, SearchBox search, MaskedTextBox masked, TextBox plain)
    {
        Pump();
        var top = FindToplevel();
        Assert.That(top, Is.Not.Zero, "no GtkWindow toplevel");
        gtk_test_widget_wait_for_draw(top);
        Pump();

        var window = gtk_widget_get_window(top);
        Assert.That(window, Is.Not.Zero, "the toplevel has no GdkWindow");

        // The caret convention a GtkEntry reports while it emits "changed" for an insertion.
        plain.Focus();
        Pump();
        plain.Select(1, 0);
        Pump();
        Type(window, "x");
        Pump();

        // Focus() on the composite has to land on the hosted editor, or nothing typed arrives.
        search.Focus();
        Pump();
        Type(window, "grid");
        Pump();
        observations.SearchTextAfterFocus = search.Text;

        // Enter is the composite's key, not the editor's: it commits and never becomes content.
        Key(window, _KeyReturn);
        Pump();
        observations.SearchTextAfterEnter = search.Text;

        // Typing into the mask, from a caret parked past the last slot and again from the front.
        masked.Focus();
        Pump();
        Key(window, _KeyEnd);
        Pump();
        Type(window, "5551234567");
        Pump();
        observations.MaskedAfterTypingFromTheEnd = masked.Text;

        Key(window, _KeyHome);
        Pump();
        Type(window, "5551234567");
        Pump();
        observations.MaskedAfterTypingFromHome = masked.Text;
        observations.MaskCompleted = masked.MaskCompleted;
    }

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the GTK loop produced no observations");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    // --- Defect 1: Focus() on a composite must reach the widget that takes text ------------------

    [Test]
    public void Focus_on_a_SearchBox_lands_on_the_hosted_editor()
    {
        // Before the fix the key handlers lived on the owner-drawn canvas while the keyboard went
        // nowhere, so every typed character was lost and the box stayed empty.
        Assert.That(Result().SearchTextAfterFocus, Is.EqualTo("grid"));
    }

    // --- Defect 2: a key the editor does not own must reach the owning composite -----------------

    [Test]
    public void Enter_typed_inside_the_hosted_editor_commits_the_search()
    {
        // Before the peer key seam existed, Enter vanished into the GtkEntry and SearchCommitted
        // could only ever fire for a key pressed on the painted surface around it.
        Assert.That(Result().CommitsFromTheEditor, Is.EqualTo(1));
    }

    [Test]
    public void The_claimed_Enter_never_reaches_the_editor_as_content()
        => Assert.That(Result().SearchTextAfterEnter, Is.EqualTo("grid"));

    // --- The platform detail the mask derivation rests on ----------------------------------------

    [Test]
    public void A_GtkEntry_reports_the_pre_edit_caret_while_it_emits_changed()
    {
        // GtkEntry advances its caret only after gtk_editable_insert_text has returned, so the
        // caret observable from a "changed" handler is the one from before the insertion. The
        // masked box's edit derivation treats it as exactly that; this pins the assumption.
        Assert.That(Result().CaretReportedDuringInsert, Is.EqualTo(1));
    }

    // --- The mask, typed into on a real widget ---------------------------------------------------

    [Test]
    public void Typing_ten_digits_from_the_end_of_the_mask_fills_it()
        => Assert.That(Result().MaskedAfterTypingFromTheEnd, Is.EqualTo("(555) 123-4567"));

    [Test]
    public void Typing_the_same_ten_digits_again_from_Home_reproduces_the_rendering()
    {
        var observed = Result();
        Assert.Multiple(() =>
        {
            Assert.That(observed.MaskedAfterTypingFromHome, Is.EqualTo("(555) 123-4567"));
            Assert.That(observed.MaskCompleted, Is.True);
        });
    }

    // --- GTK plumbing ----------------------------------------------------------------------------

    /// <summary>Types each character as a key press/release pair aimed at the focused widget.</summary>
    private static void Type(nint window, string text)
    {
        foreach (var c in text)
        {
            Key(window, c);
            Pump();
        }
    }

    /// <summary>Sends one press and one release of a key symbol to the toplevel's GdkWindow.</summary>
    private static void Key(nint window, uint keyval)
    {
        KeyEvent(window, _GdkKeyPress, keyval);
        KeyEvent(window, _GdkKeyRelease, keyval);
    }

    private static void KeyEvent(nint window, int type, uint keyval)
    {
        var display = gdk_display_get_default();
        var keyboard = display == 0 ? 0 : gdk_seat_get_keyboard(gdk_display_get_default_seat(display));

        var evt = gdk_event_new(type);
        unsafe
        {
            ref var key = ref Unsafe.AsRef<GdkKeyEvent>((void*)evt);
            key.Window = g_object_ref(window);
            key.SendEvent = 1;
            key.Time = _clock += 16;
            key.KeyVal = keyval;
            key.HardwareKeycode = KeycodeFor(keyval);
        }

        if (keyboard != 0)
            gdk_event_set_device(evt, keyboard);

        gtk_main_do_event(evt);
        gdk_event_free(evt);
    }

    /// <summary>The hardware keycode the live keymap maps a symbol to; input methods consult it
    /// alongside the symbol, so leaving it at zero can make a key look synthetic.</summary>
    private static ushort KeycodeFor(uint keyval)
    {
        var keymap = gdk_keymap_get_for_display(gdk_display_get_default());
        if (keymap == 0 || !gdk_keymap_get_entries_for_keyval(keymap, keyval, out var keys, out var count) || count <= 0)
            return 0;

        unsafe
        {
            var keycode = (ushort)Unsafe.AsRef<GdkKeymapKey>((void*)keys).Keycode;
            g_free(keys);
            return keycode;
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 400 && gtk_events_pending() != 0; ++i)
            gtk_main_iteration_do(0);
    }

    private static nint FindToplevel()
    {
        var toplevels = gtk_window_list_toplevels();
        var count = g_list_length(toplevels);
        var found = (nint)0;
        for (var i = 0u; i < count; ++i)
        {
            var candidate = g_list_nth_data(toplevels, i);
            if (Marshal.PtrToStringUTF8(gtk_widget_get_name(candidate)) == "GtkWindow")
                found = candidate;
        }

        g_list_free(toplevels);
        return found;
    }

    private static uint _clock = 3000;

    private const int _GdkKeyPress = 8;
    private const int _GdkKeyRelease = 9;

    /// <summary>The C layout of <c>GdkEventKey</c> up to the keyboard group.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkKeyEvent
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public uint Time;
        public uint State;
        public uint KeyVal;
        public int Length;
        public nint String;
        public ushort HardwareKeycode;
        public byte Group;
        public uint IsModifier;
    }

    /// <summary>One entry of the array <c>gdk_keymap_get_entries_for_keyval</c> fills.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkKeymapKey
    {
        public uint Keycode;
        public int Group;
        public int Level;
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
    [LibraryImport(Gdk)] private static partial void gdk_event_set_device(nint @event, nint device);
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default();
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default_seat(nint display);
    [LibraryImport(Gdk)] private static partial nint gdk_seat_get_keyboard(nint seat);
    [LibraryImport(Gdk)] private static partial nint gdk_keymap_get_for_display(nint display);

    [LibraryImport(Gdk)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gdk_keymap_get_entries_for_keyval(nint keymap, uint keyval, out nint keys, out int count);

    [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
    [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
    [LibraryImport(GObject)] private static partial nint g_object_ref(nint instance);
}
