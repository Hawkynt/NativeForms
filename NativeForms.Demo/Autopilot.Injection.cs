using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Synthesizes real <c>GdkEvent</c>s and hands them to <c>gtk_main_do_event</c> — the very entry
/// point the GDK X11 backend calls once it has translated an X event — so the toolkit sees genuine
/// GTK dispatch: real <c>gtk_propagate_event</c>, real widget lookup, real grab handling. Nothing
/// here talks to the toolkit; it only knows screen coordinates and GDK windows.
/// </summary>
/// <remarks>
/// The alternative, <c>gdk_test_simulate_button</c>, drives XTEST, which a Wayland compositor
/// hosting Xwayland silently swallows because it owns the pointer. Descending the GDK window tree
/// and dispatching directly reproduces what the X server would have delivered: the topmost visible
/// child window containing the point, with the point translated into that window's space.
/// Every entry point must be called on the UI thread.
/// </remarks>
internal static unsafe partial class Injection
{
    private const string _Gtk = "libgtk-3.so.0";
    private const string _Gdk = "libgdk-3.so.0";
    private const string _GLib = "libglib-2.0.so.0";
    private const string _GObject = "libgobject-2.0.so.0";

    // GdkEventType discriminators (gdkevents.h).
    private const int _MotionNotify = 3;
    private const int _ButtonPress = 4;
    private const int _DoubleButtonPress = 5;
    private const int _ButtonRelease = 7;
    private const int _KeyPress = 8;
    private const int _KeyRelease = 9;
    private const int _Scroll = 31;

    /// <summary>GdkModifierType bits the synthesized events carry.</summary>
    internal const uint ShiftMask = 1 << 0;

    /// <summary>The control-key modifier bit.</summary>
    internal const uint ControlMask = 1 << 2;

    /// <summary>The bit set in a motion/release state while button 1 is held.</summary>
    private const uint _Button1Mask = 1 << 8;

    private static uint _clock = 1000;

    /// <summary>
    /// The window an injected press implicitly grabbed, mirroring GDK's implicit pointer grab: every
    /// motion and the release keep going there even once the pointer leaves it. The reference is
    /// owned, because a press that commits a menu item destroys the surface it landed on before the
    /// matching release is sent.
    /// </summary>
    private static nint _grabWindow;

    // --- GTK / GDK entry points -----------------------------------------------------------------

    [LibraryImport(_Gtk)]
    private static partial void gtk_main_do_event(nint @event);

    [LibraryImport(_Gtk)]
    private static partial int gtk_events_pending();

    [LibraryImport(_Gtk)]
    private static partial int gtk_main_iteration_do(int blocking);

    [LibraryImport(_Gtk)]
    private static partial nint gtk_window_list_toplevels();

    [LibraryImport(_Gtk)]
    private static partial nint gtk_widget_get_window(nint widget);

    [LibraryImport(_Gtk)]
    private static partial int gtk_widget_get_mapped(nint widget);

    [LibraryImport(_Gtk)]
    private static partial nint gtk_window_get_title(nint window);

    [LibraryImport(_Gtk)]
    private static partial void gtk_window_present(nint window);

    [LibraryImport(_Gtk)]
    private static partial int gtk_window_is_active(nint window);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_event_new(int type);

    [LibraryImport(_Gdk)]
    private static partial void gdk_event_free(nint @event);

    [LibraryImport(_Gdk)]
    private static partial void gdk_event_set_device(nint @event, nint device);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_display_get_default();

    [LibraryImport(_Gdk)]
    private static partial nint gdk_display_get_default_seat(nint display);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_seat_get_pointer(nint seat);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_seat_get_keyboard(nint seat);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_window_get_children(nint window);

    [LibraryImport(_Gdk)]
    private static partial void gdk_window_get_position(nint window, out int x, out int y);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_get_width(nint window);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_get_height(nint window);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_is_visible(nint window);

    [LibraryImport(_Gdk)]
    private static partial void gdk_window_get_origin(nint window, out int x, out int y);

    [LibraryImport(_Gdk)]
    private static partial void gdk_window_get_user_data(nint window, out nint data);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_window_get_cursor(nint window);

    [LibraryImport(_Gdk)]
    private static partial nint gdk_keymap_get_for_display(nint display);

