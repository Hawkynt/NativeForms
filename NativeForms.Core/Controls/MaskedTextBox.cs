namespace Hawkynt.NativeForms;

/// <summary>Why a candidate value was rejected by a <see cref="MaskedTextBox"/> mask; the member
/// names match their <c>System.ComponentModel.MaskedTextResultHint</c> namesakes.</summary>
public enum MaskedTextResultHint
{
    /// <summary>A character did not fit the mask slot at the reported position.</summary>
    InvalidInput,

    /// <summary>Input remained after the last mask position.</summary>
    UnavailableEditPosition,
}

/// <summary>Describes a rejected <see cref="MaskedTextBox"/> input; see <see cref="MaskedTextBox.MaskInputRejected"/>.</summary>
public sealed class MaskInputRejectedEventArgs(int position, MaskedTextResultHint rejectionHint) : EventArgs
{
    /// <summary>The mask position the rejection happened at.</summary>
    public int Position { get; } = position;

    /// <summary>Why the input was rejected.</summary>
    public MaskedTextResultHint RejectionHint { get; } = rejectionHint;
}

/// <summary>
/// A <see cref="TextBox"/> whose content is forced through an input mask — phone numbers, dates,
/// license keys. The mask engine lives entirely in the core: the control uses the plain native text
/// widget of every backend and validates <em>whole-text transitions</em>, mapping each candidate
/// value into the mask's slots and pushing the rendered result back to the widget.
/// </summary>
/// <remarks>
/// <para>
/// The mask language is the familiar Windows Forms subset: <c>0</c> required digit, <c>9</c>
/// optional digit, <c>L</c> required letter, <c>?</c> optional letter, <c>A</c> required
/// alphanumeric, <c>a</c> optional alphanumeric, <c>&amp;</c> any required character, <c>C</c> any
/// optional character; every other character is a literal, and <c>\</c> escapes the next character
/// into a literal. Unfilled slots render as <see cref="PromptChar"/>.
/// </para>
/// <para>
/// Because peers report text changes as whole values (there are no per-keystroke events on
/// <see cref="Backends.ITextBoxPeer"/>), validation is transactional: a candidate that maps cleanly
/// into the mask becomes the new content, one that does not is rejected by reverting the widget to
/// the last valid rendering — the same corrective-push mechanism <see cref="TextBox.CharacterCasing"/>
/// uses. The trade-off is honest and documented: the engine cannot steer the caret slot-by-slot the
/// way Windows Forms does, so free-form edits in the middle of the text re-flow the remaining input
/// through the mask, and a prompt character in the input always reads as an empty slot.
/// </para>
/// </remarks>
public class MaskedTextBox : TextBox
{
    /// <summary>What a single mask position accepts.</summary>
    private enum SlotKind : byte
    {
        /// <summary>A literal character that is rendered as-is and never edited.</summary>
        Literal,

        /// <summary>A digit that must be filled (<c>0</c>).</summary>
        RequiredDigit,

        /// <summary>A digit that may stay empty (<c>9</c>).</summary>
        OptionalDigit,

        /// <summary>A letter that must be filled (<c>L</c>).</summary>
        RequiredLetter,

        /// <summary>A letter that may stay empty (<c>?</c>).</summary>
        OptionalLetter,

        /// <summary>A letter or digit that must be filled (<c>A</c>).</summary>
        RequiredAlphanumeric,

        /// <summary>A letter or digit that may stay empty (<c>a</c>).</summary>
        OptionalAlphanumeric,

        /// <summary>Any character, must be filled (<c>&amp;</c>).</summary>
        RequiredAny,

        /// <summary>Any character, may stay empty (<c>C</c>).</summary>
        OptionalAny,
    }

