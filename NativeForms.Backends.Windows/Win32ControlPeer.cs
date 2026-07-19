using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Shared behaviour for every Win32 peer: it buffers text/bounds/visibility/enabled state and applies
/// it to the native HWND once one exists (immediately for a window, on first parenting for a child).
/// Property writes made before the handle is created are flushed by <see cref="FlushState"/>.
/// </summary>
internal abstract class Win32ControlPeer : IControlPeer
{
    private string _text = string.Empty;
    private Rectangle _bounds;
    private bool _visible = true;
    private bool _enabled = true;

    /// <summary>The native window handle, or 0 before realization / after destruction.</summary>
    protected nint Handle;

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
        NativeMethods.ShowWindow(Handle, _visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        if (Handle == 0)
            return;

        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
    }
}
