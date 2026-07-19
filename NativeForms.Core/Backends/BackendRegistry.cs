namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// The set of platform backends available to this process and the logic that selects one for the
/// current OS.
/// </summary>
/// <remarks>
/// Registration is explicit and reflection-free so the linker can see exactly which backends an app
/// ships — register only <c>Win32Backend</c> and your Windows build carries no GTK code, which is
/// what keeps single-platform builds small. An app that wants "one binary, every platform" simply
/// registers all three (see the demo's <c>Program.cs</c>); only the supported one is ever realized.
/// </remarks>
public static class BackendRegistry
{
    private static readonly List<IPlatformBackend> _backends = [];
    private static readonly object _gate = new();

    /// <summary>
    /// Adds a backend. Idempotent per concrete type: registering the same backend type twice
    /// keeps only the first.
    /// </summary>
    public static void Register(IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            foreach (var existing in _backends)
                if (existing.GetType() == backend.GetType())
                    return;

            _backends.Add(backend);
        }
    }

    /// <summary>All registered backends, in registration order.</summary>
    public static IReadOnlyList<IPlatformBackend> Registered
    {
        get
        {
            lock (_gate)
                return _backends.ToArray();
        }
    }

    /// <summary>Removes every registered backend. Intended for tests.</summary>
    public static void Clear()
    {
        lock (_gate)
            _backends.Clear();
    }

    /// <summary>Returns the first registered backend that supports the current OS.</summary>
    /// <exception cref="PlatformNotSupportedException">
    /// No backend was registered, or none supports the current platform.
    /// </exception>
    public static IPlatformBackend Resolve()
    {
        lock (_gate)
        {
            if (_backends.Count == 0)
                throw new PlatformNotSupportedException(
                    "No NativeForms backend is registered. Register one in Program.cs, e.g. "
                    + "BackendRegistry.Register(new Win32Backend()); (see the backend packages).");

            foreach (var backend in _backends)
                if (backend.IsSupported)
                    return backend;

            var names = string.Join(", ", _backends.Select(static b => b.Name));
            throw new PlatformNotSupportedException(
                $"None of the registered NativeForms backends ({names}) supports the current platform.");
        }
    }
}
