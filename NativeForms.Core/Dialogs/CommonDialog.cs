using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for the native common dialogs (<see cref="OpenFileDialog"/>, <see cref="ColorDialog"/> …).
/// A dialog object is a thin option holder: <see cref="ShowDialog"/> hands the options to the running
/// backend, which presents the platform's own dialog application-modal and blocks until it closes.
/// </summary>
public abstract class CommonDialog
{
    private readonly IPlatformBackend? _backend;

    /// <summary>Creates a dialog bound to whatever backend the application runs on.</summary>
    protected CommonDialog() { }

    /// <summary>Creates a dialog against an explicit backend. Intended for tests.</summary>
    private protected CommonDialog(IPlatformBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>
    /// Shows the native dialog and blocks until it closes. Returns <see cref="DialogResult.OK"/> when
    /// the user confirmed (the dialog's properties then carry the choice) or
    /// <see cref="DialogResult.Cancel"/> when they backed out.
    /// </summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public DialogResult ShowDialog()
        => this.RunDialog(_backend ?? Application.Current ?? throw new InvalidOperationException(
            $"{this.GetType().Name}.ShowDialog needs a running backend — call it from inside Application.Run."));

    /// <summary>Presents the dialog on the given backend and translates its outcome.</summary>
    private protected abstract DialogResult RunDialog(IPlatformBackend backend);
}
