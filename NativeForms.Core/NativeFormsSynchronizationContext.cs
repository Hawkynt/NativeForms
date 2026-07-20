using System.Runtime.ExceptionServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// The <see cref="SynchronizationContext"/> <see cref="Application.Run(Form)"/> installs on the UI
/// thread for the duration of the message loop: <see cref="Post"/> queues onto the loop through the
/// backend (like <see cref="Control.BeginInvoke"/>), <see cref="Send"/> runs inline on the loop
/// thread and blocks — propagating exceptions — from any other (like <see cref="Control.Invoke"/>).
/// This is what makes <c>await</c> continuations inside event handlers resume on the UI thread.
/// </summary>
public sealed class NativeFormsSynchronizationContext : SynchronizationContext
{
    private readonly IPlatformBackend _backend;

    /// <summary>Creates a context that marshals onto <paramref name="backend"/>'s message loop.</summary>
    internal NativeFormsSynchronizationContext(IPlatformBackend backend) => _backend = backend;

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _backend.Post(() => d(state));
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!Application.InvokeRequired)
        {
            d(state);
            return;
        }

        SendBlocking(_backend, () => d(state));
    }

    /// <inheritdoc/>
    public override SynchronizationContext CreateCopy() => new NativeFormsSynchronizationContext(_backend);

    /// <summary>
    /// Queues <paramref name="action"/> onto the loop and blocks the calling thread until it ran,
    /// rethrowing (with its original stack) anything it threw — the shared marshalling core behind
    /// <see cref="Send"/> and <see cref="Control.Invoke"/>.
    /// </summary>
    internal static void SendBlocking(IPlatformBackend backend, Action action)
    {
        using var completed = new ManualResetEventSlim();
        ExceptionDispatchInfo? failure = null;
        backend.Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                completed.Set();
            }
        });

        completed.Wait();
        failure?.Throw();
    }
}
