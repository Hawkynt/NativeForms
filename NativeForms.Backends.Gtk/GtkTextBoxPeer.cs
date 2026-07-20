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

    /// <inheritdoc />
    public event EventHandler? TextChangedByUser;

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
        }
    }

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
    private void RaiseTextChanged() => TextChangedByUser?.Invoke(this, EventArgs.Empty);

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
}
