using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// The press-and-hold repeat engine shared by spinner buttons and scrollbar arrows: after an initial
/// 500 ms delay the action repeats every 50 ms until <see cref="Stop"/>. Built on <see cref="Timer"/>,
/// so headless tests drive it by firing timer ticks.
/// </summary>
internal sealed class AutoRepeat(Action action) : IDisposable
{
    private const int _InitialDelayMs = 500;
    private const int _RepeatDelayMs = 50;

    private Timer? _timer;

    /// <summary>Arms the initial delay, creating the timer against <paramref name="backend"/> on first use.</summary>
    public void Start(IPlatformBackend backend)
    {
        var timer = _timer;
        if (timer is null)
        {
            _timer = timer = new Timer(backend);
            timer.Tick += this.OnTick;
        }

        timer.Interval = _InitialDelayMs;
        timer.Start();
    }

    /// <summary>Stops repeating; the timer stays around for the next press.</summary>
    public void Stop() => _timer?.Stop();

    /// <summary>Stops and releases the timer source.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Switches from the initial delay to the repeat cadence and performs the action.</summary>
    private void OnTick(object? sender, EventArgs e)
    {
        var timer = _timer;
        if (timer is null)
            return;

        if (timer.Interval != _RepeatDelayMs)
            timer.Interval = _RepeatDelayMs;

        action();
    }
}
