using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a text box: a <c>GtkEntry</c> while single-line, a <c>GtkTextView</c> inside a
/// <c>GtkScrolledWindow</c> while multiline. GTK has no single widget covering both, so
/// <see cref="SetMultiline"/> captures the live state into the buffers, destroys the old widget and
/// realizes the other kind in its place — the same buffer-and-reflush pattern realization already
/// relies on, invisible to the core.
/// </summary>
/// <remarks>
/// <c>GtkTextView</c> offers no native placeholder and no maximum length, so both settings are
/// single-line-only until an owner-drawn hint / core-side limit is added. Character casing is
/// normalized by the core.
/// </remarks>
internal sealed class GtkTextBoxPeer : GtkControlPeer, ITextBoxPeer
{
    private bool _multiline;
    private string _placeholder = string.Empty;
    private char _passwordChar;
    private bool _readOnly;
    private int _maxLength;
    private int _selectionStart;
    private int _selectionLength;

    /// <summary>The <c>GtkTextView</c> inside the scrolled window, or 0 while single-line.</summary>
    private nint _textView;

    /// <summary>How deep the peer currently is inside its own "changed" emissions — a corrective
    /// <c>gtk_entry_set_text</c> nests one inside another — see <see cref="_caretToRestore"/>.</summary>
    private int _changeDepth;

    /// <summary>
    /// The caret the core asked for while the widget was reporting a change, or -1. GTK finishes a
    /// keystroke with <c>gtk_editable_set_position</c> <em>after</em> the insertion — and therefore
    /// after "changed" — so a caret placed from a change handler is silently overwritten. The wish is
    /// parked here and re-applied once the event that carried the edit has been dispatched.
    /// </summary>
    private int _caretToRestore = -1;

    /// <summary>Whether a caret restoration is already queued, so one edit queues at most one.</summary>
    private bool _caretRestoreQueued;

    /// <inheritdoc />
    public event EventHandler? TextChangedByUser;

    /// <inheritdoc />
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <summary>The multiline view's <c>GtkTextBuffer</c> (owned by the view).</summary>
    private nint Buffer => NativeMethods.gtk_text_view_get_buffer(_textView);

    /// <summary>Focus goes to the inner <c>GtkTextView</c> while multiline — the scrolled window never takes it.</summary>
    private protected override nint FocusWidget => _multiline ? _textView : _widget;

    /// <inheritdoc />
    protected override nint CreateWidget()
    {
        if (!_multiline)
            return NativeMethods.gtk_entry_new();

        var scrolled = NativeMethods.gtk_scrolled_window_new(0, 0);
        _textView = NativeMethods.gtk_text_view_new();
        NativeMethods.gtk_container_add(scrolled, _textView);
        NativeMethods.gtk_widget_set_visible(_textView, 1);
        return scrolled;
    }

    /// <inheritdoc />
    protected override void ApplyText(string text)
    {
        if (_multiline)
            NativeMethods.gtk_text_buffer_set_text(this.Buffer, text, -1);
        else
            NativeMethods.gtk_entry_set_text(_widget, text);
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        this.ApplyText(_text);
        this.FlushEditState();

        // Connected after the initial flush so it only reports real changes; the widget is recreated
        // on a multiline flip, so the (still allocated) pinning handle is reused.
        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnChanged;
            NativeMethods.g_signal_connect_data(
                _multiline ? this.Buffer : _widget, "changed", callback, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            // Connected (not "_after") on the editing widget itself, so the owning control gets first
            // refusal on every key before GtkEntry/GtkTextView runs its own binding for it. A
            // G_CONNECT_AFTER handler would be useless here: "key-press-event" accumulates on the
            // handled flag, so the widget consuming the key ends the emission before it.
            var keyPressed = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKeyPress;
            NativeMethods.g_signal_connect_data(
                _multiline ? _textView : _widget, "key-press-event", keyPressed, GCHandle.ToIntPtr(_selfHandle), 0, 0);
        }
    }