    [LibraryImport(_Gdk)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gdk_keymap_get_entries_for_keyval(nint keymap, uint keyval, out nint keys, out int count);

    [LibraryImport(_GLib)]
    private static partial uint g_list_length(nint list);

    [LibraryImport(_GLib)]
    private static partial nint g_list_nth_data(nint list, uint n);

    [LibraryImport(_GLib)]
    private static partial void g_list_free(nint list);

    [LibraryImport(_GLib)]
    private static partial void g_free(nint memory);

    [LibraryImport(_Gdk)]
    private static partial int gdk_window_is_destroyed(nint window);

    [LibraryImport(_GObject)]
    private static partial nint g_object_ref(nint instance);

    [LibraryImport(_GObject)]
    private static partial void g_object_unref(nint instance);

    [LibraryImport(_GObject)]
    private static partial nint g_type_name_from_instance(nint instance);

    // --- Event layouts (the leading, publicly documented fields of each GdkEvent member) ---------

    /// <summary>The C layout of <c>GdkEventButton</c> up to the root coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventButton
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
    private struct EventMotion
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

    /// <summary>The C layout of <c>GdkEventScroll</c> up to the smooth-scroll deltas.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventScroll
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

    /// <summary>The C layout of <c>GdkEventKey</c> up to the keyboard group.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventKey
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
    private struct KeymapKey
    {
        public uint Keycode;
        public int Group;
        public int Level;
    }

    // --- Toplevels ------------------------------------------------------------------------------

    /// <summary>Every mapped toplevel <c>GtkWindow</c> the process currently shows.</summary>
    private static List<nint> MappedToplevels()
    {
        var result = new List<nint>();
        var list = gtk_window_list_toplevels();
        var count = g_list_length(list);
        for (var i = 0u; i < count; ++i)
        {
            var widget = g_list_nth_data(list, i);
            if (widget != 0 && gtk_widget_get_mapped(widget) != 0 && gtk_widget_get_window(widget) != 0)
                result.Add(widget);
        }

        g_list_free(list);
        return result;
    }

    /// <summary>The <c>GdkWindow</c> of the mapped toplevel whose title matches, or 0.</summary>
    internal static nint MainWindow(string title)
    {
        foreach (var widget in MappedToplevels())
        {
            var text = Marshal.PtrToStringUTF8(gtk_window_get_title(widget));
            if (text == title)
                return gtk_widget_get_window(widget);
        }

        return 0;
    }

    /// <summary>
    /// The <c>GdkWindow</c>s of every mapped toplevel other than <paramref name="exclude"/> — the
    /// menus, drop-downs and modal dialogs the gallery opens on top of its main window.
    /// </summary>
    internal static List<nint> OtherToplevels(nint exclude)
    {
        var result = new List<nint>();
        foreach (var widget in MappedToplevels())
        {
            var window = gtk_widget_get_window(widget);
            if (window != 0 && window != exclude)
                result.Add(window);
        }

        return result;
    }

    /// <summary>
    /// Whether a toplevel is the one the keyboard is pointed at. A widget cannot take the focus at
    /// all while its window is inactive, so a focus assertion made against an inactive gallery is
    /// measuring the desktop rather than the toolkit.
    /// </summary>
    internal static bool IsActive(nint window)
    {
        gdk_window_get_user_data(window, out var widget);
        return widget != 0 && gtk_window_is_active(widget) != 0;
    }

    /// <summary>
    /// Asks the display server to point the keyboard back at a toplevel.
    /// </summary>
    /// <remarks>
    /// Needed because the walkthrough runs on a bare display. With no window manager the input focus
    /// only ever moves where something explicitly puts it, so every surface that takes it — a modal
    /// dialog, a menu, a tooltip the desktop floated — hands it back to nobody when it goes away, and
    /// the gallery stays inactive for the rest of the run. A real desktop restores the focus for the
    /// user; here the harness has to ask.
    /// </remarks>
    internal static void Present(nint window)
    {
        gdk_window_get_user_data(window, out var widget);
        if (widget != 0)
            gtk_window_present(widget);
    }

    /// <summary>A toplevel's origin and size in screen pixels.</summary>
    internal static Rectangle WindowBounds(nint window)
    {
        gdk_window_get_origin(window, out var x, out var y);
        return new(x, y, gdk_window_get_width(window), gdk_window_get_height(window));
    }

    // --- Hit testing ----------------------------------------------------------------------------

    /// <summary>The window an injected pointer event at a screen point must be delivered to.</summary>
    /// <param name="Window">The deepest visible <c>GdkWindow</c> containing the point.</param>
    /// <param name="Local">The point in that window's own coordinates.</param>
    /// <param name="WidgetName">The GObject type name of the widget owning the window, for the log.</param>
    internal readonly record struct Target(nint Window, Point Local, string WidgetName);

    /// <summary>
    /// Descends the GDK window tree from <paramref name="root"/> the way the X server picks a
    /// delivery window: the topmost visible child containing the point wins, recursively.
    /// </summary>
    internal static Target Resolve(nint root, Point screen)
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
            nint hit = 0;
            var hitPoint = Point.Empty;

            // The list is ordered topmost first, so the first container wins — that is the stacking
            // order a real click would resolve against.
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
                hitPoint = new(local.X - childX, local.Y - childY);
                break;
            }

