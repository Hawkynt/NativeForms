using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for an owner-drawn surface. It is a focusable child window of its own background-less
/// class, so the toolkit paints every pixel itself in <c>WM_PAINT</c> through a <see cref="Win32Graphics"/>.
/// The window procedure — a static <see cref="UnmanagedCallersOnlyAttribute"/> function pointer, never a
/// managed delegate — recovers the peer from the static <see cref="_canvases"/> map keyed by HWND, then
/// translates native paint, mouse, keyboard and focus messages into the <see cref="ICanvasPeer"/> events
/// the managed control subscribes to. As an <see cref="IContainerPeer"/> it also hosts nested child
/// HWNDs on top of its painted surface, exactly like <see cref="WindowPeer"/> hosts them in a form's
/// client area: children added before this canvas has its own HWND are buffered and parented the
/// moment it is created. <see cref="Win32PopupPeer"/> derives from it, reusing the same window class,
/// procedure and event pipeline for the light-dismiss popup surface.
/// </summary>
internal unsafe class Win32CanvasPeer : Win32ChildPeer, ICanvasPeer
{
    /// <summary>The shared canvas window class every owner-drawn surface (canvas or popup) is built from.</summary>
    private protected const string ClassName = "HawkyntNativeFormsCanvas";

    /// <summary>Maps a live canvas HWND to its peer so the static <see cref="WndProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nint, Win32CanvasPeer> _canvases = new();

    private static int _classRegistered;
    private static nint _classNamePtr;

    /// <summary>Child controls hosted by this surface, keyed by their HMENU control identifier. Created
    /// on first use so leaf canvases (the overwhelming majority) pay nothing for the container role.</summary>
    private Dictionary<int, Win32ChildPeer>? _children;

    private int _nextControlId = 1000;

    private bool _focusable;

    /// <inheritdoc/>
    public event EventHandler<PaintEventArgs>? Paint;

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? MouseDown;

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? MouseUp;

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? MouseMove;

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? MouseWheel;

    /// <inheritdoc/>
    public event EventHandler? MouseLeave;

    /// <inheritdoc/>
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <inheritdoc/>
    public event EventHandler<KeyEventArgs>? KeyUp;

    /// <inheritdoc/>
    public event EventHandler<KeyPressEventArgs>? KeyPress;

    /// <inheritdoc/>
    protected override string WindowClass => ClassName;