    /// <summary>Ahead of event dispatch, so a corrected caret is in place before the next keystroke.</summary>
    private const int _GPriorityHigh = -100;

    /// <inheritdoc />
    public void SetMultiline(bool multiline)
    {
        if (_multiline == multiline)
            return;

        if (_widget == 0)
        {
            _multiline = multiline;
            return;
        }

        // Capture the live state into the buffers, then swap the widget kind and re-flush.
        _text = this.GetText();
        (_selectionStart, _selectionLength) = this.GetSelection();
        _multiline = multiline;

        var parent = _parentFixed;
        NativeMethods.gtk_widget_destroy(_widget);
        _widget = 0;
        _textView = 0;
        if (parent != 0)
            this.Realize(parent);
    }

    /// <inheritdoc />
    public void SetPlaceholder(string placeholder)
    {
        _placeholder = placeholder ?? string.Empty;
        if (_widget != 0 && !_multiline)
            NativeMethods.gtk_entry_set_placeholder_text(_widget, _placeholder);
    }

    /// <inheritdoc />
    public void SetPasswordChar(char passwordChar)
    {
        _passwordChar = passwordChar;
        if (_widget == 0 || _multiline)
            return;

        NativeMethods.gtk_entry_set_visibility(_widget, Bool(passwordChar == '\0'));
        if (passwordChar != '\0')
            NativeMethods.gtk_entry_set_invisible_char(_widget, passwordChar);
    }

    /// <inheritdoc />
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        if (_widget == 0)
            return;

