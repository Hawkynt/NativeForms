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
            value = ApplyCasing(this.CharacterCasing, value ?? string.Empty);
            if (_text == value)
                return;

            _text = value;
            _peer?.SetText(value);
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

    /// <summary>Whether Enter inserts a newline in a multiline box instead of activating the default button.</summary>
    public bool AcceptsReturn { get; set; }

    /// <summary>Whether Tab inserts a tab character in a multiline box instead of moving focus.</summary>
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

    /// <summary>The mask character actually pushed to the peer, honoring <see cref="UseSystemPasswordChar"/>.</summary>
    private char EffectivePasswordChar => this.UseSystemPasswordChar ? _SystemPasswordChar : this.PasswordChar;

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateTextBox();

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not ITextBoxPeer textBox)
            return;

        _peer = textBox;
        textBox.TextChangedByUser += this.OnPeerTextChanged;
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
        _peer = null;
    }

    /// <summary>
    /// Syncs a text change reported by the widget back into <see cref="Text"/>. Guarded by value
    /// comparison — exactly like the <c>Checked</c>-style properties — so echoes of programmatic
    /// writes and of the casing correction below never raise a second
    /// <see cref="Control.TextChanged"/>.
    /// </summary>
    private void OnPeerTextChanged(object? sender, EventArgs e)
    {
        var peer = _peer;
        if (peer is null)
            return;

        var raw = peer.GetText();
        var value = ApplyCasing(this.CharacterCasing, raw);
        var changed = _text != value;
        _text = value;
        if (value != raw)
            peer.SetText(value);

        if (changed)
            this.OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Applies <paramref name="casing"/> to <paramref name="value"/>.</summary>
    private static string ApplyCasing(CharacterCasing casing, string value) => casing switch
    {
        CharacterCasing.Upper => value.ToUpperInvariant(),
        CharacterCasing.Lower => value.ToLowerInvariant(),
        _ => value,
    };
}
