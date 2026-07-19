using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 tray-icon peer, driven by <c>Shell_NotifyIconW</c>. Each peer owns one hidden
/// message-only window whose procedure receives the shell's callback message and translates the
/// relayed mouse messages into <see cref="Click"/>/<see cref="DoubleClick"/>. The icon itself is
/// built decoder-free from ARGB pixels: the color plane reuses the premultiplied DIB machinery of
/// <see cref="Win32Image"/> and gets an empty monochrome AND mask, exactly what 32bpp alpha icons
/// want.
/// </summary>
/// <remarks>
/// The window procedure follows the house pattern: a static
/// <see cref="UnmanagedCallersOnlyAttribute"/> function pointer recovering the managed peer through
/// a static HWND map — no delegates, no captured state across the native boundary.
/// </remarks>
internal sealed unsafe class Win32NotifyIconPeer : INotifyIconPeer
{
    private const string ClassName = "HawkyntNativeFormsTrayWindow";

    /// <summary>The application-private message the shell relays icon interactions through.</summary>
    private const uint TrayCallbackMessage = NativeMethods.WM_APP + 1;

    /// <summary>Maps a live message-window HWND to its peer so the static <see cref="WndProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nint, Win32NotifyIconPeer> _peers = new();

    private static int _classRegistered;
    private static nint _classNamePtr;

    private nint _hwnd;
    private nint _icon;
    private string _tip = string.Empty;
    private bool _added;

    /// <inheritdoc/>
    public event EventHandler? Click;

    /// <inheritdoc/>
    public event EventHandler? DoubleClick;

    /// <summary>Creates the hidden message-only callback window and registers it for routing.</summary>
    public Win32NotifyIconPeer()
    {
        EnsureClassRegistered();
        _hwnd = NativeMethods.CreateWindowExW(
            0,
            ClassName,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HWND_MESSAGE,
            0,
            NativeMethods.GetModuleHandleW(null),
            0);

        if (_hwnd != 0)
            _peers[_hwnd] = this;
    }

    /// <inheritdoc/>
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        var icon = CreateIcon(width, height, argb);
        if (icon == 0)
            return;

        var previous = _icon;
        _icon = icon;
        if (previous != 0)
            NativeMethods.DestroyIcon(previous);

        if (_added)
            this.Notify(NativeMethods.NIM_MODIFY);
    }

    /// <inheritdoc/>
    public void SetToolTip(string text)
    {
        _tip = text ?? string.Empty;
        if (_added)
            this.Notify(NativeMethods.NIM_MODIFY);
    }

    /// <inheritdoc/>
    public void SetVisible(bool visible)
    {
        if (visible == _added || _hwnd == 0)
            return;

        if (visible)
        {
            _added = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, this.BuildData());
            return;
        }

        this.Notify(NativeMethods.NIM_DELETE);
        _added = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.SetVisible(false);
        if (_icon != 0)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }

        if (_hwnd == 0)
            return;

        _peers.TryRemove(_hwnd, out _);
        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = 0;
    }

    /// <summary>Sends one <c>Shell_NotifyIconW</c> call carrying the current state.</summary>
    private void Notify(uint message) => NativeMethods.Shell_NotifyIconW(message, this.BuildData());

    /// <summary>Assembles the registration structure from the current icon, tip and callback window.</summary>
    private NativeMethods.NOTIFYICONDATAW BuildData()
    {
        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NativeMethods.NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
        };

        var length = Math.Min(_tip.Length, 127);
        for (var i = 0; i < length; ++i)
            data.szTip[i] = _tip[i];

        return data;
    }

    /// <summary>Builds a 32bpp alpha <c>HICON</c> from ARGB pixels: a premultiplied DIB color plane
    /// (via <see cref="Win32Image"/>) plus an empty monochrome mask. Shared with
    /// <see cref="WindowPeer.SetIcon"/>, which wants the exact same handle for <c>WM_SETICON</c>.</summary>
    internal static nint CreateIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        using var color = new Win32Image(width, height, argb);
        if (color.Handle == 0)
            return 0;

        var mask = NativeMethods.CreateBitmap(width, height, 1, 1, 0);
        if (mask == 0)
            return 0;

        try
        {
            var info = new NativeMethods.ICONINFO
            {
                fIcon = 1,
                hbmMask = mask,
                hbmColor = color.Handle,
            };

            // CreateIconIndirect copies both bitmaps, so ours are safe to release afterwards.
            return NativeMethods.CreateIconIndirect(in info);
        }
        finally
        {
            NativeMethods.DeleteObject(mask);
        }
    }

    /// <summary>Registers the shared message-window class exactly once for the lifetime of the process.</summary>
    private static void EnsureClassRegistered()
    {
        if (Interlocked.CompareExchange(ref _classRegistered, 1, 0) != 0)
            return;

        // Kept alive for the whole process: the class stays registered and USER32 keeps the pointer.
        _classNamePtr = Marshal.StringToHGlobalUni(ClassName);

        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
            lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = _classNamePtr,
        };

        NativeMethods.RegisterClassExW(in wc);
    }

    /// <summary>
    /// The message-window procedure: translates the shell's relayed mouse messages into managed
    /// events, recovering the peer purely through the static HWND map.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == TrayCallbackMessage && _peers.TryGetValue(hwnd, out var peer))
        {
            switch ((uint)lParam)
            {
                case NativeMethods.WM_LBUTTONUP:
                    peer.Click?.Invoke(peer, EventArgs.Empty);
                    return 0;

                case NativeMethods.WM_LBUTTONDBLCLK:
                    peer.DoubleClick?.Invoke(peer, EventArgs.Empty);
                    return 0;
            }

            return 0;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
