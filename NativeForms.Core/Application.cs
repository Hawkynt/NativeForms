using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// Entry point for a NativeForms UI. Selects a backend, realizes the main form's control tree into
/// native widgets, and pumps the platform message loop — the moral equivalent of
/// <c>System.Windows.Forms.Application</c>.
/// </summary>
public static class Application
{
    private static IPlatformBackend? _backend;

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

        _backend = backend;
        var window = mainForm.RealizeWindow(backend);
        backend.Run(window);
    }

    /// <summary>Requests the running message loop to exit.</summary>
    public static void Exit() => _backend?.Quit();
}
