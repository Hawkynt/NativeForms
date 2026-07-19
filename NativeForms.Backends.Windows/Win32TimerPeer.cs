using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 timer peer, backed by a window-less USER32 timer (<c>SetTimer</c> with a null HWND).
/// Ticks are dispatched by the thread's message loop through a static
/// <see cref="UnmanagedCallersOnlyAttribute"/> <c>TIMERPROC</c> function pointer — never a managed
/// delegate — which recovers the managed peer from the static <see cref="_timers"/> map keyed by
/// timer id, the same state-recovery pattern the window procedures use. Because <c>WM_TIMER</c> only
/// flows while the loop pumps, ticks always arrive on the UI thread.
/// </summary>
internal sealed unsafe class Win32TimerPeer : ITimerPeer
{
    /// <summary>Maps a live timer id to its peer so the static <see cref="TimerProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nuint, Win32TimerPeer> _timers = new();

    /// <summary>The USER32 timer id, or 0 while stopped.</summary>
    private nuint _id;

    /// <inheritdoc/>
    public event EventHandler? Tick;

    /// <inheritdoc/>
    public void Start(int intervalMs)
    {
        this.Stop();
        var callback = (nint)(delegate* unmanaged<nint, uint, nuint, uint, void>)&TimerProc;
        var id = NativeMethods.SetTimer(0, 0, (uint)intervalMs, callback);
        if (id == 0)
            return;

        _id = id;
        _timers[id] = this;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        var id = _id;
        if (id == 0)
            return;

        _id = 0;
        _timers.TryRemove(id, out _);
        NativeMethods.KillTimer(0, id);
    }

    /// <inheritdoc/>
    public void Dispose() => this.Stop();

    /// <summary>Raises <see cref="Tick"/>; invoked from the native timer callback.</summary>
    private void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The native <c>TIMERPROC</c>, shaped as <c>void (HWND, UINT, UINT_PTR, DWORD)</c>. Static and
    /// <see cref="UnmanagedCallersOnlyAttribute"/> so USER32 can invoke it through a function pointer;
    /// it recovers the managed peer purely from the static timer-id map.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void TimerProc(nint hwnd, uint msg, nuint idEvent, uint tickCount)
    {
        if (_timers.TryGetValue(idEvent, out var peer))
            peer.RaiseTick();
    }
}
