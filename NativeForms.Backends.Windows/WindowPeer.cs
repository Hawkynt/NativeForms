using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a top-level window (a <c>Form</c>). It registers a shared window class once,
/// creates a hidden <c>WS_OVERLAPPEDWINDOW</c>, hosts child controls in its client area, and translates
/// the native messages the toolkit cares about (<c>WM_COMMAND</c>, <c>WM_CLOSE</c>, <c>WM_DESTROY</c>)
/// into managed events.
/// </summary>
/// <remarks>
/// The window procedure is a static <see cref="UnmanagedCallersOnlyAttribute"/> function pointer (never
/// a managed delegate, which would not be AOT-safe and could be collected). It recovers the managed
/// peer purely through the static <see cref="_windows"/> map keyed by HWND — no captured state crosses
/// the native boundary.
/// </remarks>
internal sealed unsafe class WindowPeer : Win32ControlPeer, IWindowPeer
{
    private const string ClassName = "HawkyntNativeFormsWindow";

    /// <summary>Maps a live HWND to its managed peer so the static <see cref="WndProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nint, WindowPeer> _windows = new();

    private static int _classRegistered;
    private static nint _classNamePtr;

    /// <summary>Child controls hosted by this window, keyed by their HMENU control identifier.</summary>
    private readonly Dictionary<int, Win32ChildPeer> _children = new();

    private int _nextControlId = 1000;

    /// <summary>Whether a <see cref="RunModal"/> loop currently owns this window.</summary>
    private bool _modal;

    /// <summary>Whether the modal window was closed (hidden); ends the <see cref="RunModal"/> loop.</summary>
    private bool _modalClosed;

    /// <summary>Whether <see cref="Show"/> ran; before that, a window-state wish stays buffered.</summary>
    private bool _shown;

    private FormBorderStyle _borderStyle = FormBorderStyle.Sizable;
    private FormWindowState _windowState;
    private bool _minimizeBox = true;
    private bool _maximizeBox = true;
    private Size _minSize;
    private Size _maxSize;
    private double _opacity = 1d;
    private nint _icon;

    /// <inheritdoc/>
    public event EventHandler? Closed;

    /// <inheritdoc/>
    public event EventHandler<Rectangle>? BoundsChangedByUser;

    /// <inheritdoc/>
    public event EventHandler<FormWindowState>? WindowStateChanged;

    /// <summary>Creates the (hidden) native top-level window and registers it for message routing.</summary>
    public WindowPeer()
    {
        EnsureClassRegistered();

        var hInstance = NativeMethods.GetModuleHandleW(null);
        Handle = NativeMethods.CreateWindowExW(
            0,
            ClassName,
            string.Empty,
            NativeMethods.WS_OVERLAPPEDWINDOW,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            NativeMethods.CW_USEDEFAULT,
            0,
            0,
            hInstance,
            0);

        if (Handle != 0)
            _windows[Handle] = this;
    }

    /// <inheritdoc/>
    public void AddChild(IControlPeer child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not Win32ChildPeer childPeer)
            throw new ArgumentException($"Expected a {nameof(Win32ChildPeer)} but got {child.GetType()}.", nameof(child));

