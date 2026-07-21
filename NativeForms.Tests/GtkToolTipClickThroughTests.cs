using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The click that follows a visible tooltip. A tip floats on the same popup surface menus and
/// drop-downs use, and that surface arms light dismiss — a GDK seat grab plus a GTK grab that
/// redirect every press in the application to it. On a tip that is wrong: the user never aims at a
/// tip, so the grab spends their click closing it and the control they actually clicked neither
/// takes the focus nor sees the press. This fixture pins that a tip is click-through.
/// </summary>
/// <remarks>
/// Only real GTK can express this. Nothing in the managed model is wrong — a programmatic
/// <see cref="Control.Focus"/> in between always worked, because focus was never the thing that was
/// lost — so a headless assertion could only ever check the flag, not the grab. Input is injected
/// in-process as genuine <c>GdkEvent</c>s handed to <c>gtk_main_do_event</c>, the entry point the GDK
/// X11 backend itself calls; without a display the fixture reports itself ignored rather than passing
/// vacuously.
/// </remarks>
[TestFixture]
public sealed partial class GtkToolTipClickThroughTests
{
    private static Observations? _observed;
    private static string? _skipReason;

    /// <summary>Everything the run on the GTK loop recorded; the tests only assert against it.</summary>
    private sealed class Observations
    {
        public string? Failure;

        /// <summary>Whether the tip was up when the click under test was sent.</summary>
        public bool TipWasUp;

        /// <summary>Whether the plain box took the focus from the click that followed the tip.</summary>
        public bool PlainFocusedAfterClick;

        /// <summary>What the plain box holds after that click and one typed character.</summary>
        public string PlainAfterClickAndTyping = "<not run>";

        /// <summary>Whether the second box took the focus from the click that followed the first one.</summary>
        public bool SecondFocusedAfterClick;

        /// <summary>What the second box holds after that click and one typed character.</summary>
        public string SecondAfterClickAndTyping = "<not run>";
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

        var form = new Form { Text = "gtk tooltip click through", Width = 520, Height = 260 };

        // An owner-drawn control, because only a surface with no platform tip of its own floats the
        // toolkit's popup — the very surface whose grab is under test.
        var toggle = new ToggleSwitch { Bounds = new Rectangle(10, 10, 200, 24), Text = "Notifications" };
        var plain = new TextBox { Bounds = new Rectangle(10, 50, 300, 26), Text = "one" };
        var second = new TextBox { Bounds = new Rectangle(10, 90, 300, 26), Text = "two" };
        form.Controls.AddRange(toggle, plain, second);

        var tip = new ToolTip { InitialDelay = 60, AutoPopDelay = 30_000 };
        tip.SetToolTip(toggle, "An owner-drawn on/off switch.");

        var timer = new Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Exercise(observations, tip, toggle, plain, second);
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

    /// <summary>Runs on the GTK loop with the form on screen: raises a tip, then clicks elsewhere.</summary>
    private static void Exercise(Observations observations, ToolTip tip, Control hovered, TextBox plain, TextBox second)
    {
        Pump();
        var top = FindToplevel();
        Assert.That(top, Is.Not.Zero, "no GtkWindow toplevel");
        gtk_test_widget_wait_for_draw(top);
        Pump();

        var window = gtk_widget_get_window(top);
        Assert.That(window, Is.Not.Zero, "the toplevel has no GdkWindow");

        // Rest the pointer on the owner-drawn control until its tip floats.
        Motion(window, hovered.PointToScreen(new Point(hovered.Width / 2, hovered.Height / 2)));
        SpinUntil(() => tip.Active, 3000);
        observations.TipWasUp = tip.Active;

        // The click under test: the very next press while a tip is on screen.
        ClickInside(window, plain);
        observations.PlainFocusedAfterClick = plain.Focused;
        Type(window, "X");
        Pump();
        observations.PlainAfterClickAndTyping = plain.Text;

        // And the one after that, which the defect always left working — so a failure of the first
        // cannot be mistaken for injection that never worked at all.
        ClickInside(window, second);
        observations.SecondFocusedAfterClick = second.Focused;
        Type(window, "Y");
        Pump();
        observations.SecondAfterClickAndTyping = second.Text;
    }

    /// <summary>Pumps the loop until a condition holds or the budget runs out.</summary>
    private static void SpinUntil(Func<bool> until, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !until())
        {
            Pump();
            Thread.Sleep(10);
        }

        Pump();
    }

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the GTK loop produced no observations");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    // --- The defect -------------------------------------------------------------------------------

    [Test]
    public void A_tip_floats_over_the_owner_drawn_control()
        => Assert.That(Result().TipWasUp, Is.True, "the tip never appeared, so nothing was proved about the click after it");

    [Test]
    public void The_click_under_a_visible_tip_focuses_the_box_it_landed_on()
        => Assert.That(Result().PlainFocusedAfterClick, Is.True);

    [Test]
    public void The_click_under_a_visible_tip_leaves_the_box_it_landed_on_typable()
        => Assert.That(Result().PlainAfterClickAndTyping, Is.EqualTo("oneX"));


    [Test]
    public void A_further_click_keeps_working()
    {
        var observed = Result();
        Assert.Multiple(() =>
        {
            Assert.That(observed.SecondFocusedAfterClick, Is.True);
            Assert.That(observed.SecondAfterClickAndTyping, Is.EqualTo("twoY"));
        });
    }

