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
/// moment it is created.
/// </summary>
internal sealed unsafe class Win32CanvasPeer : Win32ChildPeer, ICanvasPeer
{
    private const string ClassName = "HawkyntNativeFormsCanvas";

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
    public event EventHandler? GotFocus;

    /// <inheritdoc/>
    public event EventHandler? LostFocus;

    /// <inheritdoc/>
    protected override string WindowClass => ClassName;

    /// <inheritdoc/>
    protected override uint ExtraStyle => this._focusable ? NativeMethods.WS_TABSTOP : 0;

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        EnsureClassRegistered();
        base.CreateChildHandle(parent, controlId);
        if (this.Handle == 0)
            return;

        _canvases[this.Handle] = this;

        // Flush children that were added while this canvas had no HWND of its own yet.
        if (this._children is null)
            return;

        foreach (var (childId, child) in this._children)
            child.CreateChildHandle(this.Handle, childId);
    }

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
    public void Focus()
    {
        if (this.Handle != 0)
            NativeMethods.SetFocus(this.Handle);
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

    /// <summary>Paints the update region by driving the managed <see cref="Paint"/> handler over a GDI surface.</summary>
    private void OnPaintMessage(nint hwnd)
    {
        var hdc = NativeMethods.BeginPaint(hwnd, out var ps);
        try
        {
            var graphics = new Win32Graphics(hdc);
            var clip = Rectangle.FromLTRB(ps.rcPaint.left, ps.rcPaint.top, ps.rcPaint.right, ps.rcPaint.bottom);
            this.Paint?.Invoke(this, new PaintEventArgs(graphics, clip));
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, in ps);
        }
    }

    /// <summary>Raises a button-changed event, decoding the client-space coordinates from the message.</summary>
    private void RaiseMouse(EventHandler<MouseEventArgs>? handler, MouseButtons button, nint lParam)
        => handler?.Invoke(this, new MouseEventArgs(button, LoWord(lParam), HiWord(lParam), 0));

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

    /// <summary>Raises <see cref="MouseWheel"/>, extracting the signed notch delta from the high word.</summary>
    private void OnMouseWheelMessage(nint wParam, nint lParam)
    {
        var delta = (short)((wParam >> 16) & 0xFFFF);
        this.MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, LoWord(lParam), HiWord(lParam), delta));
    }

    /// <summary>Raises a key-changed event, mapping the virtual key and current modifier state.</summary>
    private void RaiseKey(EventHandler<KeyEventArgs>? handler, nint wParam)
        => handler?.Invoke(this, new KeyEventArgs((Keys)(int)wParam, CurrentModifiers()));

    /// <summary>Extracts the low word of a message parameter as a signed 16-bit coordinate.</summary>
    private static int LoWord(nint value) => (short)(value & 0xFFFF);

    /// <summary>Extracts the high word of a message parameter as a signed 16-bit coordinate.</summary>
    private static int HiWord(nint value) => (short)((value >> 16) & 0xFFFF);

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
                    peer.RaiseKey(peer.KeyDown, wParam);
                    return 0;

                case NativeMethods.WM_KEYUP:
                    peer.RaiseKey(peer.KeyUp, wParam);
                    return 0;

                case NativeMethods.WM_CHAR:
                    peer.KeyPress?.Invoke(peer, new KeyPressEventArgs((char)wParam));
                    return 0;

                case NativeMethods.WM_SETFOCUS:
                    peer.GotFocus?.Invoke(peer, EventArgs.Empty);
                    return 0;

                case NativeMethods.WM_KILLFOCUS:
                    peer.LostFocus?.Invoke(peer, EventArgs.Empty);
                    return 0;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
