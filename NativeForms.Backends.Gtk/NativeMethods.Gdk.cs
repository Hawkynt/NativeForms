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

    /// <summary>Maps a GDK key symbol to its Unicode code point, or 0 when it has none.</summary>
    [LibraryImport(Gdk)]
    internal static partial uint gdk_keyval_to_unicode(uint keyval);
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
