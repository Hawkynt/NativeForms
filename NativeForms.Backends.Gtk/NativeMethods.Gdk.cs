using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// GDK entry points, event masks, key symbols and the minimal <c>GdkEvent*</c> struct layouts the
/// canvas peer reads in its input callbacks. Only the leading fields up to the ones actually used are
/// declared; the sequential layout (with natural alignment) mirrors the C structs on 64-bit systems.
/// </summary>
internal static partial class NativeMethods
{
    private const string Gdk = "libgdk-3.so.0";

    // --- GdkEventMask bits (subset the canvas subscribes to) ------------------------------------

    internal const int GDK_POINTER_MOTION_MASK = 1 << 2;
    internal const int GDK_BUTTON_PRESS_MASK = 1 << 8;
    internal const int GDK_BUTTON_RELEASE_MASK = 1 << 9;
    internal const int GDK_KEY_PRESS_MASK = 1 << 10;
    internal const int GDK_KEY_RELEASE_MASK = 1 << 11;
    internal const int GDK_LEAVE_NOTIFY_MASK = 1 << 13;
    internal const int GDK_FOCUS_CHANGE_MASK = 1 << 14;
    internal const int GDK_SCROLL_MASK = 1 << 21;

    // --- GdkScrollDirection ---------------------------------------------------------------------

    internal const int GDK_SCROLL_UP = 0;
    internal const int GDK_SCROLL_DOWN = 1;

    // --- GdkModifierType bits -------------------------------------------------------------------

    internal const uint GDK_SHIFT_MASK = 1 << 0;
    internal const uint GDK_CONTROL_MASK = 1 << 2;
    internal const uint GDK_MOD1_MASK = 1 << 3;

    // --- GDK key symbols (gdkkeysyms.h) ---------------------------------------------------------

    internal const uint GDK_KEY_BackSpace = 0xff08;
    internal const uint GDK_KEY_Tab = 0xff09;
    internal const uint GDK_KEY_Return = 0xff0d;
    internal const uint GDK_KEY_KP_Enter = 0xff8d;
    internal const uint GDK_KEY_Escape = 0xff1b;
    internal const uint GDK_KEY_space = 0x020;
    internal const uint GDK_KEY_Page_Up = 0xff55;
    internal const uint GDK_KEY_Page_Down = 0xff56;
    internal const uint GDK_KEY_End = 0xff57;
    internal const uint GDK_KEY_Home = 0xff50;
    internal const uint GDK_KEY_Left = 0xff51;
    internal const uint GDK_KEY_Up = 0xff52;
    internal const uint GDK_KEY_Right = 0xff53;
    internal const uint GDK_KEY_Down = 0xff54;
    internal const uint GDK_KEY_Insert = 0xff63;
    internal const uint GDK_KEY_Delete = 0xffff;
    internal const uint GDK_KEY_asterisk = 0x02a;
    internal const uint GDK_KEY_plus = 0x02b;
    internal const uint GDK_KEY_minus = 0x02d;
    internal const uint GDK_KEY_KP_Multiply = 0xffaa;
    internal const uint GDK_KEY_KP_Add = 0xffab;
    internal const uint GDK_KEY_KP_Subtract = 0xffad;

    // --- GdkSeatCapabilities --------------------------------------------------------------------

    /// <summary>All pointing devices — pointer, touch and tablet stylus (the popup's grab scope).</summary>
    internal const int GDK_SEAT_CAPABILITY_ALL_POINTING = 0x7;

    /// <summary>Maps a GDK key symbol to its Unicode code point, or 0 when it has none.</summary>
    [LibraryImport(Gdk)]
    internal static partial uint gdk_keyval_to_unicode(uint keyval);

    /// <summary>Returns the default <c>GdkDisplay</c>.</summary>
    [LibraryImport(Gdk)]
    internal static partial nint gdk_display_get_default();

