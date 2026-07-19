namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// A platform toolkit binding. One implementation exists per windowing system (Win32, GTK, Cocoa …);
/// each is a thin, reflection-free P/Invoke layer so the whole stack stays trim- and AOT-friendly.
/// The core never talks to a native API directly — it creates peers through this factory and drives
/// the platform event loop through <see cref="Run"/>.
/// </summary>
public interface IPlatformBackend
{
    /// <summary>A short, stable identifier used in diagnostics (for example <c>"Win32"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Whether this backend can run on the current OS. The registry uses it to pick a backend; a
    /// backend compiled into an app for another platform simply reports <see langword="false"/>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Creates an unrealized top-level window peer.</summary>
    IWindowPeer CreateWindow();

    /// <summary>Creates an unrealized push-button peer.</summary>
    IButtonPeer CreateButton();

    /// <summary>Creates an unrealized static-text peer.</summary>
    ILabelPeer CreateLabel();

    /// <summary>
    /// Enters the platform message loop and blocks until the main window closes or <see cref="Quit"/>
    /// is called. Must be invoked on the thread that created the widgets.
    /// </summary>
    void Run(IWindowPeer mainWindow);

    /// <summary>Requests that the running message loop exit.</summary>
    void Quit();
}