        if (_multiline)
            NativeMethods.gtk_text_view_set_editable(_textView, Bool(!readOnly));
        else
            NativeMethods.gtk_editable_set_editable(_widget, Bool(!readOnly));
    }

    /// <inheritdoc />
    public void SetMaxLength(int maxLength)
    {
        _maxLength = maxLength;
        if (_widget != 0 && !_multiline)
            NativeMethods.gtk_entry_set_max_length(_widget, maxLength);
    }

    /// <inheritdoc />
    public void SetSelection(int start, int length)
    {
        _selectionStart = start;
        _selectionLength = length;
        if (_changeDepth > 0 && length == 0)
            this.QueueCaretRestore(start);

        if (_widget == 0)
            return;

        if (_multiline)
        {
            var buffer = this.Buffer;
            NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var bound, start);
            NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var ins, start + length);
            NativeMethods.gtk_text_buffer_select_range(buffer, in ins, in bound);
        }
        else
            NativeMethods.gtk_editable_select_region(_widget, start, start + length);
    }

    /// <inheritdoc />
    public (int Start, int Length) GetSelection()
    {
        if (_widget == 0)
            return (_selectionStart, _selectionLength);

        if (_multiline)
        {
            NativeMethods.gtk_text_buffer_get_selection_bounds(this.Buffer, out var startIter, out var endIter);
            var start = NativeMethods.gtk_text_iter_get_offset(in startIter);
            var end = NativeMethods.gtk_text_iter_get_offset(in endIter);
            return (start, end - start);
        }

        NativeMethods.gtk_editable_get_selection_bounds(_widget, out var startPos, out var endPos);
        return (startPos, endPos - startPos);
    }

    /// <inheritdoc />
    public string GetText()
    {
        if (_widget == 0)
            return _text;

        if (!_multiline)
            return Marshal.PtrToStringUTF8(NativeMethods.gtk_entry_get_text(_widget)) ?? string.Empty;

        var buffer = this.Buffer;
        NativeMethods.gtk_text_buffer_get_bounds(buffer, out var start, out var end);
        var ptr = NativeMethods.gtk_text_buffer_get_text(buffer, in start, in end, 0);
        var text = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        NativeMethods.g_free(ptr);
        return text;
    }

    /// <summary>Pushes the edit-specific buffered state onto a freshly created widget.</summary>
    private void FlushEditState()
    {
        if (_multiline)
            NativeMethods.gtk_text_view_set_editable(_textView, Bool(!_readOnly));
        else
        {
            if (_placeholder.Length != 0)
                NativeMethods.gtk_entry_set_placeholder_text(_widget, _placeholder);

            if (_passwordChar != '\0')
            {
                NativeMethods.gtk_entry_set_visibility(_widget, 0);
                NativeMethods.gtk_entry_set_invisible_char(_widget, _passwordChar);
            }

            if (_maxLength != 0)
                NativeMethods.gtk_entry_set_max_length(_widget, _maxLength);

            NativeMethods.gtk_editable_set_editable(_widget, Bool(!_readOnly));
        }

        this.SetSelection(_selectionStart, _selectionLength);
    }

    /// <summary>Raises <see cref="TextChangedByUser"/>; invoked from the native "changed" callback.</summary>
    private void RaiseTextChanged()
    {
        if (_changeDepth == 0)
            _caretToRestore = -1;

        ++_changeDepth;
        try
        {
            TextChangedByUser?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            --_changeDepth;
        }
    }

    /// <summary>
    /// Parks a caret the widget is about to overwrite and queues its restoration as a high-priority
    /// idle — the first moment after the current event has been dispatched, and still ahead of the
    /// next queued event. The peer is kept alive by a handle of its own for the hop, the same
    /// pattern <c>Post</c> uses, so a peer disposed in between cannot be resurrected through a
    /// dangling pin.
    /// </summary>
    private void QueueCaretRestore(int caret)
    {
        _caretToRestore = caret;
        if (_caretRestoreQueued)
            return;

        _caretRestoreQueued = true;
        var handle = GCHandle.Alloc(this);
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&OnCaretIdle;
            NativeMethods.g_idle_add_full(_GPriorityHigh, callback, GCHandle.ToIntPtr(handle), 0);
        }
    }

    /// <summary>Re-applies the parked caret; see <see cref="QueueCaretRestore"/>.</summary>
    private void RestoreCaret()
    {
        _caretRestoreQueued = false;
        var caret = _caretToRestore;
        _caretToRestore = -1;
        if (caret >= 0 && _widget != 0)
            this.SetSelection(caret, 0);
    }

    /// <summary>Raises <see cref="KeyDown"/> and reports whether a handler consumed the key.</summary>
    private bool RaiseKeyDown(Keys key, KeyModifiers modifiers)
    {
        if (KeyDown is not { } handler)
            return false;

        var args = new KeyEventArgs(key, modifiers);
        handler(this, args);
        return args.Handled;
    }

    /// <summary>
    /// Native handler for the entry's (or the multiline buffer's) "changed" signal, shaped as
    /// <c>void (GObject *emitter, gpointer user_data)</c>; recovers the peer from
    /// <paramref name="userData"/>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnChanged(nint emitter, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkTextBoxPeer peer)
            peer.RaiseTextChanged();
    }

    /// <summary>
    /// Native "key-press-event" handler, shaped as <c>gboolean (GtkWidget*, GdkEvent*, gpointer)</c>.
    /// Returning <c>GDK_EVENT_STOP</c> for a key a managed handler claimed keeps it out of the
    /// editor; everything else propagates and the widget edits as usual.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnKeyPress(nint widget, nint eventPtr, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkTextBoxPeer peer)
            return 0;

        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventKey>((void*)eventPtr);
            return peer.RaiseKeyDown(GtkCanvasPeer.ToKey(e.KeyVal), GtkCanvasPeer.ToModifiers(e.State)) ? 1 : 0;
        }
    }

    /// <summary>
    /// Native <c>GSourceFunc</c> shaped as <c>gboolean (gpointer user_data)</c>: applies the parked
    /// caret once, frees the handle that carried the peer across the hop and retires the source.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnCaretIdle(nint userData)
    {
        if (userData == 0)
            return 0;

        var handle = GCHandle.FromIntPtr(userData);
        if (handle.Target is GtkTextBoxPeer peer)
            peer.RestoreCaret();

        handle.Free();
        return 0;
    }
}
