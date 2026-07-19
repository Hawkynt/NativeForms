namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Base for lazily-realized child controls (buttons, labels). The native HWND does not exist until the
/// owning <see cref="WindowPeer"/> parents the peer through <see cref="CreateChildHandle"/>, at which
/// point the buffered state captured by <see cref="Win32ControlPeer"/> is flushed onto it.
/// </summary>
internal abstract class Win32ChildPeer : Win32ControlPeer
{
    /// <summary>The native window class the control is built from (for example <c>"BUTTON"</c>).</summary>
    protected abstract string WindowClass { get; }

    /// <summary>Extra window-style bits OR-ed on top of <c>WS_CHILD | WS_VISIBLE</c>.</summary>
    protected abstract uint ExtraStyle { get; }

    /// <summary>
    /// Creates the native child window parented to <paramref name="parent"/>, using
    /// <paramref name="controlId"/> as the HMENU control identifier so <c>WM_COMMAND</c> notifications
    /// can be routed back to this peer, then flushes buffered state.
    /// </summary>
    internal virtual void CreateChildHandle(nint parent, int controlId)
    {
        var style = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | ExtraStyle;
        Handle = NativeMethods.CreateWindowExW(
            0,
            WindowClass,
            string.Empty,
            style,
            0,
            0,
            0,
            0,
            parent,
            controlId,
            NativeMethods.GetModuleHandleW(null),
            0);

        FlushState();
    }

    /// <summary>
    /// Handles a <c>WM_COMMAND</c> notification addressed to this control. The base implementation does
    /// nothing; interactive controls (buttons) override it.
    /// </summary>
    internal virtual void OnCommand(int notifyCode) { }

    /// <summary>
    /// Handles a <c>WM_NOTIFY</c> notification addressed to this control; <paramref name="lParam"/>
    /// points at the notification-specific structure (which starts with an <c>NMHDR</c>). The base
    /// implementation does nothing; controls with structured notifications (rich edits) override it.
    /// </summary>
    internal virtual void OnNotify(int code, nint lParam) { }
}
