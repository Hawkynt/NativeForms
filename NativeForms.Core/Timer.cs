using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// A recurring timer whose <see cref="Tick"/> fires on the UI thread, driven by the platform message
/// loop — the moral equivalent of <c>System.Windows.Forms.Timer</c>. Marquee progress, caret blink,
/// tooltip delays and key autorepeat are all built on it.
/// </summary>
/// <remarks>
/// The native timer source comes from the backend the application is running on, so a timer enabled
/// before <see cref="Application.Run(Form)"/> has started cannot tick yet: the wish is remembered and
/// the source is armed the next time <see cref="Enabled"/>, <see cref="Interval"/> or
/// <see cref="Start"/> is touched while the loop is running — which is exactly when a handler inside
/// that loop first pokes the timer.
/// </remarks>
public sealed class Timer : Component
{
    private readonly IPlatformBackend? _backend;
    private ITimerPeer? _peer;
    private int _interval = 100;
    private bool _enabled;
    private bool _running;

    /// <summary>Creates a timer bound to whatever backend the application runs on.</summary>
    public Timer() { }

    /// <summary>Creates a timer against an explicit backend. Intended for tests.</summary>
    internal Timer(IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Raised on the UI thread every <see cref="Interval"/> milliseconds while <see cref="Enabled"/>.</summary>
    public event EventHandler? Tick;

    /// <summary>The tick period in milliseconds. Setting it while the timer runs restarts the period.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Interval
    {
        get => _interval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _interval = value;
            if (_enabled)
                this.Arm();
        }
    }

    /// <summary>Whether the timer is ticking. Setting the same value again is a no-op.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            var wasRunning = _running;
            _enabled = value;
            if (!value)
            {
                if (wasRunning)
                {
                    _peer!.Stop();
                    _running = false;
                }

                return;
            }

            if (!wasRunning)
                this.Arm();
        }
    }

    /// <summary>Starts the timer; identical to setting <see cref="Enabled"/> to <see langword="true"/>.</summary>
    public void Start() => this.Enabled = true;

    /// <summary>Stops the timer; identical to setting <see cref="Enabled"/> to <see langword="false"/>.</summary>
    public void Stop() => this.Enabled = false;

    /// <summary>Stops the timer and releases the native timer source.</summary>
    protected override void Dispose(bool disposing)
    {
        _enabled = false;
        _running = false;
        var peer = _peer;
        if (peer is null)
            return;

        _peer = null;
        peer.Tick -= this.OnPeerTick;
        peer.Dispose();
    }

    /// <summary>
    /// (Re)starts the native timer source at the current interval, creating the peer on first use.
    /// Without a backend — the application loop has not started yet — the enabled wish is kept for the
    /// next <see cref="Enabled"/>/<see cref="Interval"/> write.
    /// </summary>
    private void Arm()
    {
        var peer = _peer;
        if (peer is null)
        {
            var backend = _backend ?? Application.Current;
            if (backend is null)
                return;

            _peer = peer = backend.CreateTimer();
            peer.Tick += this.OnPeerTick;
        }

        peer.Start(_interval);
        _running = true;
    }

    /// <summary>Forwards a native tick to <see cref="Tick"/> subscribers; allocation-free.</summary>
    private void OnPeerTick(object? sender, EventArgs e) => Tick?.Invoke(this, EventArgs.Empty);
}