    /// <summary>
    /// Creates a cursor from a CSS cursor name ("pointer", "text", "ew-resize" …), or returns 0 for
    /// an unknown name. The returned reference is owned by the caller; this backend caches one per
    /// name for the process lifetime instead of unreffing.
    /// </summary>
    [LibraryImport(Gdk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gdk_cursor_new_from_name(nint display, string name);

    /// <summary>Sets (or, with 0, clears) the cursor shown while the pointer is over a <c>GdkWindow</c>.</summary>
    [LibraryImport(Gdk)]
    internal static partial void gdk_window_set_cursor(nint window, nint cursor);

    /// <summary>Returns the default <c>GdkSeat</c> (the user's pointer/keyboard pair) of a display.</summary>
    [LibraryImport(Gdk)]
    internal static partial nint gdk_display_get_default_seat(nint display);

    /// <summary>
    /// Grabs the seat's devices for <paramref name="window"/>. With <paramref name="ownerEvents"/> on,
    /// events over the application's own windows are delivered normally while everything else is
    /// routed to the grab window — the mechanism behind the popup's light dismiss. Returns
    /// <c>GDK_GRAB_SUCCESS</c> (0) when the grab is taken.
    /// </summary>
    [LibraryImport(Gdk)]
    internal static partial int gdk_seat_grab(
        nint seat,
        nint window,
        int capabilities,
        int ownerEvents,
        nint cursor,
        nint @event,
        nint prepareFunc,
        nint prepareFuncData);

    /// <summary>Releases the grab taken by <see cref="gdk_seat_grab"/>.</summary>
    [LibraryImport(Gdk)]
    internal static partial void gdk_seat_ungrab(nint seat);

    /// <summary>Retrieves a window's origin (its top-left corner) in root-window (screen) coordinates.</summary>
    [LibraryImport(Gdk)]
    internal static partial void gdk_window_get_origin(nint window, out int x, out int y);

    // --- Monitors -------------------------------------------------------------------------------

    /// <summary>Returns the display's primary <c>GdkMonitor</c>, or 0 when none is marked primary.</summary>
    [LibraryImport(Gdk)]
    internal static partial nint gdk_display_get_primary_monitor(nint display);

    /// <summary>Returns the display's <paramref name="monitorNum"/>-th <c>GdkMonitor</c>.</summary>
    [LibraryImport(Gdk)]
    internal static partial nint gdk_display_get_monitor(nint display, int monitorNum);

    /// <summary>Reads a monitor's geometry in application pixels.</summary>
    [LibraryImport(Gdk)]
    internal static partial void gdk_monitor_get_geometry(nint monitor, out GdkRectangle geometry);

    /// <summary>Returns a monitor's integer device-pixel scale factor (1 on classic displays, 2 on HiDPI).</summary>
    [LibraryImport(Gdk)]
    internal static partial int gdk_monitor_get_scale_factor(nint monitor);

    /// <summary>Reads the Cairo context's clip extents as an integer rectangle; returns 0 when the
    /// clip is unbounded (the rectangle then spans the whole surface).</summary>
    [LibraryImport(Gdk)]
    internal static partial int gdk_cairo_get_clip_rectangle(nint cr, out GdkRectangle rect);

    // --- GdkWindowState bits --------------------------------------------------------------------

    /// <summary>The window is minimized (iconified).</summary>
    internal const int GDK_WINDOW_STATE_ICONIFIED = 1 << 1;

    /// <summary>The window is maximized.</summary>
    internal const int GDK_WINDOW_STATE_MAXIMIZED = 1 << 2;

    // --- GdkPixbuf (the window-icon pipeline) ---------------------------------------------------

    private const string GdkPixbuf = "libgdk_pixbuf-2.0.so.0";

    /// <summary>Value of <c>GDK_COLORSPACE_RGB</c> — the only colorspace GdkPixbuf supports.</summary>
    internal const int GDK_COLORSPACE_RGB = 0;

    /// <summary>Allocates an uninitialized pixbuf owning its own pixel buffer.</summary>
    [LibraryImport(GdkPixbuf)]
    internal static partial nint gdk_pixbuf_new(int colorspace, int hasAlpha, int bitsPerSample, int width, int height);

    /// <summary>Returns the pixbuf's pixel buffer (rows of R,G,B,A bytes, straight alpha).</summary>
    [LibraryImport(GdkPixbuf)]
    internal static partial nint gdk_pixbuf_get_pixels(nint pixbuf);

    /// <summary>Returns the byte distance between the starts of consecutive pixbuf rows.</summary>
    [LibraryImport(GdkPixbuf)]
    internal static partial int gdk_pixbuf_get_rowstride(nint pixbuf);

    // --- Clipboard ------------------------------------------------------------------------------

    /// <summary>Interns an atom by name; <c>"CLIPBOARD"</c> names the desktop clipboard selection.</summary>
    [LibraryImport(Gdk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gdk_atom_intern(string atomName, int onlyIfExists);

    /// <summary>Returns the (display-owned) clipboard object for the given selection atom.</summary>
    [LibraryImport(Gtk)]
    internal static partial nint gtk_clipboard_get(nint selection);

    /// <summary>Places UTF-8 text on the clipboard; -1 length means zero-terminated.</summary>
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_clipboard_set_text(nint clipboard, string text, int len);
}

/// <summary>A GDK rectangle — also a widget's <c>GtkAllocation</c>: integer position and size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRectangle
{
    /// <summary>The x-coordinate of the left edge.</summary>
    public int X;

    /// <summary>The y-coordinate of the top edge.</summary>
    public int Y;

    /// <summary>The width.</summary>
    public int Width;

    /// <summary>The height.</summary>
    public int Height;
}

/// <summary>The four RGBA components a GDK color carries, each a double in the 0..1 range.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRGBA
{
    /// <summary>Red component (0..1).</summary>
    public double Red;

    /// <summary>Green component (0..1).</summary>
    public double Green;

    /// <summary>Blue component (0..1).</summary>
    public double Blue;

    /// <summary>Alpha component (0..1).</summary>
    public double Alpha;
}

/// <summary>
/// The leading fields of <c>GdkEventButton</c> — enough to read the button and pointer position of a
/// press/release. Layout matches the C struct up to <see cref="Button"/> on 64-bit platforms.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventButton
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>Event time in milliseconds.</summary>
    public uint Time;

    /// <summary>Pointer x in widget coordinates.</summary>
    public double X;

    /// <summary>Pointer y in widget coordinates.</summary>
    public double Y;

    /// <summary>Device axis values (<c>gdouble*</c>).</summary>
    public nint Axes;

    /// <summary>Modifier state (<c>GdkModifierType</c>).</summary>
    public uint State;

    /// <summary>The button number (1 = left, 2 = middle, 3 = right).</summary>
    public uint Button;
}

/// <summary>The leading fields of <c>GdkEventMotion</c> — enough to read the pointer position.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventMotion
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>Event time in milliseconds.</summary>
    public uint Time;

    /// <summary>Pointer x in widget coordinates.</summary>
    public double X;

    /// <summary>Pointer y in widget coordinates.</summary>
    public double Y;
}

/// <summary>The leading fields of <c>GdkEventScroll</c> — enough to read position and direction.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventScroll
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>Event time in milliseconds.</summary>
    public uint Time;

    /// <summary>Pointer x in widget coordinates.</summary>
    public double X;

    /// <summary>Pointer y in widget coordinates.</summary>
    public double Y;

    /// <summary>Modifier state (<c>GdkModifierType</c>).</summary>
    public uint State;

    /// <summary>The scroll direction (<c>GdkScrollDirection</c>).</summary>
    public int Direction;
}

/// <summary>The fields of <c>GdkEventConfigure</c> — a top-level window's new position and size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventConfigure
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>New x-position in root-window (screen) coordinates.</summary>
    public int X;

    /// <summary>New y-position in root-window (screen) coordinates.</summary>
    public int Y;

    /// <summary>New client width in pixels.</summary>
    public int Width;

    /// <summary>New client height in pixels.</summary>
    public int Height;
}

/// <summary>The fields of <c>GdkEventWindowState</c> — which <c>GDK_WINDOW_STATE_*</c> bits flipped.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventWindowState
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>The state bits that changed in this event.</summary>
    public int ChangedMask;

    /// <summary>The complete state after the change.</summary>
    public int NewWindowState;
}

/// <summary>The leading fields of <c>GdkEventKey</c> — enough to read modifiers and the key symbol.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkEventKey
{
    /// <summary>The <c>GdkEventType</c> discriminator.</summary>
    public int Type;

    /// <summary>The event window (<c>GdkWindow*</c>).</summary>
    public nint Window;

    /// <summary>Whether the event was synthesized.</summary>
    public sbyte SendEvent;

    /// <summary>Event time in milliseconds.</summary>
    public uint Time;

    /// <summary>Modifier state (<c>GdkModifierType</c>).</summary>
    public uint State;

    /// <summary>The key symbol (<c>GDK_KEY_*</c>).</summary>
    public uint KeyVal;
}
