using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A text input box. Backed by the platform's native editor (a Win32 <c>EDIT</c>, a
/// <c>GtkEntry</c>/<c>GtkTextView</c>), so caret, selection, clipboard and IME behave exactly like
/// every other text field on the user's desktop.
/// </summary>
/// <remarks>
/// Every setting is buffered until realization and flushed into the peer when the native widget is
/// created. User edits flow back through <see cref="ITextBoxPeer.TextChangedByUser"/>, updating
/// <see cref="Text"/> and raising <see cref="Control.TextChanged"/> exactly once; programmatic
/// writes push to the widget without echoing a second event. <see cref="CharacterCasing"/> is
/// normalized here in the core — on assignment and on user input alike — so it behaves identically
/// on every backend.
/// </remarks>
public class TextBox : Control
{
    /// <summary>The platform-standard masking glyph used when <see cref="UseSystemPasswordChar"/> is on.</summary>
    private const char _SystemPasswordChar = '●';

    private ITextBoxPeer? _peer;
    private string _text = string.Empty;
    private int _selectionStart;
    private int _selectionLength;

    /// <summary>Whether the core is currently writing to the widget, so its echo is not a user edit.</summary>
    private bool _pushing;

    /// <summary>
    /// The content of the box. Assigned text is normalized by <see cref="CharacterCasing"/> and
    /// pushed to the native widget; user edits arrive here through the peer and raise
    /// <see cref="Control.TextChanged"/> exactly once.
    /// </summary>
    public override string Text
    {
        get => _text;
        set
        {
            value = this.NormalizeText(value ?? string.Empty);
            if (_text == value)
                return;

            _text = value;
            this.PushText(value);
            this.OnTextChanged(EventArgs.Empty);
        }
    }