    /// <inheritdoc/>
    protected override uint ExtraStyle => this._focusable ? NativeMethods.WS_TABSTOP : 0;

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        EnsureClassRegistered();
        base.CreateChildHandle(parent, controlId);
        if (this.Handle != 0)
            this.OnHandleCreated();
    }

    /// <summary>
    /// Registers the fresh HWND for message routing and flushes children that were added while this
    /// surface had no HWND of its own yet. Called by every creation path — child and popup alike.
    /// </summary>
    private protected void OnHandleCreated()
    {
        _canvases[this.Handle] = this;

        if (this._children is null)
            return;

        foreach (var (childId, child) in this._children)
            child.CreateChildHandle(this.Handle, childId);
    }

    /// <summary>
    /// Peer-specific first look at a message, ahead of the shared canvas handling. Return
    /// <see langword="true"/> to report the message fully handled; the popup peer uses this to
    /// implement light dismiss without duplicating the window procedure.
    /// </summary>
    private protected virtual bool PreProcessMessage(uint msg, nint wParam, nint lParam) => false;

    /// <inheritdoc/>
    public void AddChild(IControlPeer child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not Win32ChildPeer childPeer)
            throw new ArgumentException($"Expected a {nameof(Win32ChildPeer)} but got {child.GetType()}.", nameof(child));

        var controlId = this._nextControlId++;
        (this._children ??= new())[controlId] = childPeer;
        if (this.Handle != 0)
            childPeer.CreateChildHandle(this.Handle, controlId);
    }

    /// <inheritdoc/>
    public void Invalidate(Rectangle bounds)
    {
        if (this.Handle == 0)
            return;

        var rect = new NativeMethods.RECT { left = bounds.Left, top = bounds.Top, right = bounds.Right, bottom = bounds.Bottom };
        NativeMethods.InvalidateRect(this.Handle, &rect, false);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        if (this.Handle != 0)
            NativeMethods.InvalidateRect(this.Handle, null, false);
    }

    /// <inheritdoc/>
    public void SetFocusable(bool focusable)
    {
        this._focusable = focusable;
        if (this.Handle == 0)
            return;

        var style = (uint)NativeMethods.GetWindowLongPtrW(this.Handle, NativeMethods.GWL_STYLE);
        style = focusable ? style | NativeMethods.WS_TABSTOP : style & ~NativeMethods.WS_TABSTOP;
        NativeMethods.SetWindowLongPtrW(this.Handle, NativeMethods.GWL_STYLE, (nint)style);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.ReleaseBuffer();
        if (this.Handle != 0)
            _canvases.TryRemove(this.Handle, out _);

        base.Dispose();
    }

    /// <summary>Routes a <c>WM_COMMAND</c> notification from a hosted native child to its peer.</summary>
    private void OnCommandMessage(nint wParam)
    {
        if (this._children is not null && this._children.TryGetValue((int)(wParam & 0xFFFF), out var child))
            child.OnCommand((int)((wParam >> 16) & 0xFFFF));
    }

    /// <summary>Routes a <c>WM_NOTIFY</c> notification from a hosted native child to its peer.</summary>
    private void OnNotifyMessage(nint lParam)
    {
        var header = (NativeMethods.NMHDR*)lParam;
        if (this._children is not null && this._children.TryGetValue((int)header->idFrom, out var child))
            child.OnNotify((int)header->code, lParam);
    }

    // The classic double buffer: painting goes into a client-sized memory bitmap that is blitted to
    // the window DC at the end of WM_PAINT, so partial repaints never flicker. The buffer, the
    // graphics wrapper and the paint args all live in the peer and are reused frame over frame — a
    // steady-state repaint performs zero managed allocations.
    private nint _bufferDc;
    private nint _bufferBitmap;
    private nint _bufferOldBitmap;
    private int _bufferWidth;
    private int _bufferHeight;
    private Win32Graphics? _graphics;
    private PaintEventArgs? _paintArgs;

    /// <summary>Paints the update region by driving the managed <see cref="Paint"/> handler over the
    /// off-screen GDI buffer, then flips the invalid rectangle onto the window.</summary>
    private void OnPaintMessage(nint hwnd)
    {
        var hdc = NativeMethods.BeginPaint(hwnd, out var ps);
        try
        {
            var clip = Rectangle.FromLTRB(ps.rcPaint.left, ps.rcPaint.top, ps.rcPaint.right, ps.rcPaint.bottom);
            if (clip.Width <= 0 || clip.Height <= 0)
                return;

            NativeMethods.GetClientRect(hwnd, out var client);
            var target = this.EnsureBuffer(hdc, client.right - client.left, client.bottom - client.top);

            var graphics = this._graphics ??= new Win32Graphics(target);
            graphics.Bind(target);
            var args = this._paintArgs ??= new PaintEventArgs(graphics, clip);
            args.Reset(graphics, clip);
            this.Paint?.Invoke(this, args);

            if (target != hdc)
                NativeMethods.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, target, clip.X, clip.Y, NativeMethods.SRCCOPY);
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, in ps);
        }
    }

    /// <summary>
    /// Returns the memory DC to paint into, (re)creating the client-sized buffer bitmap when the
    /// surface changed size. Falls back to the window DC — direct, unbuffered painting — when the
    /// buffer cannot be created.
    /// </summary>
    private nint EnsureBuffer(nint hdc, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return hdc;

        if (this._bufferDc != 0 && width == this._bufferWidth && height == this._bufferHeight)
            return this._bufferDc;

        this.ReleaseBuffer();

        var memoryDc = NativeMethods.CreateCompatibleDC(hdc);
        if (memoryDc == 0)
            return hdc;

        var bitmap = NativeMethods.CreateCompatibleBitmap(hdc, width, height);
        if (bitmap == 0)
        {
            NativeMethods.DeleteDC(memoryDc);
            return hdc;
        }

        this._bufferOldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
        this._bufferDc = memoryDc;
        this._bufferBitmap = bitmap;
        this._bufferWidth = width;
        this._bufferHeight = height;
        return memoryDc;
    }

    /// <summary>Destroys the off-screen buffer's DC and bitmap, if any.</summary>
    private void ReleaseBuffer()
    {
        if (this._bufferDc != 0)
        {
            if (this._bufferOldBitmap != 0)
            {
                NativeMethods.SelectObject(this._bufferDc, this._bufferOldBitmap);
                this._bufferOldBitmap = 0;
            }

            NativeMethods.DeleteDC(this._bufferDc);
            this._bufferDc = 0;
        }

        if (this._bufferBitmap != 0)
        {
            NativeMethods.DeleteObject(this._bufferBitmap);
            this._bufferBitmap = 0;
        }

        this._bufferWidth = 0;
        this._bufferHeight = 0;
    }

    /// <summary>Raises a button-changed event, decoding the client-space coordinates from the message
    /// and the live modifier-key state.</summary>
    private void RaiseMouse(EventHandler<MouseEventArgs>? handler, MouseButtons button, nint lParam)
        => handler?.Invoke(this, new MouseEventArgs(button, LoWord(lParam), HiWord(lParam), 0, CurrentModifiers()));

    /// <summary>Raises <see cref="MouseMove"/> and (re)arms leave tracking for the surface.</summary>
    private void OnMouseMoveMessage(nint lParam)
    {
        var track = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(NativeMethods.TRACKMOUSEEVENT),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = this.Handle,
        };
        NativeMethods.TrackMouseEvent(ref track);

        this.MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, LoWord(lParam), HiWord(lParam), 0));
    }

    /// <summary>Raises <see cref="MouseWheel"/>, extracting the signed notch delta from the high word
    /// and the modifier keys from the low-word key-state flags.</summary>
    private void OnMouseWheelMessage(nint wParam, nint lParam)
    {
        var delta = (short)((wParam >> 16) & 0xFFFF);
        var keyState = (uint)(wParam & 0xFFFF);
        var modifiers = KeyModifiers.None;
        if ((keyState & NativeMethods.MK_SHIFT) != 0)
            modifiers |= KeyModifiers.Shift;
        if ((keyState & NativeMethods.MK_CONTROL) != 0)
            modifiers |= KeyModifiers.Control;

        this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, LoWord(lParam), HiWord(lParam), delta, modifiers));
    }

    /// <summary>Raises a key-changed event, mapping the virtual key and current modifier state.</summary>
    private void RaiseKey(EventHandler<KeyEventArgs>? handler, nint wParam)
        => handler?.Invoke(this, new KeyEventArgs((Keys)(int)wParam, CurrentModifiers()));

    /// <summary>Extracts the low word of a message parameter as a signed 16-bit coordinate.</summary>
    private protected static int LoWord(nint value) => (short)(value & 0xFFFF);

    /// <summary>Extracts the high word of a message parameter as a signed 16-bit coordinate.</summary>
    private protected static int HiWord(nint value) => (short)((value >> 16) & 0xFFFF);

    /// <summary>Reads the live Shift/Control/Alt state via <c>GetKeyState</c> into modifier flags.</summary>
    private static KeyModifiers CurrentModifiers()
    {
        var modifiers = KeyModifiers.None;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0)
            modifiers |= KeyModifiers.Shift;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
            modifiers |= KeyModifiers.Control;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_MENU) & 0x8000) != 0)
            modifiers |= KeyModifiers.Alt;
        return modifiers;
    }

    /// <summary>Registers the shared, background-less canvas window class exactly once per process.</summary>
    private protected static void EnsureClassRegistered()
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

            // No background brush: the control owns every pixel and we swallow WM_ERASEBKGND.
            hbrBackground = 0,
            lpszClassName = _classNamePtr,
        };

        NativeMethods.RegisterClassExW(in wc);
    }

    /// <summary>
    /// The native window procedure. Static and <see cref="UnmanagedCallersOnlyAttribute"/> so USER32 can
    /// invoke it through a function pointer; it recovers the managed peer from the static HWND map and
    /// forwards the messages the toolkit cares about, deferring everything else to the default handler.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (_canvases.TryGetValue(hwnd, out var peer))
        {
            if (peer.PreProcessMessage(msg, wParam, lParam))
                return 0;

            switch (msg)
            {
                case NativeMethods.WM_ERASEBKGND:
                    // Report the background as erased so Windows never paints it under us.
                    return 1;

                case NativeMethods.WM_COMMAND:
                    // A non-zero lParam means a hosted native child (its HWND) is notifying us.
                    if (lParam != 0)
                        peer.OnCommandMessage(wParam);
                    return 0;

                case NativeMethods.WM_NOTIFY:
                    if (lParam != 0)
                        peer.OnNotifyMessage(lParam);
                    return 0;

                case NativeMethods.WM_PAINT:
                    peer.OnPaintMessage(hwnd);
                    return 0;

                case NativeMethods.WM_LBUTTONDOWN:
                    peer.RaiseMouse(peer.MouseDown, MouseButtons.Left, lParam);
                    return 0;

                case NativeMethods.WM_RBUTTONDOWN:
                    peer.RaiseMouse(peer.MouseDown, MouseButtons.Right, lParam);
                    return 0;

                case NativeMethods.WM_MBUTTONDOWN:
                    peer.RaiseMouse(peer.MouseDown, MouseButtons.Middle, lParam);
                    return 0;

                case NativeMethods.WM_LBUTTONUP:
                    peer.RaiseMouse(peer.MouseUp, MouseButtons.Left, lParam);
                    return 0;

                case NativeMethods.WM_RBUTTONUP:
                    peer.RaiseMouse(peer.MouseUp, MouseButtons.Right, lParam);
                    return 0;

                case NativeMethods.WM_MBUTTONUP:
                    peer.RaiseMouse(peer.MouseUp, MouseButtons.Middle, lParam);
                    return 0;

                case NativeMethods.WM_MOUSEMOVE:
                    peer.OnMouseMoveMessage(lParam);
                    return 0;

                case NativeMethods.WM_MOUSEWHEEL:
                    peer.OnMouseWheelMessage(wParam, lParam);
                    return 0;

                case NativeMethods.WM_MOUSELEAVE:
                    peer.MouseLeave?.Invoke(peer, EventArgs.Empty);
                    return 0;

                case NativeMethods.WM_KEYDOWN:
                case NativeMethods.WM_SYSKEYDOWN:
                    // WM_SYSKEYDOWN carries Alt-held keys — the form's mnemonic chain needs them.
                    peer.RaiseKey(peer.KeyDown, wParam);
                    return 0;

                case NativeMethods.WM_KEYUP:
                case NativeMethods.WM_SYSKEYUP:
                    peer.RaiseKey(peer.KeyUp, wParam);
                    return 0;

                case NativeMethods.WM_CHAR:
                    peer.KeyPress?.Invoke(peer, new KeyPressEventArgs((char)wParam));
                    return 0;

                case NativeMethods.WM_SETFOCUS:
                    peer.RaiseGotFocus();
                    return 0;

                case NativeMethods.WM_KILLFOCUS:
                    peer.RaiseLostFocus();
                    return 0;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
