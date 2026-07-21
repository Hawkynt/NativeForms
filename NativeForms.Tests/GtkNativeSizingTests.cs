using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Regressions for two GTK defects the headless backend cannot express, asserted against real GTK
/// widgets on a real display.
///
/// The headless fake records whatever the core pushes, so a peer's bounds are its bounds by
/// construction: it has no notion of a container handing a widget a different size than the one it
/// was asked for, which is exactly the defect here. Nor does it carry GDK event masks or GDK's two
/// wheel encodings. Modelling either in the fake would only assert the fake, so these tests drive the
/// real toolkit instead: the form is shown on the running loop, every measurement is taken from GTK
/// itself (<c>gtk_widget_get_allocation</c>, <c>gdk_window_get_events</c>) and input is injected
/// in-process as genuine <c>GdkEvent</c>s dispatched through <c>gtk_main_do_event</c>.
///
/// Without a display the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed partial class GtkNativeSizingTests
{
    private const int _GdkScroll = 31;
    private const int _GdkMotionNotify = 3;
    private const int _GdkScrollDown = 1;
    private const int _GdkScrollSmooth = 4;
    private const int _GdkScrollMask = 1 << 21;
    private const int _GdkSmoothScrollMask = 1 << 23;

    /// <summary>Everything the run on the GTK loop measured; the tests only assert against it.</summary>
    private static Measurements? _measured;

    private static string? _skipReason;

    /// <summary>One measured widget: the rectangle the toolkit asked for and the one GTK allocated.</summary>
    private sealed record Measured(Rectangle Requested, Rectangle Allocated, int EventMask);

    private sealed class Measurements
    {
        public Measured? NarrowButton;
        public Measured? WideButton;
        public Measured? Label;
        public Measured? TextBox;
        public int DiscreteWheelOverChild;
        public int SmoothWheelOverChild;
        public int SmoothWheelOverPanel;
        public int ChildEventMask;

        /// <summary>The GTK allocation of a child whose logical bounds overhang the vertical scrollbar.</summary>
        public Rectangle OverhangingChild;

        /// <summary>The x the panel's scrollbar band starts at — everything right of it must stay clear.</summary>
        public int StripLeft;

        /// <summary>The platform tooltip GTK reports on a native button after its tip was raised.</summary>
        public string? NativeToolTipText;

        /// <summary>Whether a child scrolled completely below the viewport is still mapped.</summary>
        public bool OffscreenChildMapped;

        /// <summary>Whether that same child is mapped again once it is scrolled back into view.</summary>
        public bool ScrolledBackChildMapped;

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
        var measurements = new Measurements();

        var form = new Form { Text = "gtk sizing", Width = 620, Height = 480 };

        // Leaves whose natural size differs from the bounds the toolkit assigns them.
        var narrow = new Button { Text = "Flow 12", Bounds = new Rectangle(10, 10, 60, 26) };
        var wide = new Button { Text = "OK", Bounds = new Rectangle(10, 50, 200, 30) };
        var label = new Label { Text = "A rather long label caption", Bounds = new Rectangle(10, 90, 70, 20) };
        var textBox = new TextBox { Text = "some text", Bounds = new Rectangle(10, 120, 50, 18) };
        form.Controls.Add(narrow);
        form.Controls.Add(wide);
        form.Controls.Add(label);
        form.Controls.Add(textBox);

        // A native child inside a panel that scrolls.
        var panel = new Panel { Bounds = new Rectangle(300, 10, 280, 200), AutoScroll = true };
        var inner = new Button { Text = "Inner", Bounds = new Rectangle(10, 10, 90, 30) };
        panel.Controls.Add(inner);
        panel.Controls.Add(new Button { Text = "Tall bottom", Bounds = new Rectangle(10, 500, 120, 30) });

        // Placed at absolute coordinates so the layout engine never touches it: its logical right
        // edge (300) reaches past the panel's viewport and into the vertical scrollbar's band.
        panel.Controls.Add(new Button { Text = "Overhang", Bounds = new Rectangle(200, 60, 100, 30) });
        form.Controls.Add(panel);

        // A native button carrying a tip, to prove the platform tooltip is what gets raised.
        var tipped = new Button { Text = "Tipped", Bounds = new Rectangle(10, 160, 90, 28) };
        form.Controls.Add(tipped);
        var toolTip = new ToolTip { InitialDelay = 40 };
        toolTip.SetToolTip(tipped, "a native tip");

        var timer = new Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Measure(measurements, panel);
            }
            catch (Exception exception)
            {
                measurements.Failure = exception.ToString();
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
            measurements.Failure ??= "The GTK loop never reached the measurement tick.";
            Application.Exit();
        };
        watchdog.Start();

        Application.Run(form);
        watchdog.Stop();
        _measured = measurements;
    }

    /// <summary>Runs on the GTK loop with the form on screen: reads GTK's own numbers and injects wheels.</summary>
    private static void Measure(Measurements measurements, Panel panel)
    {
        Pump();
        var top = FindToplevel();
        Assert.That(top, Is.Not.Zero, "no GtkWindow toplevel");
        gtk_test_widget_wait_for_draw(top);
        Pump();

        var widgets = new List<(string Name, nint Handle)>();
        Walk(top, widgets);

        nint ButtonLabelled(string text)
        {
            foreach (var (name, handle) in widgets)
                if (name == "GtkButton" && Marshal.PtrToStringUTF8(gtk_button_get_label(handle)) == text)
                    return handle;

            return 0;
        }

        nint OfType(string type, int skip)
        {
            foreach (var (name, handle) in widgets)
                if (name == type && skip-- == 0)
                    return handle;

            return 0;
        }

        var narrow = ButtonLabelled("Flow 12");
        var inner = ButtonLabelled("Inner");
        measurements.NarrowButton = Snapshot(narrow, new Rectangle(10, 10, 60, 26));
        measurements.WideButton = Snapshot(ButtonLabelled("OK"), new Rectangle(10, 50, 200, 30));

        // Index 2: the first two GtkLabels are the captions inside the two buttons above.
        measurements.Label = Snapshot(OfType("GtkLabel", 2), new Rectangle(10, 90, 70, 20));
        measurements.TextBox = Snapshot(OfType("GtkEntry", 0), new Rectangle(10, 120, 50, 18));

        var canvas = gtk_widget_get_parent(inner);
        var canvasWindow = gtk_widget_get_window(canvas);
        var childWindow = EventWindowOf(inner, canvasWindow);
        Assert.That(childWindow, Is.Not.Zero, "the native child has no GdkWindow of its own");
        measurements.ChildEventMask = gdk_window_get_events(childWindow);

        measurements.DiscreteWheelOverChild = WheelAndRead(panel, childWindow, _GdkScrollDown, 0);
        measurements.SmoothWheelOverChild = WheelAndRead(panel, childWindow, _GdkScrollSmooth, 1.0);
        measurements.SmoothWheelOverPanel = WheelAndRead(panel, canvasWindow, _GdkScrollSmooth, 1.0);

        panel.AutoScrollPosition = Point.Empty;
        Pump();
        gtk_widget_get_allocation(ButtonLabelled("Overhang"), out var overhang);
        measurements.OverhangingChild = new Rectangle(overhang.X, overhang.Y, overhang.Width, overhang.Height);

        // The band starts where the client area ends; the allocation above must not reach into it.
        measurements.StripLeft = panel.DisplayRectangle.Right;

        // "Tall bottom" sits at y=500 in a viewport 184px tall, so at rest it is entirely out of
        // view. Its rectangle therefore has no area, and only unmapping can express that: an
        // allocation cannot, because gtk_widget_set_size_request is a minimum and a widget asked for
        // zero height falls back to its natural one, reappearing full-size outside the panel.
        measurements.OffscreenChildMapped = gtk_widget_get_mapped(ButtonLabelled("Tall bottom")) != 0;

        // Scrolled to the bottom the same child is back inside the viewport and has to reappear.
        panel.AutoScrollPosition = new Point(0, 1000);
        Pump();
        measurements.ScrolledBackChildMapped = gtk_widget_get_mapped(ButtonLabelled("Tall bottom")) != 0;
        panel.AutoScrollPosition = Point.Empty;
        Pump();

        var tipped = ButtonLabelled("Tipped");
        var tippedWindow = EventWindowOf(tipped, gtk_widget_get_window(gtk_widget_get_parent(tipped)));
        Assert.That(tippedWindow, Is.Not.Zero, "the tipped button has no GdkWindow of its own");
        InjectMotion(tippedWindow, 20, 12);

        // The tip is raised by the toolkit's own delay timer, so the loop has to actually run.
        for (var i = 0; i < 60 && measurements.NativeToolTipText is null; ++i)
        {
            Thread.Sleep(20);
            Pump();
            var text = gtk_widget_get_tooltip_text(tipped);
            if (text == 0)
                continue;

            measurements.NativeToolTipText = Marshal.PtrToStringUTF8(text);
            g_free(text);
        }
    }

    /// <summary>Injects one genuine motion event into <paramref name="window"/>.</summary>
    private static void InjectMotion(nint window, double x, double y)
    {
        var pointer = gdk_seat_get_pointer(gdk_display_get_default_seat(gdk_display_get_default()));
        var e = gdk_event_new(_GdkMotionNotify);
        unsafe
        {
            ref var motion = ref *(GdkMotionEvent*)e;
            motion.Window = g_object_ref(window);
            motion.SendEvent = 1;
            motion.Time = 9000;
            motion.X = x;
            motion.Y = y;
            motion.Device = pointer;
        }

        gtk_main_do_event(e);
        gdk_event_free(e);
        Pump();
    }

    /// <summary>Injects three wheel-down events into <paramref name="window"/> and reports the panel's
    /// resulting vertical scroll offset.</summary>
    private static int WheelAndRead(Panel panel, nint window, int direction, double deltaY)
    {
        panel.AutoScrollPosition = Point.Empty;
        Pump();

        var display = gdk_display_get_default();
        var pointer = gdk_seat_get_pointer(gdk_display_get_default_seat(display));
        for (var i = 0; i < 3; ++i)
        {
            var e = gdk_event_new(_GdkScroll);
            unsafe
            {
                ref var scroll = ref *(GdkScrollEvent*)e;
                scroll.Window = g_object_ref(window);
                scroll.SendEvent = 1;
                scroll.Time = (uint)(5000 + i * 20);
                scroll.X = 5;
                scroll.Y = 5;
                scroll.Direction = direction;
                scroll.Device = pointer;
                scroll.DeltaY = deltaY;
            }

            gtk_main_do_event(e);
            gdk_event_free(e);
            Pump();
        }

        return panel.AutoScrollPosition.Y;
    }

    private static Measured Snapshot(nint widget, Rectangle requested)
    {
        Assert.That(widget, Is.Not.Zero, $"no widget found for {requested}");
        gtk_widget_get_allocation(widget, out var allocation);
        var window = gtk_widget_get_window(widget);
        return new(
            requested,
            new Rectangle(allocation.X, allocation.Y, allocation.Width, allocation.Height),
            window == 0 ? 0 : gdk_window_get_events(window));
    }

    private static Measurements Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_measured, Is.Not.Null, "the GTK loop produced no measurements");
        Assert.That(_measured!.Failure, Is.Null, _measured.Failure);
        return _measured;
    }

    // --- Defect 1: Control.Bounds is what the native widget occupies ----------------------------
    //
    // Measured before the fix, on the same form: the button the toolkit sized 60x26 was allocated
    // 87x34, the label sized 70x20 got 187x20, the entry sized 50x18 got 168x34, and even the
    // 200x30 button got 200x34 — GtkFixed allocates a child its preferred size, and
    // gtk_widget_set_size_request only raises the minimum.

    [Test]
    public void Button_narrower_than_its_caption_is_allocated_exactly_its_bounds()
    {
        var measured = Result().NarrowButton!;
        Assert.That(measured.Allocated, Is.EqualTo(measured.Requested));
    }

    [Test]
    public void Button_wider_than_its_caption_is_allocated_exactly_its_bounds()
    {
        var measured = Result().WideButton!;
        Assert.That(measured.Allocated, Is.EqualTo(measured.Requested));
    }

    [Test]
    public void Label_narrower_than_its_caption_is_allocated_exactly_its_bounds()
    {
        var measured = Result().Label!;
        Assert.That(measured.Allocated, Is.EqualTo(measured.Requested));
    }

    [Test]
    public void TextBox_smaller_than_its_natural_size_is_allocated_exactly_its_bounds()
    {
        var measured = Result().TextBox!;
        Assert.That(measured.Allocated, Is.EqualTo(measured.Requested));
    }

    // --- Defect 2: the wheel reaches the scrollable ancestor -------------------------------------

    [Test]
    public void Native_children_select_both_encodings_of_the_wheel()
    {
        var mask = Result().ChildEventMask;
        Assert.Multiple(() =>
        {
            Assert.That(mask & _GdkScrollMask, Is.Not.Zero, "discrete wheel not selected");
            Assert.That(mask & _GdkSmoothScrollMask, Is.Not.Zero, "smooth wheel not selected");
        });
    }

    [Test]
    public void Discrete_wheel_over_a_native_child_scrolls_the_hosting_panel()
        => Assert.That(Result().DiscreteWheelOverChild, Is.LessThan(0));

    /// <summary>The encoding a real wheel produces under XI2 and Wayland. It was dropped outright
    /// before the fix — the panel stayed at 0 both over a child and over the panel itself.</summary>
    [Test]
    public void Smooth_wheel_over_a_native_child_scrolls_the_hosting_panel()
        => Assert.That(Result().SmoothWheelOverChild, Is.LessThan(0));

    [Test]
    public void Smooth_wheel_over_the_panel_itself_scrolls_it()
        => Assert.That(Result().SmoothWheelOverPanel, Is.LessThan(0));

    // --- Defect: the AutoScroll band belongs to the scrollbar ------------------------------------
    //
    // A native child peer is a real GdkWindow stacked above the container's own surface, so X11
    // delivers a press to the child and the panel's button-press-event never fires for that point.
    // A child overhanging the strip therefore ate every press aimed at the thumb, which no headless
    // assertion can express: the fake has no z-order and hands the peer whatever bounds it is given.

    [Test]
    public void A_child_overhanging_the_scrollbar_is_allocated_clear_of_the_band()
    {
        var measured = Result();
        Assert.That(
            measured.OverhangingChild.Right,
            Is.LessThanOrEqualTo(measured.StripLeft),
            $"the child was allocated {measured.OverhangingChild}, reaching into the band at x={measured.StripLeft}");
    }

    // --- Defect: an AutoScroll panel did not clip its native children ---------------------------
    //
    // Two mechanisms let a scrolled child escape, and neither is visible headlessly. GTK 3.20 and
    // later derive a container's clip from the union of its children's, so the panel claimed its
    // whole 300x530 content box and everything drawn through that clip — gtk_widget_draw, an
    // offscreen surface, the damage region — painted the children across the panel's border. And a
    // child scrolled entirely out of view gets a rectangle with no area, which an allocation cannot
    // express at all: gtk_widget_set_size_request only raises the minimum, so the widget fell back
    // to its natural size and reappeared, full height, below the panel and over its neighbours.

    [Test]
    public void A_child_scrolled_completely_out_of_view_is_unmapped()
        => Assert.That(
            Result().OffscreenChildMapped,
            Is.False,
            "a child with no visible area left must not stay mapped, or GTK gives it its natural size back");

    [Test]
    public void A_child_scrolled_back_into_view_is_mapped_again()
        => Assert.That(
            Result().ScrolledBackChildMapped,
            Is.True,
            "unmapping an out-of-view child must be reversible");

    // --- Defect: a tip on a native-peer control ---------------------------------------------------
    //
    // SetToolTip used to hook OwnerDrawnControl only, so a Button's registration was accepted and
    // then ignored. The tip now rides the peer's pointer channel and is raised through the platform
    // tooltip — GTK's own, which unlike the toolkit popup never takes a pointer grab.

    [Test]
    public void A_native_peer_control_raises_the_platform_tooltip()
        => Assert.That(Result().NativeToolTipText, Is.EqualTo("a native tip"));

    // --- GTK plumbing the fixture needs ----------------------------------------------------------

    private static void Pump()
    {
        for (var i = 0; i < 400 && gtk_events_pending() != 0; ++i)
            gtk_main_iteration_do(0);
    }

    private static void Walk(nint widget, List<(string, nint)> into)
    {
        into.Add((Marshal.PtrToStringUTF8(gtk_widget_get_name(widget)) ?? "?", widget));
        if (g_type_check_instance_is_a(widget, g_type_from_name("GtkContainer")) == 0)
            return;

        var children = gtk_container_get_children(widget);
        var count = g_list_length(children);
        for (var i = 0u; i < count; ++i)
            Walk(g_list_nth_data(children, i), into);

        g_list_free(children);
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

    /// <summary>Finds the <c>GdkWindow</c> GTK maps back to <paramref name="widget"/> — for a
    /// window-less widget such as a GtkButton that is its input-only event window, which is where a
    /// real pointer event over the child would land.</summary>
    private static nint EventWindowOf(nint widget, nint parentWindow)
    {
        var children = gdk_window_get_children(parentWindow);
        var count = g_list_length(children);
        var found = (nint)0;
        for (var i = 0u; i < count; ++i)
        {
            var candidate = g_list_nth_data(children, i);
            gdk_window_get_user_data(candidate, out var owner);
            if (owner == widget)
                found = candidate;
        }

        g_list_free(children);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GtkAllocation
    {
        public int X, Y, Width, Height;
    }

    /// <summary>The full <c>GdkEventScroll</c>, so an injected event carries a smooth delta too.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkScrollEvent
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public uint Time;
        public double X;
        public double Y;
        public uint State;
        public int Direction;
        public nint Device;
        public double XRoot;
        public double YRoot;
        public double DeltaX;
        public double DeltaY;
    }

    /// <summary><c>GdkEventMotion</c> — the layout the injected hover is written into.</summary>
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

    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [LibraryImport(Gtk)] private static partial void gtk_widget_get_allocation(nint widget, out GtkAllocation allocation);
    [LibraryImport(Gtk)] private static partial int gtk_widget_get_mapped(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_window(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_parent(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_name(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_button_get_label(nint button);
    [LibraryImport(Gtk)] private static partial nint gtk_container_get_children(nint container);
    [LibraryImport(Gtk)] private static partial nint gtk_window_list_toplevels();
    [LibraryImport(Gtk)] private static partial int gtk_test_widget_wait_for_draw(nint widget);
    [LibraryImport(Gtk)] private static partial int gtk_events_pending();
    [LibraryImport(Gtk)] private static partial int gtk_main_iteration_do(int blocking);
    [LibraryImport(Gtk)] private static partial void gtk_main_do_event(nint @event);

    [LibraryImport(Gdk)] private static partial nint gdk_event_new(int type);
    [LibraryImport(Gdk)] private static partial void gdk_event_free(nint @event);
    [LibraryImport(Gdk)] private static partial nint gdk_window_get_children(nint window);
    [LibraryImport(Gdk)] private static partial void gdk_window_get_user_data(nint window, out nint data);
    [LibraryImport(Gdk)] private static partial int gdk_window_get_events(nint window);
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default();
    [LibraryImport(Gdk)] private static partial nint gdk_display_get_default_seat(nint display);
    [LibraryImport(Gdk)] private static partial nint gdk_seat_get_pointer(nint seat);

    [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
    [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
    [LibraryImport(GLib)] private static partial void g_free(nint mem);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_tooltip_text(nint widget);
    [LibraryImport(GObject)] private static partial nint g_object_ref(nint instance);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nuint g_type_from_name(string name);
    [LibraryImport(GObject)] private static partial int g_type_check_instance_is_a(nint instance, nuint type);
}