    /// <summary>Whether the box is a multiline editor (with vertical scrolling) rather than a single-line entry.</summary>
    public bool Multiline
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetMultiline(value);
        }
    }

    /// <summary>The greyed hint shown while the box is empty. Single-line only on most platforms.</summary>
    public string PlaceholderText
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _peer?.SetPlaceholder(value);
        }
    } = string.Empty;

    /// <summary>The character that masks displayed text, or <c>'\0'</c> for no masking.</summary>
    public char PasswordChar
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetPasswordChar(this.EffectivePasswordChar);
        }
    }

    /// <summary>Masks input with the platform's standard password glyph, overriding <see cref="PasswordChar"/>.</summary>
    public bool UseSystemPasswordChar
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetPasswordChar(this.EffectivePasswordChar);
        }
    }

    /// <summary>Whether the text can be selected and copied but not edited.</summary>
    public bool ReadOnly
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetReadOnly(value);
        }
    }

    /// <summary>The maximum number of characters the user can type; 0 means unlimited.</summary>
    public int MaxLength
    {
        get => field;
        set
        {
            value = Math.Max(0, value);
            if (field == value)
                return;

            field = value;
            _peer?.SetMaxLength(value);
        }
    }

    /// <summary>Whether Enter is kept by a multiline box (a newline) instead of activating the form's
    /// <see cref="Form.AcceptButton"/>. Steered through the peer key seam: while set on a multiline
    /// box, Enter is an <see cref="IsInputKey"/> the editor handles; otherwise it reaches the default
    /// button.</summary>
    public bool AcceptsReturn { get; set; }

    /// <summary>Whether Tab is kept by the box (a tab character) instead of moving focus. Steered
    /// through the peer key seam: while set, an unmodified Tab is an <see cref="IsInputKey"/> the
    /// editor handles; otherwise it navigates.</summary>
    public bool AcceptsTab { get; set; }

    /// <summary>
    /// Forces the content to a fixed casing. Changing it re-cases the current <see cref="Text"/>;
    /// while active, programmatic writes and user input are normalized alike.
    /// </summary>
    public CharacterCasing CharacterCasing
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Text = _text;
        }
    }

    /// <summary>The index of the first selected character (the caret position when nothing is selected).</summary>
    public int SelectionStart
    {
        get => _peer?.GetSelection().Start ?? _selectionStart;
        set
        {
            value = Math.Max(0, value);
            var length = this.SelectionLength;
            _selectionStart = value;
            _peer?.SetSelection(value, length);
        }
    }

    /// <summary>The number of selected characters.</summary>
    public int SelectionLength
    {
        get => _peer?.GetSelection().Length ?? _selectionLength;
        set
        {
            value = Math.Max(0, value);
            var start = this.SelectionStart;
            _selectionLength = value;
            _peer?.SetSelection(start, value);
        }
    }

    /// <summary>The currently selected run of <see cref="Text"/>; assigning replaces the selection.</summary>
    public string SelectedText
    {
        get
        {
            var text = _text;
            var start = Math.Clamp(this.SelectionStart, 0, text.Length);
            var length = Math.Clamp(this.SelectionLength, 0, text.Length - start);
            return text.Substring(start, length);
        }
        set
        {
            value ??= string.Empty;
            var text = _text;
            var start = Math.Clamp(this.SelectionStart, 0, text.Length);
            var length = Math.Clamp(this.SelectionLength, 0, text.Length - start);
            this.Text = string.Concat(text.AsSpan(0, start), value, text.AsSpan(start + length));
            this.SelectionStart = start + ApplyCasing(this.CharacterCasing, value).Length;
            this.SelectionLength = 0;
        }
    }

    /// <summary>Selects the given run of text (the caret moves to its start); negative values clamp
    /// to zero. Buffered until realization and forwarded to the live widget like the
    /// <see cref="SelectionStart"/>/<see cref="SelectionLength"/> setters.</summary>
    public void Select(int start, int length)
    {
        _selectionStart = Math.Max(0, start);
        _selectionLength = Math.Max(0, length);
        _peer?.SetSelection(_selectionStart, _selectionLength);
    }

    /// <summary>
    /// Raised for a key pressed inside the native editor, before the editor acts on it; a handler
    /// that sets <see cref="KeyEventArgs.Handled"/> keeps the key away from the widget. This is what
    /// lets a composite hosting this box (a <see cref="SearchBox"/>, a spinner, a grid cell editor)
    /// claim Enter, Escape or the arrows while the editor keeps the caret.
    /// </summary>
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <summary>Raises <see cref="KeyDown"/>.</summary>
    protected virtual void OnKeyDown(KeyEventArgs e) => this.KeyDown?.Invoke(this, e);

    /// <summary>Selects the whole content.</summary>
    public void SelectAll() => this.Select(0, _text.Length);

    /// <summary>Empties the box, raising <see cref="Control.TextChanged"/> when it held text.</summary>
    public void Clear() => this.Text = string.Empty;

    /// <summary>Appends <paramref name="text"/> to the content and parks the caret at the end —
    /// the classic log-window helper.</summary>
    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        this.Text = _text + text;
        this.Select(_text.Length, 0);
    }

    /// <summary>The mask character actually pushed to the peer, honoring <see cref="UseSystemPasswordChar"/>.</summary>
    private char EffectivePasswordChar => this.UseSystemPasswordChar ? _SystemPasswordChar : this.PasswordChar;

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateTextBox();

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not ITextBoxPeer textBox)
            return;

        _peer = textBox;
        textBox.TextChangedByUser += this.OnPeerTextChanged;
        textBox.KeyDown += this.OnPeerKeyDown;
        textBox.SetMultiline(this.Multiline);
        textBox.SetPlaceholder(this.PlaceholderText);
        textBox.SetPasswordChar(this.EffectivePasswordChar);
        textBox.SetReadOnly(this.ReadOnly);
        textBox.SetMaxLength(this.MaxLength);
        textBox.SetSelection(_selectionStart, _selectionLength);
    }

    private protected override void OnUnrealized()
    {
        if (_peer is null)
            return;

        (_selectionStart, _selectionLength) = _peer.GetSelection();
        _peer.TextChangedByUser -= this.OnPeerTextChanged;
        _peer.KeyDown -= this.OnPeerKeyDown;
        _peer = null;
    }

    /// <summary>
    /// Routes a key the widget reported first through the form's dialog-key chain — Enter to the
    /// <see cref="Form.AcceptButton"/>, Escape to the <see cref="Form.CancelButton"/>, Tab/Shift+Tab
    /// to navigation, menu shortcuts and Alt+mnemonics — and, when nothing there consumes it, to the
    /// <see cref="OnKeyDown"/> hook. A consumed key is marked handled so the native editor never sees
    /// it. Keys the box wants for itself (<see cref="IsInputKey"/> — a newline while
    /// <see cref="AcceptsReturn"/>, a tab while <see cref="AcceptsTab"/>) are left to the editor.
    /// </summary>
    private void OnPeerKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.FindForm() is { } form && form.ProcessDialogKey(this, e))
        {
            e.Handled = true;
            return;
        }

        this.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Enter => this.Multiline && this.AcceptsReturn,
        Keys.Tab => this.AcceptsTab && (keyData & Keys.Modifiers) == 0,
        _ => base.IsInputKey(keyData),
    };

    /// <summary>
    /// Syncs a text change reported by the widget back into <see cref="Text"/>. Guarded by value
    /// comparison — exactly like the <c>Checked</c>-style properties — so echoes of programmatic
    /// writes and of the casing correction below never raise a second
    /// <see cref="Control.TextChanged"/>. The widget's caret travels with the candidate, because a
    /// normalizer that rewrites the value has to be able to say where the caret belongs afterwards.
    /// </summary>
    private void OnPeerTextChanged(object? sender, EventArgs e)
    {
        var peer = _peer;
        if (peer is null || _pushing)
            return;

        var raw = peer.GetText();
        var value = this.NormalizeUserEdit(raw, peer.GetSelection().Start, out var caret);
        var changed = _text != value;
        _text = value;
        if (value != raw)
        {
            this.PushText(value);
            if (caret >= 0)
                peer.SetSelection(Math.Min(caret, value.Length), 0);
        }

        if (changed)
            this.OnTextChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Writes text into the widget without letting the echo come back as a user edit. Native editors
    /// do not replace their content atomically — <c>gtk_entry_set_text</c> empties the entry before it
    /// refills it — so an unguarded push reports an intermediate empty value, which a normalizer that
    /// reads edits differentially would faithfully interpret as the user clearing the box.
    /// </summary>
    private void PushText(string value)
    {
        if (_peer is not { } peer)
            return;

        _pushing = true;
        try
        {
            peer.SetText(value);
        }
        finally
        {
            _pushing = false;
        }
    }

    /// <summary>
    /// Normalizes a candidate the <em>widget</em> produced, knowing where the edit left the caret.
    /// The base implementation is the plain <see cref="NormalizeText"/> whole-value pass and asks for
    /// no caret correction (<paramref name="correctedCaret"/> stays negative); the masked box
    /// overrides it, because reconstructing which keystroke happened — an insertion at the caret, a
    /// deletion, a replaced selection — is only possible from the previous value, the candidate and
    /// the caret together.
    /// </summary>
    /// <param name="value">The live text the widget reports.</param>
    /// <param name="caret">Where the edit started — see <see cref="ITextBoxPeer.GetSelection"/> for
    /// the guarantee peers give while they are reporting a change.</param>
    /// <param name="correctedCaret">Where the caret must be placed when the value was rewritten, or
    /// a negative number to leave the widget's own caret alone.</param>
    private protected virtual string NormalizeUserEdit(string value, int caret, out int correctedCaret)
    {
        correctedCaret = -1;
        return this.NormalizeText(value);
    }

    /// <summary>
    /// Normalizes a candidate text value before it is stored, pushed to the widget or compared. The
    /// base implementation applies <see cref="CharacterCasing"/>; subclasses refine it — the masked
    /// box routes every candidate through its mask here, which is what makes the corrective push in
    /// <see cref="OnPeerTextChanged"/> (write the normalized value back when the widget disagrees)
    /// work for casing and masking alike. Must be idempotent: normalizing an already-normalized
    /// value has to return it unchanged, or the correction would echo forever.
    /// </summary>
    private protected virtual string NormalizeText(string value) => ApplyCasing(this.CharacterCasing, value);

    /// <summary>Applies <paramref name="casing"/> to <paramref name="value"/>.</summary>
    private static string ApplyCasing(CharacterCasing casing, string value) => casing switch
    {
        CharacterCasing.Upper => value.ToUpperInvariant(),
        CharacterCasing.Lower => value.ToLowerInvariant(),
        _ => value,
    };
}
