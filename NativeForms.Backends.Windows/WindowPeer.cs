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

    /// <summary>Routes a <c>WM_COMMAND</c> notification to the child identified by its control id.</summary>
    private void OnCommand(int controlId, int notifyCode)
    {
        if (_children.TryGetValue(controlId, out var child))
            child.OnCommand(notifyCode);
    }

    /// <summary>Raises <see cref="Closed"/> and abandons the (now destroyed) native handle.</summary>
    private void RaiseClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        Handle = 0;
    }

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
                NativeMethods.DestroyWindow(hwnd);
                return 0;

            case NativeMethods.WM_DESTROY:
                if (_windows.TryRemove(hwnd, out var closingWindow))
                    closingWindow.RaiseClosed();

                NativeMethods.PostQuitMessage(0);
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