    /// <summary>One parsed mask position: a literal character or an input slot.</summary>
    private readonly struct MaskSlot(SlotKind kind, char literal)
    {
        /// <summary>The slot's acceptance class.</summary>
        public SlotKind Kind { get; } = kind;

        /// <summary>The literal character rendered at this position (literal slots only).</summary>
        public char Literal { get; } = literal;

        /// <summary>Whether this position is a pass-through literal.</summary>
        public bool IsLiteral => this.Kind == SlotKind.Literal;

        /// <summary>Whether this slot must be filled for the mask to be complete.</summary>
        public bool IsRequired => this.Kind
            is SlotKind.RequiredDigit
            or SlotKind.RequiredLetter
            or SlotKind.RequiredAlphanumeric
            or SlotKind.RequiredAny;

        /// <summary>Whether <paramref name="c"/> is a legal value for this slot.</summary>
        public bool Accepts(char c) => this.Kind switch
        {
            SlotKind.RequiredDigit or SlotKind.OptionalDigit => char.IsDigit(c),
            SlotKind.RequiredLetter or SlotKind.OptionalLetter => char.IsLetter(c),
            SlotKind.RequiredAlphanumeric or SlotKind.OptionalAlphanumeric => char.IsLetterOrDigit(c),
            SlotKind.RequiredAny or SlotKind.OptionalAny => true,
            _ => false,
        };
    }

    private MaskSlot[] _slots = [];

    /// <summary>The most recent rendering that passed the mask — the revert target for rejected edits.</summary>
    private string _lastValid = string.Empty;

    /// <summary>Raised after a candidate value was successfully applied through the mask and changed the text.</summary>
    public event EventHandler? MaskedTextChanged;

    /// <summary>Raised when a candidate value does not fit the mask and the transactional revert
    /// restores the last valid rendering, carrying the offending position and a reason.</summary>
    public event EventHandler<MaskInputRejectedEventArgs>? MaskInputRejected;

