using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a light-dismiss popup surface. It derives from <see cref="Win32CanvasPeer"/> so
/// the whole owner-drawn pipeline — window class, procedure, paint, mouse, keyboard, child hosting —
/// is reused untouched; only the window's creation and life cycle differ: instead of a child HWND it
/// is a topmost <c>WS_POPUP</c> tool window (<c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c>), shown
/// without activation so the owner window keeps focus. Light dismiss rides on mouse capture taken in
/// <see cref="ShowAt"/>: a left click outside the client area, losing the capture to another window,
/// losing keyboard focus, or Escape all hide the surface and then raise <see cref="Dismissed"/>.
/// Because the surface never activates, keyboard messages only arrive while it is explicitly focused;
/// forwarding richer keyboard input into an unfocused popup is left to the owning control.
/// </summary>
internal sealed class Win32PopupPeer : Win32CanvasPeer, IPopupPeer
{
    private bool _shown;

    /// <inheritdoc/>
    public event EventHandler? Dismissed;

    /// <inheritdoc/>
    public void ShowAt(Point screenLocation, Size size)
    {
        this.EnsureHandle();
        if (this.Handle == 0)
            return;

        NativeMethods.SetWindowPos(
            this.Handle,
            NativeMethods.HWND_TOPMOST,
            screenLocation.X,
            screenLocation.Y,
            size.Width,
            size.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        this._shown = true;

        // Capture routes every mouse message here — including clicks outside the surface — without
        // stealing activation from the owner window.
        NativeMethods.SetCapture(this.Handle);
    }

    /// <inheritdoc/>
    public void Hide()
    {
        // Drop the shown flag first: releasing capture posts WM_CAPTURECHANGED, which must not
        // re-enter dismissal for a surface that is already going away.
        this._shown = false;
        if (this.Handle == 0)
            return;

        if (NativeMethods.GetCapture() == this.Handle)
            NativeMethods.ReleaseCapture();

        NativeMethods.ShowWindow(this.Handle, NativeMethods.SW_HIDE);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.Hide();
        base.Dispose();
    }

    /// <inheritdoc/>
    private protected override bool PreProcessMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                // Captured clicks arrive in this window's client space even when they land outside it.
                NativeMethods.GetClientRect(this.Handle, out var client);
                var x = LoWord(lParam);
                var y = HiWord(lParam);
                if (x >= client.left && x < client.right && y >= client.top && y < client.bottom)
                    return false;

                this.Dismiss();
                return true;

            case NativeMethods.WM_CAPTURECHANGED:
                // Another window took the mouse capture that keeps light dismiss armed.
                if (lParam != this.Handle)
                    this.Dismiss();
                return false;

            case NativeMethods.WM_KILLFOCUS:
                // Dismiss, but let the canvas raise LostFocus as usual.
                this.Dismiss();
                return false;

            case NativeMethods.WM_KEYDOWN when wParam == NativeMethods.VK_ESCAPE:
                this.Dismiss();
                return true;
        }

        return false;
    }

    /// <summary>Hides the surface, then raises <see cref="Dismissed"/>. A no-op while hidden.</summary>
    private void Dismiss()
    {
        if (!this._shown)
            return;

        this.Hide();
        this.Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Creates the (hidden) topmost popup tool window of the shared canvas class on first show.</summary>
    private void EnsureHandle()
    {
        if (this.Handle != 0)
            return;

        EnsureClassRegistered();
        this.Handle = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            ClassName,
            string.Empty,
            NativeMethods.WS_POPUP,
            0,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.GetModuleHandleW(null),
            0);

        if (this.Handle != 0)
            this.OnHandleCreated();
    }
}
