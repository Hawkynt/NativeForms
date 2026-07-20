using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// Entry point for a NativeForms UI. Selects a backend, realizes the main form's control tree into
/// native widgets, and pumps the platform message loop — the moral equivalent of
/// <c>System.Windows.Forms.Application</c>.
/// </summary>
public static class Application
{
    /// <summary>The managed id of the thread pumping the loop, or -1 outside <see cref="Run(Form)"/>.</summary>
    private static int _loopThreadId = -1;

    /// <summary>
    /// The backend the message loop is currently running on, or <see langword="null"/> outside
    /// <see cref="Run(Form)"/>. Components that need a backend after startup — <see cref="Timer"/>,
    /// for example — resolve it here instead of dragging one through every constructor.
    /// </summary>
    internal static IPlatformBackend? Current { get; private set; }

    /// <summary>
    /// Whether a message loop is running and the caller is not on its thread — the condition under
    /// which UI state must be reached through <see cref="Control.Invoke"/> instead of directly.
    /// </summary>
    internal static bool InvokeRequired
    {
        get
        {
            var loopThreadId = _loopThreadId;
            return loopThreadId != -1 && loopThreadId != Environment.CurrentManagedThreadId;
        }
    }

    /// <summary>
    /// Shows <paramref name="mainForm"/> and runs the message loop until it closes, choosing the
    /// backend registered for the current platform.
    /// </summary>
    public static void Run(Form mainForm) => Run(mainForm, BackendRegistry.Resolve());

    /// <summary>
    /// Shows <paramref name="mainForm"/> and runs the message loop on an explicit backend. The
    /// calling thread becomes the UI thread: its id anchors <see cref="Control.InvokeRequired"/>, and
    /// a <see cref="NativeFormsSynchronizationContext"/> is installed on it for the duration of the
    /// loop so <c>await</c> continuations resume on the loop.
    /// </summary>
    public static void Run(Form mainForm, IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(backend);

        Current = backend;
        _loopThreadId = Environment.CurrentManagedThreadId;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NativeFormsSynchronizationContext(backend));
        try
        {
            var window = mainForm.RealizeWindow(backend);
            backend.Run(window);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            _loopThreadId = -1;
            Current = null;
        }
    }

    /// <summary>Requests the running message loop to exit.</summary>
    public static void Exit() => Current?.Quit();
}
