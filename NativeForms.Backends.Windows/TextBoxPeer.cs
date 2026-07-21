using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a text box — a native <c>EDIT</c> child window. Single-line boxes use
/// <c>ES_AUTOHSCROLL</c>; multiline boxes use <c>ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL</c>.
/// Those are creation-time styles, so <see cref="SetMultiline"/> destroys and recreates the HWND
/// and re-flushes the buffered state — invisible to the core, which only ever talks to the peer.
/// User edits arrive as <c>EN_CHANGE</c> notifications through the parent's <c>WM_COMMAND</c>
/// routing, exactly like button clicks.
/// </summary>
/// <remarks>
/// The cue banner (<c>EM_SETCUEBANNER</c>) only exists on single-line EDIT controls, so multiline
/// boxes show no placeholder until an owner-drawn hint is added. Character casing is normalized by
/// the core, so no <c>ES_UPPERCASE</c>/<c>ES_LOWERCASE</c> style bits are needed here.
///
/// Keys have no <c>WM_COMMAND</c> notification, so <see cref="KeyDown"/> comes from a window-procedure
/// subclass on the EDIT: the replacement proc is a static function pointer and the peer is recovered
/// from a handle-keyed map, never from a captured closure or a marshalled delegate.
/// </remarks>
internal unsafe class TextBoxPeer : Win32ChildPeer, ITextBoxPeer
{
    /// <summary>Maps a live EDIT window to its peer so the static <see cref="EditProc"/> can find it.</summary>
    private static readonly ConcurrentDictionary<nint, TextBoxPeer> _edits = new();

    /// <summary>The window procedure the EDIT class installed, chained to for everything unclaimed.</summary>
    private nint _baseProc;

    /// <summary>Whether the peer is reporting a change — see <see cref="GetSelection"/>.</summary>
    private bool _inChange;

    private bool _multiline;
    private string _placeholder = string.Empty;
    private char _passwordChar;
    private bool _readOnly;
    private int _maxLength;
    private int _selectionStart;
    private int _selectionLength;
    private nint _parentHandle;
    private int _controlId;

    /// <inheritdoc/>
    public event EventHandler? TextChangedByUser;

    /// <inheritdoc/>
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <inheritdoc/>
    protected override string WindowClass => "EDIT";

