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
/// The cue banner (<c>EM_SETCUEBANNER</c>) only exists on single-line EDIT controls — a multiline box
/// accepts the message and shows nothing — so the multiline hint is painted over the empty control
/// instead, out of the same subclass the keys come through. Character casing is normalized by the
/// core, so no <c>ES_UPPERCASE</c>/<c>ES_LOWERCASE</c> style bits are needed here.
///
/// A line break is <c>\n</c> everywhere above this class and <c>\r\n</c> inside the widget, so it is
/// translated on the way in and back on the way out. An EDIT breaks a line on the pair alone — a bare
/// <c>\n</c> is a character it has no glyph for and draws as nothing, which is how a three-line box
/// photographed as one line — while the toolkit cannot let the convention become platform-dependent
/// or <c>Lines</c>, <c>TextLength</c> and every caret index would mean different things per backend.
/// The character indices <c>EM_SETSEL</c> and <c>EM_GETSEL</c> speak count that pair as two, so they
/// are mapped as well, and only for a multiline box: a single-line EDIT holds no break at all, so the
/// two numberings are the same one and nothing is walked.
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
    /// <summary>This peer's identity in the window's subclass chain; distinct from the base's.</summary>
    private const nuint _EditSubclassId = 2;

    /// <summary>Whether the peer is reporting a change — see <see cref="GetSelection"/>.</summary>
    private bool _inChange;

    private bool _multiline;
    private bool _hasFrame = true;
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
           | (_hasFrame ? NativeMethods.WS_BORDER : 0)
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

    /// <summary>
    /// Adds the key-intercepting procedure to the EDIT's subclass chain.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>SetWindowSubclass</c> rather than swapping <c>GWLP_WNDPROC</c>: the base peer has
    /// already put a COMCTL32 subclass on this window for the pointer channel, and replacing the window
    /// procedure out from under that dispatcher is exactly what its documentation forbids — the chain is
    /// then re-entered out of band through <c>CallWindowProc</c>, which faults. Both procedures belong in
    /// the one chain, where COMCTL32 runs the last installed first, so this one still sees keys before
    /// the control does.
    /// </remarks>
    private void Subclass()
    {
        if (Handle == 0)
            return;

        _edits[Handle] = this;
        NativeMethods.SetWindowSubclass(
            Handle,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nint, nint>)&EditProc,
            _EditSubclassId,
            0);
    }

    /// <summary>Takes the procedure back out of the chain and forgets the handle.</summary>
    private void Unsubclass()
    {
        if (Handle == 0)
            return;

        NativeMethods.RemoveWindowSubclass(
            Handle,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nint, nint>)&EditProc,
            _EditSubclassId);

        _edits.TryRemove(Handle, out _);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.Unsubclass();
        base.Dispose();
    }

    /// <summary>
    /// The EDIT's subclass procedure: gives the owning control first refusal on every key down and
    /// swallows the ones it claims, then defers to the rest of the chain. The peer is found by HWND
    /// rather than through the reference data, so an entry the chain reports without one is harmless.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint EditProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        if (_edits.TryGetValue(hwnd, out var peer)
            && msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN
            && peer.RaiseKeyDown(wParam))
            return 0;

        var result = NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);

        // After the control has drawn, never instead of it: the hint is an overlay on an empty box, so
        // the EDIT still owns its background, its border and its caret. Same shape as the GTK half,
        // which hangs its placeholder off the text view's "draw" signal with G_CONNECT_AFTER.
        if (msg == NativeMethods.WM_PAINT && peer is not null)
            peer.PaintPlaceholder(hwnd);

        return result;
    }

    /// <summary>
    /// Draws the grey hint over an empty multiline box.
    /// </summary>
    /// <remarks>
    /// <c>EM_SETCUEBANNER</c> is a single-line-EDIT message — a multiline box accepts it and shows
    /// nothing — so the multiline hint has to be painted. The rectangle comes from
    /// <c>EM_GETRECT</c> rather than from the client area, because that is the box the control itself
    /// lays text out in, so the hint starts exactly where the first typed character will.
    ///
    /// It stays visible while the box has focus, which is what the GTK half does and what the caller
    /// asked for; a cue banner's default of vanishing on focus would make the two backends disagree
    /// about a property neither of them exposes.
    ///
    /// A fresh DC rather than the one <c>BeginPaint</c> handed the control: that one was released
    /// before this returns. Nothing is cached — a box that is empty is a box nobody is typing in, so
    /// this runs at rest and never on a keystroke.
    /// </remarks>
    private void PaintPlaceholder(nint hwnd)
    {
        if (!_multiline || _placeholder.Length == 0 || NativeMethods.GetWindowTextLengthW(hwnd) != 0)
            return;

        var hdc = NativeMethods.GetDC(hwnd);
        if (hdc == 0)
            return;

        var font = NativeMethods.SendMessageW(hwnd, NativeMethods.WM_GETFONT, 0, 0);
        var previousFont = font == 0 ? 0 : NativeMethods.SelectObject(hdc, font);
        var previousMode = NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
        var previousColor = NativeMethods.SetTextColor(hdc, NativeMethods.GetSysColor(NativeMethods.COLOR_GRAYTEXT));

        var rect = default(NativeMethods.RECT);
        NativeMethods.SendMessageW(hwnd, NativeMethods.EM_GETRECT, 0, (nint)(&rect));
        NativeMethods.DrawTextW(
            hdc,
            _placeholder,
            -1,
            ref rect,
            NativeMethods.DT_LEFT | NativeMethods.DT_TOP | NativeMethods.DT_NOPREFIX | NativeMethods.DT_WORDBREAK);

        NativeMethods.SetTextColor(hdc, previousColor);
        NativeMethods.SetBkMode(hdc, previousMode);
        if (previousFont != 0)
            NativeMethods.SelectObject(hdc, previousFont);

        NativeMethods.ReleaseDC(hwnd, hdc);
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

        // ES_MULTILINE cannot be toggled on a live EDIT window: capture the live text and selection
        // into the buffers, tear the HWND down and rebuild it with the new style bits. The control
        // id is reused, so the parent's WM_COMMAND routing keeps working unchanged. Both reads happen
        // before the flag moves, because which numbering EM_GETSEL is answering in is a property of
        // the window still standing rather than of the one about to be built.
        if (Handle != 0)
        {
            this.SetText(this.GetText());
            (_selectionStart, _selectionLength) = this.GetSelection();
        }

        _multiline = multiline;
        if (Handle == 0)
            return;

        this.Unsubclass();
        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
        this.CreateChildHandle(_parentHandle, _controlId);
    }

    /// <inheritdoc/>
    public void SetHasFrame(bool hasFrame)
    {
        if (_hasFrame == hasFrame)
            return;

        _hasFrame = hasFrame;
        if (Handle == 0)
            return;

        // WS_BORDER is a creation-time style on an EDIT, so rebuild the HWND with the new bit — the same
        // capture-text/selection, destroy and recreate SetMultiline uses; the control id is reused.
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
        if (Handle == 0)
            return;

        if (!_multiline)
        {
            NativeMethods.SendMessageStringW(Handle, NativeMethods.EM_SETCUEBANNER, 1, _placeholder);
            return;
        }

        // The multiline hint is painted, so nothing about the control changed and it has no reason to
        // repaint on its own — a hint replaced while the box sits empty would otherwise not appear
        // until something else invalidated it.
        NativeMethods.InvalidateRect(Handle, null, true);
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
        if (Handle == 0)
            return;

        var text = _multiline ? this.GetText() : null;
        var from = text is null ? start : NativeIndexOf(text, start);
        var to = text is null ? start + length : NativeIndexOf(text, start + length);
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETSEL, from, to);
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
        // _text still holds the value the core last pushed, which is exactly the pre-edit content —
        // measured the way the widget counts it, since the difference being taken is the widget's.
        if (_inChange)
            start -= Math.Max(0, GetTextLength() - NativeLengthOf(_text));

        start = Math.Max(0, start);
        if (end < start)
            end = start;

        if (!_multiline)
            return (start, end - start);

        var text = this.GetText();
        var from = CoreIndexOf(text, start);
        return (from, CoreIndexOf(text, end) - from);
    }

    /// <summary>The character count the EDIT currently holds, in the widget's own numbering.</summary>
    private int GetTextLength() => Handle == 0 ? NativeLengthOf(_text) : NativeMethods.GetWindowTextLengthW(Handle);

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

        return ToCoreLineEndings(new string(buffer, 0, length));
    }

    /// <inheritdoc/>
    public override void SetText(string text)
    {
        _text = text ?? string.Empty;
        if (Handle != 0)
            NativeMethods.SetWindowTextW(Handle, ToNativeLineEndings(_text));
    }

    /// <summary>The two characters a line break can be written with, for the scan that decides whether
    /// a translation is needed at all.</summary>
    private static readonly char[] _LineBreakChars = ['\r', '\n'];

    /// <summary>
    /// The widget's spelling of <paramref name="text"/>: every line break a <c>\r\n</c> pair.
    /// </summary>
    /// <remarks>
    /// A string with no break in it — which is nearly every one — is handed straight back, so the
    /// common single-line case pays one scan and allocates nothing.
    /// </remarks>
    internal static string ToNativeLineEndings(string text)
        => text.IndexOfAny(_LineBreakChars) < 0 ? text : ToCoreLineEndings(text).Replace("\n", "\r\n");

    /// <summary>
    /// The toolkit's spelling of <paramref name="text"/>: every line break a single <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// A lone <c>\r</c> folds too, because a rich edit stores a paragraph mark as one and hands it
    /// back that way — the same translation therefore serves both classes this peer builds.
    /// </remarks>
    internal static string ToCoreLineEndings(string text)
        => text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>How many characters the widget counts <paramref name="text"/> as.</summary>
    internal static int NativeLengthOf(string text)
    {
        var extra = 0;
        for (var i = 0; i < text.Length; ++i)
            if (text[i] == '\n')
                ++extra;

        return text.Length + extra;
    }

    /// <summary>The widget's index for the core index <paramref name="index"/> into <paramref name="text"/>.</summary>
    internal static int NativeIndexOf(string text, int index)
    {
        if (index <= 0)
            return 0;

        var native = 0;
        var limit = Math.Min(index, text.Length);
        for (var i = 0; i < limit; ++i)
            native += text[i] == '\n' ? 2 : 1;

        // An index past the end is clamped by the widget anyway; carrying the excess keeps a caret
        // asked for beyond the text from silently jumping backwards.
        return native + (index - limit);
    }

    /// <summary>The core index for the widget index <paramref name="index"/> into <paramref name="text"/>.</summary>
    internal static int CoreIndexOf(string text, int index)
    {
        var native = 0;
        var core = 0;
        while (core < text.Length && native < index)
        {
            native += text[core] == '\n' ? 2 : 1;
            ++core;
        }

        return core + Math.Max(0, index - native);
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
    /// <remarks>
    /// The text is written a second time here, and it has to be. The base flush puts the buffered
    /// string on the window verbatim, which is the toolkit's spelling of a line break rather than the
    /// widget's — so a box whose text was set before it was realized, which is every box built
    /// declaratively, arrived carrying bare newlines the EDIT draws as nothing. Only a string that
    /// actually holds a break is re-sent; the two spellings are the same string for every other one.
    /// The selection follows for the same reason: the buffered indices are in the core's numbering.
    /// </remarks>
    private void FlushEditState()
    {
        if (Handle == 0)
            return;

        if (_text.IndexOfAny(_LineBreakChars) >= 0)
            NativeMethods.SetWindowTextW(Handle, ToNativeLineEndings(_text));

        if (_passwordChar != '\0')
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETPASSWORDCHAR, _passwordChar, 0);

        if (!_multiline && _placeholder.Length != 0)
            NativeMethods.SendMessageStringW(Handle, NativeMethods.EM_SETCUEBANNER, 1, _placeholder);

        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETREADONLY, _readOnly ? 1 : 0, 0);
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETLIMITTEXT, _maxLength, 0);

        var end = _selectionStart + _selectionLength;
        NativeMethods.SendMessageW(
            Handle,
            NativeMethods.EM_SETSEL,
            _multiline ? NativeIndexOf(_text, _selectionStart) : _selectionStart,
            _multiline ? NativeIndexOf(_text, end) : end);
    }
}