        var controlId = _nextControlId++;
        _children[controlId] = childPeer;
        childPeer.CreateChildHandle(Handle, controlId);
    }

    /// <inheritdoc/>
    public void Show()
    {
        _shown = true;
        NativeMethods.ShowWindow(Handle, _windowState switch
        {
            FormWindowState.Minimized => NativeMethods.SW_SHOWMINIMIZED,
            FormWindowState.Maximized => NativeMethods.SW_SHOWMAXIMIZED,
            _ => NativeMethods.SW_SHOW,
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// All the frame-related bits (<c>WS_THICKFRAME</c>, <c>WS_CAPTION</c>, <c>WS_EX_TOOLWINDOW</c> …)
    /// are toggled live via <c>SetWindowLongPtr</c> + <c>SWP_FRAMECHANGED</c> rather than by
    /// recreating the HWND: destroying a top-level window would take every child HWND with it, and
    /// USER32 supports flipping these particular styles on a live window (WinForms does the same).
    /// </remarks>
    public void SetBorderStyle(FormBorderStyle borderStyle)
    {
        _borderStyle = borderStyle;
        this.ApplyFrameStyle();
    }

    /// <inheritdoc/>
    public void SetWindowState(FormWindowState state)
    {
        _windowState = state;
        if (Handle == 0 || !_shown)
            return;

        NativeMethods.ShowWindow(Handle, state switch
        {
            FormWindowState.Minimized => NativeMethods.SW_MINIMIZE,
            FormWindowState.Maximized => NativeMethods.SW_SHOWMAXIMIZED,
            _ => NativeMethods.SW_RESTORE,
        });
    }

    /// <inheritdoc/>
    public void SetMinimizeBox(bool visible)
    {
        _minimizeBox = visible;
        this.ApplyFrameStyle();
    }

    /// <inheritdoc/>
    public void SetMaximizeBox(bool visible)
    {
        _maximizeBox = visible;
        this.ApplyFrameStyle();
    }

    /// <inheritdoc/>
    /// <remarks>The limits are stored and served from the <c>WM_GETMINMAXINFO</c> handler in
    /// <see cref="WndProc"/>, which USER32 consults on every interactive size change.</remarks>
    public void SetSizeLimits(Size minimum, Size maximum)
    {
        _minSize = minimum;
        _maxSize = maximum;
    }

    /// <inheritdoc/>
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb)
    {
        var icon = Win32NotifyIconPeer.CreateIcon(width, height, argb);
        if (icon == 0)
            return;

        var previous = _icon;
        _icon = icon;
        if (Handle != 0)
        {
            NativeMethods.SendMessageW(Handle, NativeMethods.WM_SETICON, NativeMethods.ICON_SMALL, icon);
            NativeMethods.SendMessageW(Handle, NativeMethods.WM_SETICON, NativeMethods.ICON_BIG, icon);
        }

        if (previous != 0)
            NativeMethods.DestroyIcon(previous);
    }

    /// <inheritdoc/>
    public void SetTopMost(bool topMost)
    {
        if (Handle == 0)
            return;

        NativeMethods.SetWindowPos(
            Handle,
            topMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <inheritdoc/>
    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0d, 1d);
        if (Handle == 0)
            return;

        var exStyle = (uint)NativeMethods.GetWindowLongPtrW(Handle, NativeMethods.GWL_EXSTYLE);
        if (_opacity < 1d)
        {
            // A layered window composites at the given alpha; toggling the bit on is cheap and sticky.
            NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWL_EXSTYLE, (nint)(exStyle | NativeMethods.WS_EX_LAYERED));
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(_opacity * 255), NativeMethods.LWA_ALPHA);
        }
        else
        {
            // Fully opaque: drop the layered bit so the window renders on the fast, unlayered path again.
            NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWL_EXSTYLE, (nint)(exStyle & ~NativeMethods.WS_EX_LAYERED));
        }
    }

    /// <summary>
    /// Recomputes the window's style bits from the buffered border style and caption-button wishes
    /// and refreshes the frame in place (<c>SWP_FRAMECHANGED</c>). The visibility bit and the
    /// externally managed extended bits (layered, topmost) are preserved.
    /// </summary>
    private void ApplyFrameStyle()
    {
        if (Handle == 0)
            return;

        var visible = (uint)NativeMethods.GetWindowLongPtrW(Handle, NativeMethods.GWL_STYLE) & NativeMethods.WS_VISIBLE;
        var style = _borderStyle switch
        {
            FormBorderStyle.None => NativeMethods.WS_POPUP,
            FormBorderStyle.FixedSingle => NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU | NativeMethods.WS_BORDER,
            FormBorderStyle.FixedDialog => NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU,
            FormBorderStyle.FixedToolWindow => NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU,
            _ => NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU | NativeMethods.WS_THICKFRAME,
        };

        // Caption buttons exist only on captioned, non-tool frames.
        if (_borderStyle is not FormBorderStyle.None and not FormBorderStyle.FixedToolWindow)
        {
            if (_minimizeBox)
                style |= NativeMethods.WS_MINIMIZEBOX;
            if (_maximizeBox)
                style |= NativeMethods.WS_MAXIMIZEBOX;
        }

        var exStyle = (uint)NativeMethods.GetWindowLongPtrW(Handle, NativeMethods.GWL_EXSTYLE);
        exStyle &= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST;
        exStyle |= _borderStyle switch
        {
            FormBorderStyle.FixedDialog => NativeMethods.WS_EX_DLGMODALFRAME,
            FormBorderStyle.FixedToolWindow => NativeMethods.WS_EX_TOOLWINDOW,
            _ => 0,
        };

        NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWL_STYLE, (nint)(style | visible));
        NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWL_EXSTYLE, (nint)exStyle);
        NativeMethods.SetWindowPos(
            Handle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Handles a native size change: syncs the minimized/maximized/restored state (raising
    /// <see cref="WindowStateChanged"/> on a real transition) and reports the new bounds. A
    /// minimized window reports no bounds — its rectangle is the meaningless off-screen icon spot.
    /// </summary>
    private void OnNativeSizeChanged(int stateWord)
    {
        var state = stateWord switch
        {
            NativeMethods.SIZE_MINIMIZED => FormWindowState.Minimized,
            NativeMethods.SIZE_MAXIMIZED => FormWindowState.Maximized,
            _ => FormWindowState.Normal,
        };

        if (state != _windowState)
        {
            _windowState = state;
            WindowStateChanged?.Invoke(this, state);
        }

        if (state != FormWindowState.Minimized)
            this.RaiseNativeBounds();
    }

    /// <summary>Reports the window's current screen rectangle through <see cref="BoundsChangedByUser"/>.</summary>
    private void RaiseNativeBounds()
    {
        if (Handle == 0 || BoundsChangedByUser is not { } handler || !NativeMethods.GetWindowRect(Handle, out var rect))
            return;

        handler.Invoke(this, new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The Win32 modal recipe: disable the owner, show this window, and pump a nested
    /// <c>GetMessage</c> loop bound to this window's lifetime. Closing hides the window (see
    /// <c>WM_CLOSE</c> in <see cref="WndProc"/>) rather than destroying it, so the peer survives the
    /// loop and the core disposes it normally afterwards. A <c>WM_QUIT</c> arriving mid-dialog is
    /// re-posted so the outer application loop unwinds too.
    /// </remarks>
    public void RunModal(IWindowPeer? owner)
    {
        var ownerHandle = (owner as WindowPeer)?.Handle ?? 0;
        _modal = true;
        _modalClosed = false;
        if (ownerHandle != 0)
            NativeMethods.EnableWindow(ownerHandle, false);

        try
        {
            this.Show();
            while (!_modalClosed && Handle != 0)
            {
                var result = NativeMethods.GetMessageW(out var msg, 0, 0, 0);

                // 0 => WM_QUIT (meant for the outer loop: re-post it and unwind); -1 => error.
                if (result is 0 or -1)
                {
                    if (result == 0)
                        NativeMethods.PostQuitMessage(0);

                    break;
                }

                NativeMethods.TranslateMessage(in msg);
                NativeMethods.DispatchMessageW(in msg);
            }
        }
        finally
        {
            _modal = false;
            if (ownerHandle != 0)
            {
                NativeMethods.EnableWindow(ownerHandle, true);
                NativeMethods.SetActiveWindow(ownerHandle);
            }
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.WM_CLOSE, 0, 0);
    }

    /// <summary>Routes a <c>WM_COMMAND</c> notification to the child identified by its control id.</summary>
    private void OnCommand(int controlId, int notifyCode)
    {
        if (_children.TryGetValue(controlId, out var child))
            child.OnCommand(notifyCode);
    }

    /// <summary>The hosted child peer owning the given HWND, or <see langword="null"/>.</summary>
    private Win32ChildPeer? ChildFromHandle(nint hwnd)
    {
        foreach (var child in _children.Values)
            if (child.Handle == hwnd)
                return child;

        return null;
    }

    /// <summary>
    /// Answers a <c>WM_CTLCOLORSTATIC</c>/<c>EDIT</c>/<c>LISTBOX</c>/<c>BTN</c> for the child that
    /// sent it (<paramref name="childHwnd"/>), returning the brush to erase with or 0 for default
    /// handling. Classic <c>BUTTON</c> push buttons only honor the brush, not the text color — a
    /// USER32 limit; rich edits take their colors through <c>EM_SETBKGNDCOLOR</c>/<c>CHARFORMAT</c>
    /// instead and are not covered by this route.
    /// </summary>
    internal nint OnControlColor(nint hdc, nint childHwnd)
        => this.ChildFromHandle(childHwnd)?.HandleControlColor(hdc) ?? 0;

    /// <summary>
    /// Resolves <c>WM_SETCURSOR</c> over the client area: activates the buffered cursor of the
    /// window itself or of the native child under the pointer. Returns whether it was handled.
    /// </summary>
    internal bool OnSetCursor(nint targetHwnd)
    {
        var cursor = targetHwnd == Handle ? CursorValue : this.ChildFromHandle(targetHwnd)?.CursorValue;
        if (cursor is null)
            return false;

        NativeMethods.SetCursor(NativeMethods.LoadCursorW(0, ToCursorResource(cursor.Kind)));
        return true;
    }

    /// <summary>Routes a <c>WM_NOTIFY</c> notification to the child identified by its control id.</summary>
    private void OnNotify(int controlId, int code, nint lParam)
    {
        if (_children.TryGetValue(controlId, out var child))
            child.OnNotify(code, lParam);
    }

    /// <summary>Raises <see cref="Closed"/>.</summary>
    private void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>Registers the shared window class exactly once for the lifetime of the process.</summary>
    private static void EnsureClassRegistered()
    {
        if (Interlocked.CompareExchange(ref _classRegistered, 1, 0) != 0)
            return;

        // Kept alive for the whole process: the class stays registered and USER32 keeps the pointer.
        _classNamePtr = Marshal.StringToHGlobalUni(ClassName);

        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            hCursor = NativeMethods.LoadCursorW(0, NativeMethods.IDC_ARROW),
            hbrBackground = NativeMethods.COLOR_WINDOW + 1,
            lpszClassName = _classNamePtr,
        };

        NativeMethods.RegisterClassExW(in wc);
    }

    /// <summary>
    /// The native window procedure. Static and <see cref="UnmanagedCallersOnlyAttribute"/> so it can be
    /// invoked directly by USER32 through a function pointer; it recovers the managed peer from the
    /// static HWND map only.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_COMMAND:
                // A non-zero lParam means the message came from a control (its HWND); wParam packs the
                // control id in the low word and the notification code in the high word.
                if (lParam != 0 && _windows.TryGetValue(hwnd, out var commandWindow))
                {
                    var controlId = (int)(wParam & 0xFFFF);
                    var notifyCode = (int)((wParam >> 16) & 0xFFFF);
                    commandWindow.OnCommand(controlId, notifyCode);
                }

                return 0;

            case NativeMethods.WM_NOTIFY:
                // Structured notifications (rich edits, common controls): lParam points at an NMHDR
                // carrying the sender's control id and the notification code.
                if (lParam != 0 && _windows.TryGetValue(hwnd, out var notifyWindow))
                {
                    var header = (NativeMethods.NMHDR*)lParam;
                    notifyWindow.OnNotify((int)header->idFrom, (int)header->code, lParam);
                }

                return 0;

            case NativeMethods.WM_CTLCOLORSTATIC:
            case NativeMethods.WM_CTLCOLOREDIT:
            case NativeMethods.WM_CTLCOLORLISTBOX:
            case NativeMethods.WM_CTLCOLORBTN:
                // wParam is the child's paint HDC, lParam its HWND; a non-zero brush answers the
                // message, 0 falls through to the default coloring.
                if (_windows.TryGetValue(hwnd, out var coloringWindow))
                {
                    var brush = coloringWindow.OnControlColor(wParam, lParam);
                    if (brush != 0)
                        return brush;
                }

                break;

            case NativeMethods.WM_SETCURSOR:
                // Only the client area: frame edges keep their resize arrows. wParam names the
                // window that contains the pointer (this window or a native child).
                if ((lParam & 0xFFFF) == NativeMethods.HTCLIENT
                    && _windows.TryGetValue(hwnd, out var cursorWindow)
                    && cursorWindow.OnSetCursor(wParam))
                    return 1;

                break;

            case NativeMethods.WM_ERASEBKGND:
                // An explicit BackColor replaces the class background brush.
                if (_windows.TryGetValue(hwnd, out var erasingWindow)
                    && erasingWindow.HasBackColor
                    && NativeMethods.GetClientRect(hwnd, out var clientRect))
                {
                    NativeMethods.FillRect(wParam, in clientRect, erasingWindow.BackBrush);
                    return 1;
                }

                break;

            case NativeMethods.WM_SIZE:
                if (_windows.TryGetValue(hwnd, out var sizedWindow))
                    sizedWindow.OnNativeSizeChanged((int)wParam);

                return 0;

            case NativeMethods.WM_MOVE:
                if (_windows.TryGetValue(hwnd, out var movedWindow))
                    movedWindow.RaiseNativeBounds();

                return 0;

            case NativeMethods.WM_GETMINMAXINFO:
                // USER32 asks for the resize-tracking limits; answer from the buffered size limits
                // (zero components stay at the system defaults the struct arrives with).
                if (_windows.TryGetValue(hwnd, out var limitedWindow))
                {
                    var info = (NativeMethods.MINMAXINFO*)lParam;
                    var min = limitedWindow._minSize;
                    var max = limitedWindow._maxSize;
                    if (min.Width > 0)
                        info->ptMinTrackSize.x = min.Width;
                    if (min.Height > 0)
                        info->ptMinTrackSize.y = min.Height;
                    if (max.Width > 0)
                        info->ptMaxTrackSize.x = max.Width;
                    if (max.Height > 0)
                        info->ptMaxTrackSize.y = max.Height;

                    return 0;
                }

                break;

            case NativeMethods.WM_THEMECHANGED:
            case NativeMethods.WM_SYSCOLORCHANGE:
            case NativeMethods.WM_SETTINGCHANGE:
                // The desktop announced a theme/system-color/settings change: drop the backend's
                // cached theme and let owner-drawn controls repaint, then let USER32 do its part.
                Win32Backend.NotifySystemThemeChanged();
                break;

            case NativeMethods.WM_CLOSE:
                // A modal window hides instead of dying: the peer must outlive its nested loop so
                // the core can read state and dispose it after ShowDialog returns.
                if (_windows.TryGetValue(hwnd, out var closingModal) && closingModal._modal)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    closingModal._modalClosed = true;
                    closingModal.RaiseClosed();
                    return 0;
                }

                NativeMethods.DestroyWindow(hwnd);
                return 0;

            case NativeMethods.WM_DESTROY:
                if (_windows.TryRemove(hwnd, out var destroyedWindow))
                {
                    // A modal window already announced its close on WM_CLOSE; destruction is then
                    // just the core disposing the peer and must neither re-notify nor quit the loop.
                    var notify = !destroyedWindow._modalClosed;
                    destroyedWindow.Handle = 0;
                    if (notify)
                    {
                        destroyedWindow.RaiseClosed();
                        NativeMethods.PostQuitMessage(0);
                    }
                }

                return 0;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (Handle != 0)
            _windows.TryRemove(Handle, out _);

        base.Dispose();

        if (_icon == 0)
            return;

        NativeMethods.DestroyIcon(_icon);
        _icon = 0;
    }
}
