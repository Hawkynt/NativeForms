using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The Linux platform backend. Realizes NativeForms controls as native GTK 3 widgets and drives the
/// GTK main loop. Reflection-free and AOT-safe: every native call is a source-generated P/Invoke and
/// every signal callback is an unmanaged function pointer.
/// </summary>
public sealed class GtkBackend : IPlatformBackend
{
    private static readonly object _initGate = new();
    private static bool _initialized;

    /// <summary>Calls <c>gtk_init</c> exactly once, before any widget is created or the loop runs.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initGate)
        {
            if (_initialized)
                return;

            NativeMethods.gtk_init(0, 0);
            _initialized = true;
        }
    }

    /// <inheritdoc />
    public string Name => "Gtk";

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public ITheme Theme
    {
        get
        {
            EnsureInitialized();
            return field ??= new GtkTheme();
        }
    }

    /// <inheritdoc />
    public ICanvasPeer CreateCanvas()
    {
        EnsureInitialized();
        return new GtkCanvasPeer();
    }

    /// <inheritdoc />
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
    {
        EnsureInitialized();
        return new GtkImage(width, height, argb);
    }

    /// <inheritdoc />
    public ITimerPeer CreateTimer()
    {
        EnsureInitialized();
        return new GtkTimerPeer();
    }

    /// <inheritdoc />
    public IWindowPeer CreateWindow()
    {
        EnsureInitialized();
        return new GtkWindowPeer();
    }

    /// <inheritdoc />
    public IButtonPeer CreateButton()
    {
        EnsureInitialized();
        return new GtkButtonPeer();
    }

    /// <inheritdoc />
    public ILabelPeer CreateLabel()
    {
        EnsureInitialized();
        return new GtkLabelPeer();
    }

    /// <inheritdoc />
    public void Run(IWindowPeer mainWindow)
    {
        EnsureInitialized();
        NativeMethods.gtk_main();
    }

    /// <inheritdoc />
    public void Quit() => NativeMethods.gtk_main_quit();
}
