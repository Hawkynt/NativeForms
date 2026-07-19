using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// Entry point for a NativeForms UI. Selects a backend, realizes the main form's control tree into
/// native widgets, and pumps the platform message loop — the moral equivalent of
/// <c>System.Windows.Forms.Application</c>.
/// </summary>
public static class Application
{
    /// <summary>
    /// The backend the message loop is currently running on, or <see langword="null"/> outside
    /// <see cref="Run(Form)"/>. Components that need a backend after startup — <see cref="Timer"/>,
    /// for example — resolve it here instead of dragging one through every constructor.
    /// </summary>
    internal static IPlatformBackend? Current { get; private set; }

    /// <summary>
    /// Shows <paramref name="mainForm"/> and runs the message loop until it closes, choosing the
    /// backend registered for the current platform.
    /// </summary>
    public static void Run(Form mainForm) => Run(mainForm, BackendRegistry.Resolve());

    /// <summary>Shows <paramref name="mainForm"/> and runs the message loop on an explicit backend.</summary>
    public static void Run(Form mainForm, IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(backend);

        Current = backend;
        try
        {
            var window = mainForm.RealizeWindow(backend);
            backend.Run(window);
        }
        finally
        {
            Current = null;
        }
    }

    /// <summary>Requests the running message loop to exit.</summary>
    public static void Exit() => Current?.Quit();
}
