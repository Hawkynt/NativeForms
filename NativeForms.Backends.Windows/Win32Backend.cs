using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Windows (Win32/USER32) implementation of <see cref="IPlatformBackend"/>. It manufactures native
/// peers and pumps the classic <c>GetMessage</c>/<c>TranslateMessage</c>/<c>DispatchMessage</c> loop.
/// The type compiles on any OS; <see cref="IsSupported"/> gates it to Windows at run time.
/// </summary>
public sealed partial class Win32Backend : IPlatformBackend
{
    /// <summary>The most recently constructed backend — the instance the static window procedures
    /// notify when a system-wide theme message arrives (an app runs exactly one backend).</summary>
    private static Win32Backend? _current;

    private Win32Theme? _theme;

    /// <summary>Registers this instance as the receiver of system theme-change notifications.</summary>
    public Win32Backend() => _current = this;

    /// <inheritdoc/>
    public string Name => "Win32";

    /// <inheritdoc/>
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    // Built lazily and cached: constructing it queries the OS, so we defer that until a control paints
    // (and never touch USER32/GDI just by instantiating the backend on a non-Windows host). The cache
    // is dropped when the desktop announces a theme change, so the next read snapshots fresh values.
    public ITheme Theme => _theme ??= new Win32Theme();

    /// <inheritdoc/>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Called from the window procedures when the desktop announces a theme change
    /// (<c>WM_THEMECHANGED</c>, <c>WM_SYSCOLORCHANGE</c>, <c>WM_SETTINGCHANGE</c>): drops the cached
    /// theme snapshot, then raises <see cref="ThemeChanged"/> so realized owner-drawn controls
    /// re-read it and repaint.
    /// </summary>
    internal static void NotifySystemThemeChanged()
    {
        var backend = _current;
        if (backend is null)
            return;

        backend._theme = null;
        backend.ThemeChanged?.Invoke(backend, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public double GetDpiScale()
    {
        var dpi = NativeMethods.GetDpiForSystem();
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <inheritdoc/>
    public IWindowPeer CreateWindow() => new WindowPeer();

    /// <inheritdoc/>
    public IButtonPeer CreateButton() => new ButtonPeer();

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => new LabelPeer();

    /// <inheritdoc/>
    public ITextBoxPeer CreateTextBox() => new TextBoxPeer();

    /// <inheritdoc/>
    public IRichTextBoxPeer CreateRichTextBox() => new RichTextBoxPeer();

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => new Win32CanvasPeer();

    /// <inheritdoc/>
    public IPopupPeer CreatePopup() => new Win32PopupPeer();

    /// <inheritdoc/>
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
        => new Win32Image(width, height, argb);

    /// <inheritdoc/>
    public ITimerPeer CreateTimer() => new Win32TimerPeer();

    /// <inheritdoc/>
    public INotifyIconPeer CreateNotifyIcon() => new Win32NotifyIconPeer();

    /// <inheritdoc/>
    public Size GetScreenSize()
        => new(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

    /// <inheritdoc/>
    public Size MeasureText(string text, Font font)
    {
        var hdc = NativeMethods.GetDC(0);
        if (hdc == 0)
            return Size.Empty;

        try
        {
            var dpi = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
            return Win32Graphics.MeasureText(hdc, text, font, dpi > 0 ? dpi : 96);
        }
        finally
        {
            NativeMethods.ReleaseDC(0, hdc);
        }
    }

    /// <inheritdoc/>
    public void SetClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (NativeMethods.OpenClipboard(0) == 0)
            return;

        try
        {
            NativeMethods.EmptyClipboard();

            // CF_UNICODETEXT is a zero-terminated UTF-16 string in a movable global block; on a
            // successful SetClipboardData the system takes ownership of the handle.
            var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
            if (handle == 0)
                return;

            var target = NativeMethods.GlobalLock(handle);
            if (target == 0)
            {
                NativeMethods.GlobalFree(handle);
                return;
            }

            unsafe
            {
                var destination = new Span<char>((void*)target, text.Length + 1);
                text.AsSpan().CopyTo(destination);
                destination[text.Length] = '\0';
            }

            NativeMethods.GlobalUnlock(handle);
            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, handle) == 0)
                NativeMethods.GlobalFree(handle);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <inheritdoc/>
    public string? GetClipboardText()
    {
        if (NativeMethods.OpenClipboard(0) == 0)
            return null;

        try
        {
            // CF_UNICODETEXT arrives as a zero-terminated UTF-16 string in a global block the
            // clipboard keeps owning — lock, copy into a managed string, unlock, never free.
            var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == 0)
                return null;

            var source = NativeMethods.GlobalLock(handle);
            if (source == 0)
                return null;

            try
            {
                unsafe
                {
                    return new string((char*)source);
                }
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <inheritdoc/>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Win32Dispatcher.Post(action);
    }

    /// <inheritdoc/>
    public void Run(IWindowPeer mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        // The dispatcher's message-only window must live on the loop thread; creating it here also
        // drains anything posted before the loop started.
        Win32Dispatcher.EnsureCreated();

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
