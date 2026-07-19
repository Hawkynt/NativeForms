using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK timer peer, backed by a GLib main-loop timeout source. The callback is a static
/// <see cref="UnmanagedCallersOnlyAttribute"/> <c>GSourceFunc</c> — never a managed delegate — that
/// recovers the peer from the <see cref="GCHandle"/> threaded through <c>user_data</c>, exactly like
/// the signal handlers do, and returns 1 (<c>G_SOURCE_CONTINUE</c>) so the source keeps firing until
/// it is removed. Ticks are dispatched by <c>gtk_main</c>, so they always arrive on the UI thread.
/// </summary>
internal sealed class GtkTimerPeer : ITimerPeer
{
    /// <summary>Pinning handle that keeps this peer reachable from the native timeout callback.</summary>
    private GCHandle _selfHandle;

    /// <summary>The GLib source id, or 0 while stopped.</summary>
    private uint _sourceId;

    /// <inheritdoc/>
    public event EventHandler? Tick;

    /// <inheritdoc/>
    public void Start(int intervalMs)
    {
        this.Stop();
        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&OnTimeout;
            _sourceId = NativeMethods.g_timeout_add_full(
                NativeMethods.G_PRIORITY_DEFAULT, (uint)intervalMs, callback, GCHandle.ToIntPtr(_selfHandle), 0);
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (_sourceId == 0)
            return;

        NativeMethods.g_source_remove(_sourceId);
        _sourceId = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Stop();
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    /// <summary>Raises <see cref="Tick"/>; invoked from the native timeout callback.</summary>
    private void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Native <c>GSourceFunc</c> handler shaped as <c>gboolean (gpointer user_data)</c>; recovers the
    /// peer from <paramref name="userData"/> and returns 1 (<c>G_SOURCE_CONTINUE</c>) to keep ticking.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnTimeout(nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkTimerPeer peer)
            peer.RaiseTick();

        return 1;
    }
}