    /// <summary>
    /// The input mask. Setting it re-maps the current content into the new mask; content that no
    /// longer fits is discarded and the box falls back to the empty rendering (all prompts). An
    /// empty mask turns the box back into a plain <see cref="TextBox"/>.
    /// </summary>
    public string Mask
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            _slots = ParseMask(value);
            _lastValid = this.Render(string.Empty);
            this.Text = this.Text;
        }
    } = string.Empty;

    /// <summary>
    /// The character rendered in unfilled slots. Changing it re-renders the current content; slot
    /// values equal to the old prompt read as empty and therefore adopt the new prompt.
    /// </summary>
    public char PromptChar
    {
        get => field;
        set
        {
            if (field == value)
                return;

            var previous = field;
            field = value;
            if (_slots.Length == 0)
                return;

            var chars = this.Text.ToCharArray();
            for (var i = 0; i < chars.Length && i < _slots.Length; ++i)
                if (!_slots[i].IsLiteral && chars[i] == previous)
                    chars[i] = value;

            this.Text = new string(chars);
        }
    } = '_';

    /// <summary>Whether every required slot of the mask is filled (always <see langword="true"/> without a mask).</summary>
    public bool MaskCompleted
    {
        get
        {
            var text = this.Text;
            for (var i = 0; i < _slots.Length; ++i)
                if (_slots[i].IsRequired && (i >= text.Length || text[i] == this.PromptChar))
                    return false;

            return true;
        }
    }

    /// <summary>
    /// Returns only the characters the user actually entered — mask literals and prompt characters
    /// stripped. Without a mask this is simply <see cref="TextBox.Text"/>.
    /// </summary>
    public string GetTextWithoutPromptOrLiterals()
        => _slots.Length == 0 ? this.Text : this.ExtractRaw(this.Text);

    /// <summary>
    /// Routes every candidate value through the mask, after the base casing normalization. A value
    /// that maps cleanly becomes (and is remembered as) the new valid rendering; anything else is
    /// rejected by returning the last valid rendering, which the corrective push writes back to the
    /// widget.
    /// </summary>
    private protected override string NormalizeText(string value)
    {
        value = base.NormalizeText(value);
        if (_slots.Length == 0)
            return value;

        if (!this.TryApply(value, out var display, out var position, out var hint))
        {
            if (this.MaskInputRejected is not null)
                this.OnMaskInputRejected(new(position, hint));

            return _lastValid;
        }

        _lastValid = display;
        return display;
    }

    /// <summary>Raises <see cref="MaskInputRejected"/>.</summary>
    protected virtual void OnMaskInputRejected(MaskInputRejectedEventArgs e) => this.MaskInputRejected?.Invoke(this, e);

    /// <summary>Raises <see cref="Control.TextChanged"/> and, while a mask is active, <see cref="MaskedTextChanged"/>.</summary>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_slots.Length != 0)
            MaskedTextChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Maps <paramref name="input"/> into the mask. Literal positions render themselves and consume
    /// a matching input character when one is next; slot positions consume the next input character
    /// (the prompt char leaves the slot empty), rejecting the whole candidate when it does not fit
    /// or when input remains after the last mask position — the rejected mask position and reason
    /// come back through <paramref name="rejectPosition"/>/<paramref name="rejectHint"/>.
    /// </summary>
    private bool TryApply(string input, out string display, out int rejectPosition, out MaskedTextResultHint rejectHint)
    {
        var slots = _slots;
        var prompt = this.PromptChar;
        var result = new char[slots.Length];
        var next = 0;
        rejectPosition = -1;
        rejectHint = MaskedTextResultHint.InvalidInput;
        for (var i = 0; i < slots.Length; ++i)
        {
            var slot = slots[i];
            if (slot.IsLiteral)
            {
                if (next < input.Length && input[next] == slot.Literal)
                    ++next;

                result[i] = slot.Literal;
                continue;
            }

            if (next >= input.Length)
            {
                result[i] = prompt;
                continue;
            }

            var c = input[next];
            if (c == prompt)
            {
                result[i] = prompt;
                ++next;
                continue;
            }

            if (!slot.Accepts(c))
            {
                display = string.Empty;
                rejectPosition = i;
                return false;
            }

            result[i] = c;
            ++next;
        }

        if (next < input.Length)
        {
            display = string.Empty;
            rejectPosition = slots.Length;
            rejectHint = MaskedTextResultHint.UnavailableEditPosition;
            return false;
        }

        display = new string(result);
        return true;
    }

    /// <summary>Renders <paramref name="raw"/> through the mask, falling back to the empty rendering.</summary>
    private string Render(string raw)
        => this.TryApply(raw, out var display, out _, out _) ? display : this.RenderEmpty();

    /// <summary>The rendering of a mask with every slot empty: literals plus prompt characters.</summary>
    private string RenderEmpty()
    {
        var slots = _slots;
        var result = new char[slots.Length];
        for (var i = 0; i < slots.Length; ++i)
            result[i] = slots[i].IsLiteral ? slots[i].Literal : this.PromptChar;

        return new string(result);
    }

    /// <summary>Extracts the entered characters from a rendered value (slots that are neither literal nor prompt).</summary>
    private string ExtractRaw(string display)
    {
        var slots = _slots;
        var result = new char[display.Length];
        var count = 0;
        for (var i = 0; i < display.Length && i < slots.Length; ++i)
            if (!slots[i].IsLiteral && display[i] != this.PromptChar)
                result[count++] = display[i];

        return new string(result, 0, count);
    }

    /// <summary>Parses the mask language into slots; <c>\</c> escapes the following character into a literal.</summary>
    private static MaskSlot[] ParseMask(string mask)
    {
        if (mask.Length == 0)
            return [];

        var slots = new List<MaskSlot>(mask.Length);
        for (var i = 0; i < mask.Length; ++i)
        {
            var c = mask[i];
            if (c == '\\' && i + 1 < mask.Length)
            {
                slots.Add(new(SlotKind.Literal, mask[++i]));
                continue;
            }

            slots.Add(c switch
            {
                '0' => new MaskSlot(SlotKind.RequiredDigit, c),
                '9' => new MaskSlot(SlotKind.OptionalDigit, c),
                'L' => new MaskSlot(SlotKind.RequiredLetter, c),
                '?' => new MaskSlot(SlotKind.OptionalLetter, c),
                'A' => new MaskSlot(SlotKind.RequiredAlphanumeric, c),
                'a' => new MaskSlot(SlotKind.OptionalAlphanumeric, c),
                '&' => new MaskSlot(SlotKind.RequiredAny, c),
                'C' => new MaskSlot(SlotKind.OptionalAny, c),
                _ => new MaskSlot(SlotKind.Literal, c),
            });
        }

        return [.. slots];
    }
}