    // --- GTK plumbing ----------------------------------------------------------------------------

    /// <summary>Moves, presses and releases in the middle of a control, then drains the loop.</summary>
    private static void ClickInside(nint window, Control control)
    {
        var screen = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
        Motion(window, screen);
        Pump();
        Button(window, _GdkButtonPress, screen, 0);
        Pump();
        Button(window, _GdkButtonRelease, screen, _Button1Mask);
        Pump();
    }

    /// <summary>
    /// Descends the GDK window tree the way the X server picks a delivery window: the topmost visible
    /// child containing the point wins, recursively.
    /// </summary>
    private static (nint Window, Point Local) Resolve(nint root, Point screen)
    {
        gdk_window_get_origin(root, out var originX, out var originY);
        var window = root;
        var local = new Point(screen.X - originX, screen.Y - originY);

        for (var depth = 0; depth < 32; ++depth)
        {
            var children = gdk_window_get_children(window);
            if (children == 0)
                break;

            var count = g_list_length(children);
            var hit = (nint)0;
            var hitPoint = Point.Empty;
            for (var i = 0u; i < count; ++i)
            {
                var child = g_list_nth_data(children, i);
                if (child == 0 || gdk_window_is_visible(child) == 0)
                    continue;

                gdk_window_get_position(child, out var childX, out var childY);
                var bounds = new Rectangle(childX, childY, gdk_window_get_width(child), gdk_window_get_height(child));
                if (!bounds.Contains(local))
                    continue;

                hit = child;
                hitPoint = new Point(local.X - childX, local.Y - childY);
                break;
            }

            g_list_free(children);
            if (hit == 0)
                break;

            window = hit;
            local = hitPoint;
        }

        return (window, local);
    }

    private static void Motion(nint root, Point screen)
    {
        var (window, local) = Resolve(root, screen);
        var evt = gdk_event_new(_GdkMotionNotify);
        unsafe
        {
            ref var motion = ref Unsafe.AsRef<GdkMotionEvent>((void*)evt);
            motion.Window = g_object_ref(window);
            motion.SendEvent = 1;
            motion.Time = _clock += 16;
            motion.X = local.X;
            motion.Y = local.Y;
            motion.XRoot = screen.X;
            motion.YRoot = screen.Y;
        }

        Dispatch(evt, Pointer());
    }

    private static void Button(nint root, int type, Point screen, uint state)
    {
        var (window, local) = Resolve(root, screen);
        var evt = gdk_event_new(type);
        unsafe
        {
            ref var press = ref Unsafe.AsRef<GdkButtonEvent>((void*)evt);
            press.Window = g_object_ref(window);
            press.SendEvent = 1;
            press.Time = _clock += 16;
            press.X = local.X;
            press.Y = local.Y;
            press.State = state;
            press.Button = 1;
            press.XRoot = screen.X;
            press.YRoot = screen.Y;
        }

        Dispatch(evt, Pointer());
    }

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

        Dispatch(evt, Keyboard());
    }

    private static void Dispatch(nint evt, nint device)
    {
        if (device != 0)
            gdk_event_set_device(evt, device);

        gtk_main_do_event(evt);
        gdk_event_free(evt);
    }

    private static nint Pointer()
    {
        var display = gdk_display_get_default();
        return display == 0 ? 0 : gdk_seat_get_pointer(gdk_display_get_default_seat(display));
    }

    private static nint Keyboard()
    {
        var display = gdk_display_get_default();
        return display == 0 ? 0 : gdk_seat_get_keyboard(gdk_display_get_default_seat(display));
    }

    /// <summary>The hardware keycode the live keymap maps a symbol to.</summary>
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

    private static uint _clock = 7000;

    private const int _GdkMotionNotify = 3;
    private const int _GdkButtonPress = 4;
    private const int _GdkButtonRelease = 7;
    private const int _GdkKeyPress = 8;
    private const int _GdkKeyRelease = 9;
    private const uint _Button1Mask = 1 << 8;

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

    /// <summary>The C layout of <c>GdkEventButton</c> up to the root coordinates.</summary>
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

    /// <summary>The C layout of <c>GdkEventMotion</c> up to the root coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkMotionEvent
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public uint Time;
        public double X;
        public double Y;
        public nint Axes;
        public uint State;
        public short IsHint;
        public nint Device;
        public double XRoot;
        public double YRoot;
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
    [LibraryImport(Gdk)] private static partial nint gdk_seat_get_pointer(nint seat);
    [LibraryImport(Gdk)] private static partial nint gdk_keymap_get_for_display(nint display);
    [LibraryImport(Gdk)] private static partial nint gdk_window_get_children(nint window);
    [LibraryImport(Gdk)] private static partial void gdk_window_get_position(nint window, out int x, out int y);
    [LibraryImport(Gdk)] private static partial int gdk_window_get_width(nint window);
    [LibraryImport(Gdk)] private static partial int gdk_window_get_height(nint window);
    [LibraryImport(Gdk)] private static partial int gdk_window_is_visible(nint window);
    [LibraryImport(Gdk)] private static partial void gdk_window_get_origin(nint window, out int x, out int y);

    [LibraryImport(Gdk)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gdk_keymap_get_entries_for_keyval(nint keymap, uint keyval, out nint keys, out int count);

    [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
    [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
    [LibraryImport(GObject)] private static partial nint g_object_ref(nint instance);
}
