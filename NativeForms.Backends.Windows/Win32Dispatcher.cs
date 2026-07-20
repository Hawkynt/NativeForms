using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 cross-thread marshaller: a hidden message-only window (parent <c>HWND_MESSAGE</c>) on
/// the UI thread whose queue carries a private wake-up message. <see cref="Post"/> enqueues the
/// action into a <see cref="ConcurrentQueue{T}"/> from any thread and posts the wake-up; the window
/// procedure — a static <see cref="UnmanagedCallersOnlyAttribute"/> function pointer, like every
/// other callback in this backend — drains the queue on the loop thread. Actions posted before the
/// loop starts are held in the queue and drained when <see cref="EnsureCreated"/> runs.
/// </summary>
internal static unsafe class Win32Dispatcher
{
    private const string _ClassName = "HawkyntNativeFormsDispatcher";

    /// <summary>The private message that wakes the dispatcher window up to drain the queue
    /// (<c>WM_APP + 1</c> belongs to the tray callback, on its own window class).</summary>
    private const uint _WM_DRAIN = NativeMethods.WM_APP + 2;

    /// <summary>Work queued for the UI thread, in posting order.</summary>
    private static readonly ConcurrentQueue<Action> _queue = new();

    private static int _classRegistered;
    private static nint _classNamePtr;

    /// <summary>The message-only window handle, or 0 until the loop thread created it. Volatile so
    /// a poster that still reads 0 is guaranteed to have enqueued before the creation-time drain.</summary>
    private static volatile nint _hwnd;

    /// <summary>
    /// Creates the message-only window on the calling (loop) thread, once, and drains anything that
    /// was posted before the loop existed. Called at the start of <see cref="Win32Backend.Run"/>.
    /// </summary>
    internal static void EnsureCreated()
    {
        if (_hwnd != 0)
            return;

        EnsureClassRegistered();
        _hwnd = NativeMethods.CreateWindowExW(
            0, _ClassName, string.Empty, 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0,
            NativeMethods.GetModuleHandleW(null), 0);

        Drain();
    }

    /// <summary>Queues <paramref name="action"/> for the UI thread and wakes the dispatcher window.</summary>
    internal static void Post(Action action)
    {
        _queue.Enqueue(action);
        var hwnd = _hwnd;
        if (hwnd != 0)
            NativeMethods.PostMessageW(hwnd, _WM_DRAIN, 0, 0);
    }

    /// <summary>Runs every queued action, in order, on the current (loop) thread.</summary>
    private static void Drain()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }

    /// <summary>Registers the dispatcher window class exactly once for the lifetime of the process.</summary>
    private static void EnsureClassRegistered()
    {
        if (Interlocked.CompareExchange(ref _classRegistered, 1, 0) != 0)
            return;

        // Kept alive for the whole process: the class stays registered and USER32 keeps the pointer.
        _classNamePtr = Marshal.StringToHGlobalUni(_ClassName);

        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
            lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = _classNamePtr,
        };

        NativeMethods.RegisterClassExW(in wc);
    }

    /// <summary>
    /// The dispatcher's window procedure. Static and <see cref="UnmanagedCallersOnlyAttribute"/> so
    /// USER32 invokes it through a function pointer; all state lives in the static queue, so no
    /// managed object crosses the native boundary.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg != _WM_DRAIN)
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);

        Drain();
        return 0;
    }
}
