using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Windows (Win32/USER32) implementation of <see cref="IPlatformBackend"/>. It manufactures native
/// peers and pumps the classic <c>GetMessage</c>/<c>TranslateMessage</c>/<c>DispatchMessage</c> loop.
/// The type compiles on any OS; <see cref="IsSupported"/> gates it to Windows at run time.
/// </summary>
public sealed class Win32Backend : IPlatformBackend
{
    /// <inheritdoc/>
    public string Name => "Win32";

    /// <inheritdoc/>
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    // Built lazily and cached: constructing it queries the OS, so we defer that until a control paints
    // (and never touch USER32/GDI just by instantiating the backend on a non-Windows host).
    public ITheme Theme => field ??= new Win32Theme();

    /// <inheritdoc/>
    public IWindowPeer CreateWindow() => new WindowPeer();

    /// <inheritdoc/>
    public IButtonPeer CreateButton() => new ButtonPeer();

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => new LabelPeer();

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => new Win32CanvasPeer();

    /// <inheritdoc/>
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
        => new Win32Image(width, height, argb);

    /// <inheritdoc/>
    public ITimerPeer CreateTimer() => new Win32TimerPeer();

    /// <inheritdoc/>
    public void Run(IWindowPeer mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        while (true)
        {
            var result = NativeMethods.GetMessageW(out var msg, 0, 0, 0);

            // 0 => WM_QUIT (normal exit); -1 => error. Either way, stop pumping.
            if (result is 0 or -1)
                break;

            NativeMethods.TranslateMessage(in msg);
            NativeMethods.DispatchMessageW(in msg);
        }
    }

    /// <inheritdoc/>
    public void Quit() => NativeMethods.PostQuitMessage(0);
}
