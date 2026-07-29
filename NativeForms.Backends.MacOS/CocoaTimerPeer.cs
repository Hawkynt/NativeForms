using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A recurring timer whose tick is marshalled onto the UI thread through the backend's post queue.
/// </summary>
/// <remarks>
/// The tick fires on a thread-pool thread and is immediately handed to <see cref="CocoaBackend.Post"/>
/// rather than run there: everything a tick touches is UI state, and the toolkit's contract is that
/// <c>Tick</c> arrives on the UI thread. Doing the work on the pool thread would be a race that looks
/// fine until a repaint happens to overlap it.
/// </remarks>
internal sealed class CocoaTimerPeer(CocoaBackend backend) : ITimerPeer
{
    private System.Threading.Timer? _timer;

    /// <inheritdoc/>
    public event EventHandler? Tick;

    /// <inheritdoc/>
    public void Start(int intervalMs)
    {
        var period = Math.Max(1, intervalMs);
        _timer ??= new(_ => backend.Post(() => this.Tick?.Invoke(this, EventArgs.Empty)));
        _timer.Change(period, period);
    }

    /// <inheritdoc/>
    public void Stop() => _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