            g_list_free(children);
            if (hit == 0)
                break;

            window = hit;
            local = hitPoint;
        }

        return new(window, local, WidgetNameOf(window));
    }

    /// <summary>
    /// The <c>GdkCursor</c> currently set on the window under a screen point, or 0 when that window
    /// inherits the default pointer. A region cursor — the sizing shape over a splitter band — shows
    /// up here without being visible anywhere in the managed control state.
    /// </summary>
    internal static nint CursorAt(nint root, Point screen)
        => gdk_window_get_cursor(Resolve(root, screen).Window);

    /// <summary>The GObject type name of the widget a window belongs to ("(none)" when unowned).</summary>
    private static string WidgetNameOf(nint window)
    {
        gdk_window_get_user_data(window, out var widget);
        if (widget == 0)
            return "(none)";

        return Marshal.PtrToStringUTF8(g_type_name_from_instance(widget)) ?? "(unnamed)";
    }

    // --- Pointer --------------------------------------------------------------------------------

    /// <summary>Moves the pointer to a screen point, delivering a motion event.</summary>
    internal static Target Move(nint root, Point screen, bool buttonHeld = false)
    {
        var target = GrabTarget(screen) ?? Resolve(root, screen);
        var evt = gdk_event_new(_MotionNotify);
        ref var motion = ref Unsafe.AsRef<EventMotion>((void*)evt);
        motion.Window = g_object_ref(target.Window);
        motion.SendEvent = 1;
        motion.Time = NextTime();
        motion.X = target.Local.X;
        motion.Y = target.Local.Y;
        motion.State = buttonHeld ? _Button1Mask : 0;
        motion.XRoot = screen.X;
        motion.YRoot = screen.Y;
        Dispatch(evt, Pointer());
        return target;
    }

    /// <summary>Presses a mouse button at a screen point and takes the implicit grab.</summary>
    internal static Target Press(nint root, Point screen, uint button, uint modifiers, bool doubleClick = false)
    {
        var target = Resolve(root, screen);
        ReleaseGrab();
        _grabWindow = g_object_ref(target.Window);
        Button(doubleClick ? _DoubleButtonPress : _ButtonPress, target, screen, button, modifiers);
        return target;
    }

    /// <summary>Releases a mouse button at a screen point and drops the implicit grab.</summary>
    internal static void Release(nint root, Point screen, uint button, uint modifiers)
    {
        var target = GrabTarget(screen) ?? Resolve(root, screen);
        Button(_ButtonRelease, target, screen, button, modifiers | _Button1Mask);
        ReleaseGrab();
    }

    /// <summary>The grabbed window's view of a screen point, or null when no grab is standing — or
    /// when the press destroyed the surface it landed on, as a committing menu click does.</summary>
    private static Target? GrabTarget(Point screen)
    {
        if (_grabWindow == 0)
            return null;

        if (gdk_window_is_destroyed(_grabWindow) != 0)
        {
            ReleaseGrab();
            return null;
        }

        return Translate(_grabWindow, screen);
    }

    /// <summary>Drops the implicit grab and the reference it holds.</summary>
    private static void ReleaseGrab()
    {
        if (_grabWindow == 0)
            return;

        g_object_unref(_grabWindow);
        _grabWindow = 0;
    }

    /// <summary>Sends one wheel notch (positive scrolls up) at a screen point.</summary>
    internal static void Wheel(nint root, Point screen, int notches)
    {
        var target = Resolve(root, screen);
        var evt = gdk_event_new(_Scroll);
        ref var scroll = ref Unsafe.AsRef<EventScroll>((void*)evt);
        scroll.Window = g_object_ref(target.Window);
        scroll.SendEvent = 1;
        scroll.Time = NextTime();
        scroll.X = target.Local.X;
        scroll.Y = target.Local.Y;
        scroll.Direction = notches > 0 ? 0 : 1;
        scroll.XRoot = screen.X;
        scroll.YRoot = screen.Y;
        Dispatch(evt, Pointer());
    }

    /// <summary>Fills and dispatches a button press or release.</summary>
    private static void Button(int type, Target target, Point screen, uint button, uint modifiers)
    {
        var evt = gdk_event_new(type);
        ref var press = ref Unsafe.AsRef<EventButton>((void*)evt);
        press.Window = g_object_ref(target.Window);
        press.SendEvent = 1;
        press.Time = NextTime();
        press.X = target.Local.X;
        press.Y = target.Local.Y;
        press.State = modifiers;
        press.Button = button;
        press.XRoot = screen.X;
        press.YRoot = screen.Y;
        Dispatch(evt, Pointer());
    }

    /// <summary>Re-expresses a screen point in the grabbed window's coordinates, out of bounds and
    /// all — exactly what a drag past a widget's edge delivers under a real implicit grab.</summary>
    private static Target Translate(nint window, Point screen)
    {
        gdk_window_get_origin(window, out var x, out var y);
        return new(window, new(screen.X - x, screen.Y - y), WidgetNameOf(window));
    }

    // --- Keyboard -------------------------------------------------------------------------------

    /// <summary>Sends a press and a release of one key symbol to a toplevel.</summary>
    internal static void Key(nint toplevel, uint keyval, uint modifiers = 0)
    {
        KeyEvent(toplevel, _KeyPress, keyval, modifiers);
        KeyEvent(toplevel, _KeyRelease, keyval, modifiers);
    }

    /// <summary>Sends one key press or release.</summary>
    private static void KeyEvent(nint toplevel, int type, uint keyval, uint modifiers)
    {
        var evt = gdk_event_new(type);
        ref var key = ref Unsafe.AsRef<EventKey>((void*)evt);
        key.Window = g_object_ref(toplevel);
        key.SendEvent = 1;
        key.Time = NextTime();
        key.State = modifiers;
        key.KeyVal = keyval;
        key.HardwareKeycode = KeycodeFor(keyval);
        key.Group = 0;
        key.Length = 0;
        key.String = 0;
        Dispatch(evt, Keyboard());
    }

    /// <summary>The hardware keycode the current keymap maps a symbol to; input methods consult it
    /// alongside the symbol, so leaving it at zero can make a key look synthetic.</summary>
    private static ushort KeycodeFor(uint keyval)
    {
        var keymap = gdk_keymap_get_for_display(gdk_display_get_default());
        if (keymap == 0 || !gdk_keymap_get_entries_for_keyval(keymap, keyval, out var keys, out var count) || count <= 0)
            return 0;

        var keycode = (ushort)Unsafe.AsRef<KeymapKey>((void*)keys).Keycode;
        g_free(keys);
        return keycode;
    }

    // --- Plumbing -------------------------------------------------------------------------------

    /// <summary>Attaches the seat device the event needs and hands it to GTK, then frees it.</summary>
    private static void Dispatch(nint evt, nint device)
    {
        if (device != 0)
            gdk_event_set_device(evt, device);

        gtk_main_do_event(evt);
        gdk_event_free(evt);
    }

    /// <summary>A strictly increasing event timestamp; GTK drops events that travel backwards.</summary>
    private static uint NextTime() => _clock += 16;

    /// <summary>The seat's pointer device — gesture recognizers ignore an event without one.</summary>
    private static nint Pointer()
    {
        var display = gdk_display_get_default();
        return display == 0 ? 0 : gdk_seat_get_pointer(gdk_display_get_default_seat(display));
    }

    /// <summary>The seat's keyboard device.</summary>
    private static nint Keyboard()
    {
        var display = gdk_display_get_default();
        return display == 0 ? 0 : gdk_seat_get_keyboard(gdk_display_get_default_seat(display));
    }

    /// <summary>Runs the main loop until it has nothing left to do, so the next assertion reads a
    /// settled UI rather than one mid-relayout.</summary>
    internal static void Drain()
    {
        for (var i = 0; i < 400 && gtk_events_pending() != 0; ++i)
            gtk_main_iteration_do(0);
    }
}