    /// <inheritdoc/>
    protected override uint ExtraStyle
        => NativeMethods.WS_TABSTOP
           | NativeMethods.WS_BORDER
           | (_multiline
               ? NativeMethods.ES_MULTILINE | NativeMethods.ES_AUTOVSCROLL | NativeMethods.WS_VSCROLL
               : NativeMethods.ES_AUTOHSCROLL);

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        _parentHandle = parent;
        _controlId = controlId;
        base.CreateChildHandle(parent, controlId);
        this.Subclass();
        this.FlushEditState();
    }

    /// <summary>Installs the key-intercepting window procedure on the freshly created EDIT.</summary>
    private void Subclass()
    {
        if (Handle == 0)
            return;

        _edits[Handle] = this;
        _baseProc = NativeMethods.SetWindowLongPtrW(
            Handle,
            NativeMethods.GWLP_WNDPROC,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&EditProc);
    }

    /// <summary>Restores the EDIT's own window procedure and forgets the handle.</summary>
    private void Unsubclass()
    {
        if (Handle == 0)
            return;

        if (_baseProc != 0)
        {
            NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWLP_WNDPROC, _baseProc);
            _baseProc = 0;
        }

        _edits.TryRemove(Handle, out _);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.Unsubclass();
        base.Dispose();
    }

    /// <summary>
    /// The subclassed EDIT procedure: gives the owning control first refusal on every key down and
    /// swallows the ones it claims, then chains to the control's own behavior.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint EditProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!_edits.TryGetValue(hwnd, out var peer))
            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);

        if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN && peer.RaiseKeyDown(wParam))
            return 0;

        return NativeMethods.CallWindowProcW(peer._baseProc, hwnd, msg, wParam, lParam);
    }

    /// <summary>Raises <see cref="KeyDown"/> and reports whether a handler consumed the key.</summary>
    private bool RaiseKeyDown(nint virtualKey)
    {
        if (KeyDown is not { } handler)
            return false;

        var args = new KeyEventArgs((Keys)(int)virtualKey, Win32CanvasPeer.CurrentModifiers());
        handler(this, args);
        return args.Handled;
    }

    /// <inheritdoc/>
    public void SetMultiline(bool multiline)
    {
        if (_multiline == multiline)
            return;

        _multiline = multiline;
        if (Handle == 0)
            return;

        // ES_MULTILINE cannot be toggled on a live EDIT window: capture the live text and selection
        // into the buffers, tear the HWND down and rebuild it with the new style bits. The control
        // id is reused, so the parent's WM_COMMAND routing keeps working unchanged.
        this.SetText(this.GetText());
        (_selectionStart, _selectionLength) = this.GetSelection();
        this.Unsubclass();
        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
        this.CreateChildHandle(_parentHandle, _controlId);
    }

    /// <inheritdoc/>
    public void SetPlaceholder(string placeholder)
    {
        _placeholder = placeholder ?? string.Empty;
        if (Handle != 0 && !_multiline)
            NativeMethods.SendMessageStringW(Handle, NativeMethods.EM_SETCUEBANNER, 1, _placeholder);
    }

    /// <inheritdoc/>
    public void SetPasswordChar(char passwordChar)
    {
        _passwordChar = passwordChar;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETPASSWORDCHAR, passwordChar, 0);
    }

    /// <inheritdoc/>
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETREADONLY, readOnly ? 1 : 0, 0);
    }

    /// <inheritdoc/>
    public void SetMaxLength(int maxLength)
    {
        _maxLength = maxLength;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETLIMITTEXT, maxLength, 0);
    }

    /// <inheritdoc/>
    public void SetSelection(int start, int length)
    {
        _selectionStart = start;
        _selectionLength = length;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETSEL, start, start + length);
    }

    /// <inheritdoc/>
    public (int Start, int Length) GetSelection()
    {
        if (Handle == 0)
            return (_selectionStart, _selectionLength);

        int start, end;
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_GETSEL, (nint)(&start), (nint)(&end));

        // EN_CHANGE arrives once the EDIT has finished the edit and moved its caret past what was
        // inserted, so during a change the caret is walked back to where the edit began — the
        // convention ITextBoxPeer.GetSelection promises, and the one a GtkEntry reports natively.
        // _text still holds the value the core last pushed, which is exactly the pre-edit content.
        if (_inChange)
            start -= Math.Max(0, GetTextLength() - _text.Length);

        return (Math.Max(0, start), end - start);
    }

    /// <summary>The character count the EDIT currently holds.</summary>
    private int GetTextLength() => Handle == 0 ? _text.Length : NativeMethods.GetWindowTextLengthW(Handle);

    /// <inheritdoc/>
    public string GetText()
    {
        if (Handle == 0)
            return _text;

        var length = NativeMethods.GetWindowTextLengthW(Handle);
        if (length == 0)
            return string.Empty;

        var buffer = new char[length + 1];
        fixed (char* p = buffer)
            length = NativeMethods.GetWindowTextW(Handle, p, buffer.Length);

        return new string(buffer, 0, length);
    }

    /// <inheritdoc/>
    internal override void OnCommand(int notifyCode)
    {
        switch (notifyCode)
        {
            case NativeMethods.EN_CHANGE:
                _inChange = true;
                try
                {
                    TextChangedByUser?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    _inChange = false;
                }

                break;

            case NativeMethods.EN_SETFOCUS:
                RaiseGotFocus();
                break;

            case NativeMethods.EN_KILLFOCUS:
                RaiseLostFocus();
                break;
        }
    }

    /// <summary>Pushes the edit-specific buffered state onto a freshly created HWND.</summary>
    private void FlushEditState()
    {
        if (Handle == 0)
            return;

        if (_passwordChar != '\0')
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETPASSWORDCHAR, _passwordChar, 0);

        if (!_multiline && _placeholder.Length != 0)
            NativeMethods.SendMessageStringW(Handle, NativeMethods.EM_SETCUEBANNER, 1, _placeholder);

        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETREADONLY, _readOnly ? 1 : 0, 0);
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETLIMITTEXT, _maxLength, 0);
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETSEL, _selectionStart, _selectionStart + _selectionLength);
    }
}
