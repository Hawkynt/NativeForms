using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Base for lazily-realized child controls (buttons, labels). The native HWND does not exist until the
/// owning <see cref="WindowPeer"/> parents the peer through <see cref="CreateChildHandle"/>, at which
/// point the buffered state captured by <see cref="Win32ControlPeer"/> is flushed onto it.
/// </summary>
internal abstract class Win32ChildPeer : Win32ControlPeer
{
    /// <summary>The subclass identity — one per peer class is enough, the HWND disambiguates.</summary>
    private const nuint _PointerSubclassId = 1;

    /// <summary>Pinning handle keeping this peer reachable from the subclass procedure.</summary>
    private GCHandle _subclassHandle;

    /// <summary>Whether leave tracking is currently armed, so it is re-armed once per crossing.</summary>
    private bool _leaveTracked;

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

        this.InstallPointerSubclass();
        FlushState();
    }

    /// <summary>
    /// Subclasses the stock control so its pointer messages become <see cref="IControlPeer.PointerMove"/>
    /// and <see cref="IControlPeer.PointerLeave"/>. A stock class owns its own window procedure, so
    /// this is the only way to observe hover on a native child; the messages are passed straight on
    /// to <c>DefSubclassProc</c>, so the control's own behavior is untouched.
    /// </summary>
    /// <summary>
    /// Whether this peer needs the subclass to report hover. A peer that already owns its window
    /// class and sees every message itself — the canvas — overrides this to <see langword="false"/>
    /// and feeds the pointer channel from its own procedure instead.
    /// </summary>
    private protected virtual bool NeedsPointerSubclass => true;

    private unsafe void InstallPointerSubclass()
    {
        if (Handle == 0 || !this.NeedsPointerSubclass)
            return;

        if (!_subclassHandle.IsAllocated)
            _subclassHandle = GCHandle.Alloc(this);

        NativeMethods.SetWindowSubclass(
            Handle,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nint, nint>)&PointerSubclassProc,
            _PointerSubclassId,
            GCHandle.ToIntPtr(_subclassHandle));
    }

    /// <summary>
    /// The subclass procedure: observes the pointer messages, then always defers. Static and
    /// <see cref="UnmanagedCallersOnlyAttribute"/> so COMCTL32 can call it through a function
    /// pointer, with the peer recovered from the reference data rather than a captured closure.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint PointerSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        if (refData != 0 && GCHandle.FromIntPtr(refData).Target is Win32ChildPeer peer)
            switch (msg)
            {
                case NativeMethods.WM_MOUSEMOVE:
                    peer.OnPointerMoveMessage(lParam);
                    break;
                case NativeMethods.WM_MOUSELEAVE:
                    peer._leaveTracked = false;
                    peer.RaisePointerLeave();
                    break;
                default:
                    break;
            }

        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Arms leave tracking once per crossing and raises the move with client-space coordinates.</summary>
    private unsafe void OnPointerMoveMessage(nint lParam)
    {
        if (!this.HasPointerListener)
            return;

        if (!_leaveTracked)
        {
            var track = new NativeMethods.TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(NativeMethods.TRACKMOUSEEVENT),
                dwFlags = NativeMethods.TME_LEAVE,
                hwndTrack = Handle,
            };
            _leaveTracked = NativeMethods.TrackMouseEvent(ref track);
        }

        this.RaisePointerMove((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
    }

    /// <summary>Removes the pointer subclass and releases its pinning handle before the window goes.</summary>
    public override unsafe void Dispose()
    {
        if (_subclassHandle.IsAllocated)
        {
            if (Handle != 0)
                NativeMethods.RemoveWindowSubclass(
                    Handle,
                    (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nint, nint>)&PointerSubclassProc,
                    _PointerSubclassId);

            _subclassHandle.Free();
        }

        base.Dispose();
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
