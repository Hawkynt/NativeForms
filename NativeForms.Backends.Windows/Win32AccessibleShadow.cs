using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Lends an owner-drawn control the accessibility identity of the real Win32 control it imitates
/// (PRD §8), by keeping one of those controls beside it and handing out <em>its</em> accessible object.
/// </summary>
/// <remarks>
/// <para>
/// MSAA derives a window's role from its class, so our canvas is a pane whatever it paints. The way out
/// is not to implement an accessible object — that would be a COM server, with a vtable to build and a
/// role, state and name model to keep correct — but to borrow one: create the real control the owner-drawn
/// surface is imitating, ask Windows for <em>its</em> standard accessible object, and return that from the
/// canvas's <c>WM_GETOBJECT</c>. The object is Windows' own implementation throughout; this file never
/// implements an interface, and the only method it ever calls on one is <c>Release</c>.
/// </para>
/// <para>
/// The shadow is real, so it must be kept from behaving like a control: it is created without
/// <c>WS_TABSTOP</c> so the keyboard never lands on it, and subclassed to paint nothing and to report
/// itself transparent to hit-testing, so the mouse and the pixels both belong to the owner-drawn control
/// as before. It is created lazily, on the first <c>WM_GETOBJECT</c> — an application nobody is reading
/// with assistive technology never allocates one at all.
/// </para>
/// </remarks>
internal static class Win32AccessibleShadow
{
    /// <summary>The window class and creation styles that make Windows report a given role.</summary>
    /// <remarks>
    /// A role with no faithful stock control is left to the canvas's own pane identity rather than
    /// mapped onto an approximation: announcing the wrong thing is worse than announcing a generic one.
    /// </remarks>
    public static (string Class, uint Style)? ControlFor(AccessibleRole role)
        => role switch
        {
            AccessibleRole.PushButton => ("BUTTON", NativeMethods.BS_PUSHBUTTON),
            AccessibleRole.CheckButton => ("BUTTON", NativeMethods.BS_AUTOCHECKBOX),
            AccessibleRole.RadioButton => ("BUTTON", NativeMethods.BS_RADIOBUTTON),
            AccessibleRole.Grouping => ("BUTTON", NativeMethods.BS_GROUPBOX),
            AccessibleRole.StaticText => ("STATIC", 0),
            AccessibleRole.Text => ("EDIT", 0),
            AccessibleRole.ComboBox => ("COMBOBOX", NativeMethods.CBS_DROPDOWNLIST),
            AccessibleRole.List => ("LISTBOX", 0),
            AccessibleRole.ScrollBar => ("SCROLLBAR", 0),
            AccessibleRole.ProgressBar => (NativeMethods.PROGRESS_CLASS, 0),
            AccessibleRole.Slider => (NativeMethods.TRACKBAR_CLASS, 0),
            AccessibleRole.Link => (NativeMethods.WC_LINK, 0),
            _ => null,
        };

    /// <summary>
    /// Creates the shadow control for a role as a child of <paramref name="parent"/>, or 0 when the role
    /// has no faithful stock control or the class is unavailable in this process.
    /// </summary>
    /// <param name="parent">The canvas the shadow belongs to.</param>
    /// <param name="role">The role to imitate.</param>
    /// <param name="name">The accessible name, which becomes the shadow's window text.</param>
    /// <param name="bounds">The canvas's client size, so the shadow reports the same rectangle.</param>
    public static nint Create(nint parent, AccessibleRole role, string? name, System.Drawing.Size bounds)
    {
        if (ControlFor(role) is not { } control || !ClassExists(control.Class))
            return 0;

        // Visible, because MSAA omits an invisible window from the tree — but painting nothing and
        // refusing the mouse, so nothing about the control's behaviour or appearance changes.
        var handle = NativeMethods.CreateWindowExW(
            0,
            control.Class,
            name ?? string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | control.Style,
            0,
            0,
            bounds.Width,
            bounds.Height,
            parent,
            0,
            NativeMethods.GetModuleHandleW(null),
            0);

        if (handle != 0)
            Win32ShadowProc.Install(handle);

        return handle;
    }

    /// <summary>
    /// Answers a <c>WM_GETOBJECT</c> with the shadow's standard accessible object, or 0 to let the
    /// default handling reply for the canvas itself.
    /// </summary>
    /// <param name="shadow">The shadow control's window.</param>
    /// <param name="wParam">The message's <c>wParam</c>, which the reply has to carry back.</param>
    public static nint Reply(nint shadow, nint wParam)
    {
        if (shadow == 0)
            return 0;

        if (NativeMethods.CreateStdAccessibleObject(shadow, NativeMethods.OBJID_CLIENT, NativeMethods.IID_IAccessible, out var accessible) < 0
            || accessible == 0)
            return 0;

        try
        {
            return NativeMethods.LresultFromObject(NativeMethods.IID_IAccessible, wParam, accessible);
        }
        finally
        {
            // LresultFromObject took its own reference; ours is done with.
            NativeMethods.Release(accessible);
        }
    }

    /// <summary>Whether a window class can be instantiated here — the common controls may not be.</summary>
    private static bool ClassExists(string className) => NativeMethods.GetClassInfoExW(0, className, out _);
}
