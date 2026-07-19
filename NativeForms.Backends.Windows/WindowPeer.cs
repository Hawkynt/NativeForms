using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a top-level window (a <c>Form</c>). It registers a shared window class once,
/// creates a hidden <c>WS_OVERLAPPEDWINDOW</c>, hosts child controls in its client area, and translates
/// the native messages the toolkit cares about (<c>WM_COMMAND</c>, <c>WM_CLOSE</c>, <c>WM_DESTROY</c>)
/// into managed events.
/// </summary>
/// <remarks>
/// The window procedure is a static <see cref="UnmanagedCallersOnlyAttribute"/> function pointer (never
/// a managed delegate, which would not be AOT-safe and could be collected). It recovers the managed
/// peer purely through the static <see cref="_windows"/> map keyed by HWND — no captured state crosses
/// the native boundary.
/// </remarks>
internal sealed unsafe class WindowPeer : Win32ControlPeer, IWindowPeer
{
    private const string ClassName = "HawkyntNativeFormsWindow";

    /// <summary>Maps a live HWND to its managed peer so the static <see cref="WndProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nint, WindowPeer> _windows = new();

    private static int _classRegistered;
    private static nint _classNamePtr;

    /// <summary>Child controls hosted by this window, keyed by their HMENU control identifier.</summary>
    private readonly Dictionary<int, Win32ChildPeer> _children = new();

    private int _nextControlId = 1000;

    /// <summary>Whether a <see cref="RunModal"/> loop currently owns this window.</summary>
    private bool _modal;

    /// <summary>Whether the modal window was closed (hidden); ends the <see cref="RunModal"/> loop.</summary>
    private bool _modalClosed;

    /// <inheritdoc/>
    public event EventHandler? Closed;

    /// <summary>Creates the (hidden) native top-level window and registers it for message routing.</summary>
    public WindowPeer()
    {
        EnsureClassRegistered();

        var hInstance = NativeMethods.GetModuleHandleW(null);
        Handle = NativeMethods.CreateWindowExW(
            0,
            ClassName,
            string.Empty,
            NativeMethods.WS_OVERLAPPEDWINDOW,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            0,
            0,
            hInstance,
            0);

        if (Handle != 0)
            _windows[Handle] = this;
    }

    /// <inheritdoc/>
    public void AddChild(IControlPeer child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not Win32ChildPeer childPeer)
            throw new ArgumentException($"Expected a {nameof(Win32ChildPeer)} but got {child.GetType()}.", nameof(child));

        var controlId = _nextControlId++;
        _children[controlId] = childPeer;
        childPeer.CreateChildHandle(Handle, controlId);
    }

    /// <inheritdoc/>
    public void Show() => NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOW);

    /// <inheritdoc/>
    /// <remarks>
    /// The Win32 modal recipe: disable the owner, show this window, and pump a nested
    /// <c>GetMessage</c> loop bound to this window's lifetime. Closing hides the window (see
    /// <c>WM_CLOSE</c> in <see cref="WndProc"/>) rather than destroying it, so the peer survives the
    /// loop and the core disposes it normally afterwards. A <c>WM_QUIT</c> arriving mid-dialog is
    /// re-posted so the outer application loop unwinds too.
    /// </remarks>
    public void RunModal(IWindowPeer? owner)
    {
        var ownerHandle = (owner as WindowPeer)?.Handle ?? 0;
        _modal = true;
        _modalClosed = false;
        if (ownerHandle != 0)
            NativeMethods.EnableWindow(ownerHandle, false);

        try
        {
            this.Show();
            while (!_modalClosed && Handle != 0)
            {
                var result = NativeMethods.GetMessageW(out var msg, 0, 0, 0);

                // 0 => WM_QUIT (meant for the outer loop: re-post it and unwind); -1 => error.
                if (result is 0 or -1)
                {
                    if (result == 0)
                        NativeMethods.PostQuitMessage(0);

                    break;
                }

                NativeMethods.TranslateMessage(in msg);
                NativeMethods.DispatchMessageW(in msg);
            }
        }
        finally
        {
            _modal = false;
            if (ownerHandle != 0)
            {
                NativeMethods.EnableWindow(ownerHandle, true);
                NativeMethods.SetActiveWindow(ownerHandle);
            }
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.WM_CLOSE, 0, 0);
    }

    /// <summary>Routes a <c>WM_COMMAND</c> notification to the child identified by its control id.</summary>
    private void OnCommand(int controlId, int notifyCode)
    {
        if (_children.TryGetValue(controlId, out var child))
            child.OnCommand(notifyCode);
    }

    /// <summary>Raises <see cref="Closed"/>.</summary>
    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>Registers the shared window class exactly once for the lifetime of the process.</summary>
    private static void EnsureClassRegistered()
    {
        if (Interlocked.CompareExchange(ref _classRegistered, 1, 0) != 0)
            return;

        // Kept alive for the whole process: the class stays registered and USER32 keeps the pointer.
        _classNamePtr = Marshal.StringToHGlobalUni(ClassName);

        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            hCursor = NativeMethods.LoadCursorW(0, NativeMethods.IDC_ARROW),
            hbrBackground = NativeMethods.COLOR_WINDOW + 1,
            lpszClassName = _classNamePtr,
        };

        NativeMethods.RegisterClassExW(in wc);
    }

    /// <summary>
    /// The native window procedure. Static and <see cref="UnmanagedCallersOnlyAttribute"/> so it can be
    /// invoked directly by USER32 through a function pointer; it recovers the managed peer from the
    /// static HWND map only.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_COMMAND:
                // A non-zero lParam means the message came from a control (its HWND); wParam packs the
                // control id in the low word and the notification code in the high word.
                if (lParam != 0 && _windows.TryGetValue(hwnd, out var commandWindow))
                {
                    var controlId = (int)(wParam & 0xFFFF);
                    var notifyCode = (int)((wParam >> 16) & 0xFFFF);
                    commandWindow.OnCommand(controlId, notifyCode);
                }

                return 0;

            case NativeMethods.WM_CLOSE:
                // A modal window hides instead of dying: the peer must outlive its nested loop so
                // the core can read state and dispose it after ShowDialog returns.
                if (_windows.TryGetValue(hwnd, out var closingModal) && closingModal._modal)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    closingModal._modalClosed = true;
                    closingModal.RaiseClosed();
                    return 0;
                }

                NativeMethods.DestroyWindow(hwnd);
                return 0;

            case NativeMethods.WM_DESTROY:
                if (_windows.TryRemove(hwnd, out var destroyedWindow))
                {
                    // A modal window already announced its close on WM_CLOSE; destruction is then
                    // just the core disposing the peer and must neither re-notify nor quit the loop.
                    var notify = !destroyedWindow._modalClosed;
                    destroyedWindow.Handle = 0;
                    if (notify)
                    {
                        destroyedWindow.RaiseClosed();
                        NativeMethods.PostQuitMessage(0);
                    }
                }

                return 0;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (Handle != 0)
            _windows.TryRemove(Handle, out _);

        base.Dispose();
    }
}
