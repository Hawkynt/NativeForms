using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Shared behaviour for every Win32 peer: it buffers text/bounds/visibility/enabled state and applies
/// it to the native HWND once one exists (immediately for a window, on first parenting for a child).
/// Property writes made before the handle is created are flushed by <see cref="FlushState"/>.
/// </summary>
internal abstract class Win32ControlPeer : IControlPeer
{
    /// <summary>Buffered caption/content text, applied whenever a native handle exists.</summary>
    protected string _text = string.Empty;

    private Rectangle _bounds;
    private bool _visible = true;
    private bool _enabled = true;
    private Font? _font;
    private Color _foreColor;
    private Color _backColor;
    private nint _backBrush;

    /// <summary>The buffered cursor, resolved by the parent's <c>WM_SETCURSOR</c> handler; null = default.</summary>
    internal Cursor? CursorValue;

    /// <summary>The native window handle, or 0 before realization / after destruction.</summary>
    internal nint Handle;

    /// <inheritdoc/>
    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (Handle != 0)
            NativeMethods.MoveWindow(Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height, true);
    }

    /// <inheritdoc/>
    public void SetText(string text)
    {
        _text = text ?? string.Empty;
        if (Handle != 0)
            NativeMethods.SetWindowTextW(Handle, _text);
    }

    /// <inheritdoc/>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (Handle != 0)
            NativeMethods.ShowWindow(Handle, visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
    }

    /// <inheritdoc/>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (Handle != 0)
            NativeMethods.EnableWindow(Handle, enabled);
    }

    /// <inheritdoc/>
    public event EventHandler? GotFocus;

    /// <inheritdoc/>
    public event EventHandler? LostFocus;

    /// <inheritdoc/>
    public void Focus()
    {
        if (Handle != 0)
            NativeMethods.SetFocus(Handle);
    }

    /// <summary>Raises <see cref="GotFocus"/>; called by subclasses translating their native focus notification.</summary>
    protected void RaiseGotFocus() => GotFocus?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="LostFocus"/>; called by subclasses translating their native focus notification.</summary>
    protected void RaiseLostFocus() => LostFocus?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void SetFont(Font font)
    {
        _font = font;
        this.ApplyFont();
    }

    /// <inheritdoc/>
    public unsafe void SetColors(Color foreColor, Color backColor)
    {
        _foreColor = foreColor;
        _backColor = backColor;

        // The colors take effect through the next WM_CTLCOLOR* round trip; the cached brush is
        // rebuilt on demand and the widget repaints with an erased background.
        if (_backBrush != 0)
        {
            NativeMethods.DeleteObject(_backBrush);
            _backBrush = 0;
        }

        if (Handle != 0)
            NativeMethods.InvalidateRect(Handle, null, true);
    }

    /// <inheritdoc/>
    public void SetCursor(Cursor cursor) => CursorValue = cursor;

    /// <summary>Whether an explicit background color is buffered (drives the erase path of windows).</summary>
    private protected bool HasBackColor => !_backColor.IsEmpty;

    /// <summary>Sends <c>WM_SETFONT</c> with the cached <c>HFONT</c> for the buffered font, if any.</summary>
    private void ApplyFont()
    {
        if (Handle == 0 || _font is not { } font)
            return;

        var hFont = Win32FontCache.Get(font, Win32FontCache.ScreenDpi);
        if (hFont != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.WM_SETFONT, hFont, 1);
    }

    /// <summary>
    /// Answers a parent-routed <c>WM_CTLCOLOR*</c> for this control: applies the buffered text color
    /// to the supplied HDC and returns the background brush to erase with, or 0 when no color is set
    /// (letting <c>DefWindowProc</c> take over). The brush is cached per peer; an unset background
    /// falls back to the button-face system color so an explicit foreground alone still works.
    /// </summary>
    internal nint HandleControlColor(nint hdc)
    {
        if (_foreColor.IsEmpty && _backColor.IsEmpty)
            return 0;

        NativeMethods.SetTextColor(
            hdc,
            _foreColor.IsEmpty ? NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT) : ToColorRef(_foreColor));

        if (_backColor.IsEmpty)
        {
            NativeMethods.SetBkColor(hdc, NativeMethods.GetSysColor(NativeMethods.COLOR_BTNFACE));
            return NativeMethods.GetSysColorBrush(NativeMethods.COLOR_BTNFACE);
        }

        NativeMethods.SetBkColor(hdc, ToColorRef(_backColor));
        return this.BackBrush;
    }

    /// <summary>The cached solid brush for the buffered background color, created on first use.</summary>
    private protected nint BackBrush
    {
        get
        {
            if (_backBrush == 0 && !_backColor.IsEmpty)
                _backBrush = NativeMethods.CreateSolidBrush(ToColorRef(_backColor));

            return _backBrush;
        }
    }

    /// <summary>Maps a stock cursor to its <c>IDC_*</c> resource id.</summary>
    internal static nint ToCursorResource(CursorKind kind) => kind switch
    {
        CursorKind.Hand => NativeMethods.IDC_HAND,
        CursorKind.IBeam => NativeMethods.IDC_IBEAM,
        CursorKind.Wait => NativeMethods.IDC_WAIT,
        CursorKind.Cross => NativeMethods.IDC_CROSS,
        CursorKind.SizeWE => NativeMethods.IDC_SIZEWE,
        CursorKind.SizeNS => NativeMethods.IDC_SIZENS,
        CursorKind.SizeNWSE => NativeMethods.IDC_SIZENWSE,
        CursorKind.SizeNESW => NativeMethods.IDC_SIZENESW,
        CursorKind.No => NativeMethods.IDC_NO,
        CursorKind.SizeAll => NativeMethods.IDC_SIZEALL,
        CursorKind.Help => NativeMethods.IDC_HELP,
        CursorKind.AppStarting => NativeMethods.IDC_APPSTARTING,

        // USER32 ships no splitter cursors (WinForms carries them as private resources); the plain
        // resize arrows are the honest stock stand-ins.
        CursorKind.VSplit => NativeMethods.IDC_SIZEWE,
        CursorKind.HSplit => NativeMethods.IDC_SIZENS,
        _ => NativeMethods.IDC_ARROW,
    };

    /// <summary>Converts a managed color to a Win32 <c>COLORREF</c> (0x00BBGGRR); alpha is dropped for GDI.</summary>
    private protected static uint ToColorRef(Color color)
        => (uint)(color.R | (color.G << 8) | (color.B << 16));

    /// <inheritdoc/>
    public Point PointToScreen(Point clientPoint)
    {
        if (Handle == 0)
            return clientPoint;

        var point = new NativeMethods.POINT { x = clientPoint.X, y = clientPoint.Y };
        NativeMethods.ClientToScreen(Handle, ref point);
        return new(point.x, point.y);
    }

    /// <summary>Pushes all buffered state onto the native handle. Call right after it is created.</summary>
    protected void FlushState()
    {
        NativeMethods.SetWindowTextW(Handle, _text);
        NativeMethods.MoveWindow(Handle, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, true);
        NativeMethods.EnableWindow(Handle, _enabled);
        this.ApplyFont();
        NativeMethods.ShowWindow(Handle, _visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        if (_backBrush != 0)
        {
            NativeMethods.DeleteObject(_backBrush);
            _backBrush = 0;
        }

        if (Handle == 0)
            return;

        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
    }
}
