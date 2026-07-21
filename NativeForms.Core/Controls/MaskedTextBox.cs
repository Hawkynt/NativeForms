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
/// widget of every backend, maps every candidate value into the mask's slots and pushes the rendered
/// result — and the caret that belongs with it — back to the widget.
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
/// Peers report text changes as whole values, so the engine works in two modes. A programmatic
/// assignment to <see cref="TextBox.Text"/> is transactional over the whole string: a value that maps
/// cleanly into the mask becomes the new content, one that does not is rejected outright and the last
/// valid rendering stands. A <em>user</em> edit cannot be read that way — every keystroke makes the
/// widget's text longer than the mask — so the edit is reconstructed first: the previous rendering,
/// the candidate and the caret the widget left behind identify an insertion, a deletion or a replaced
/// selection, and only that edit is pushed through the mask. The corrected rendering and the caret
/// that belongs with it are written back to the widget, the same corrective push
/// <see cref="TextBox.CharacterCasing"/> uses.
/// </para>
/// <para>
/// Typing therefore fills slot by slot, skipping literals: a character lands in the first editable
/// slot at or after the caret, and a caret parked past the last slot (End on a partly filled mask)
/// falls back to the first still-empty slot rather than being refused. A prompt character in the
/// input always reads as an empty slot.
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

    /// <summary>
    /// Applies a <em>user</em> edit through the mask. The candidate the widget reports is never a
    /// mask rendering — an insertion makes it one character too long, a deletion one too short — so
    /// the edit itself is derived first (see <see cref="DeriveEdit"/>) and replayed slot by slot onto
    /// the last valid rendering. A character that no slot accepts, or one with nowhere left to go,
    /// rejects the whole edit and restores that rendering, exactly like a rejected assignment does.
    /// </summary>
    private protected override string NormalizeUserEdit(string value, int caret, out int correctedCaret)
    {
        if (_slots.Length == 0)
            return base.NormalizeUserEdit(value, caret, out correctedCaret);

        correctedCaret = -1;
        value = base.NormalizeText(value);

        var previous = _lastValid.Length == _slots.Length ? _lastValid : this.RenderEmpty();
        DeriveEdit(previous, value, caret, out var start, out var removedLength, out var insertedLength);
        if (!this.TryEdit(previous, value, start, removedLength, insertedLength, out var display, out var position, out var hint))
        {
            if (this.MaskInputRejected is not null)
                this.OnMaskInputRejected(new(position, hint));

            correctedCaret = Math.Min(start, _lastValid.Length);
            return _lastValid;
        }

        correctedCaret = position;
        _lastValid = display;
        return display;
    }

    /// <summary>
    /// Reconstructs the edit that turned <paramref name="previous"/> into <paramref name="candidate"/>
    /// as one replaced run: the characters <c>[start, start + removedLength)</c> of the previous value
    /// gave way to <c>[start, start + insertedLength)</c> of the candidate.
    /// </summary>
    /// <remarks>
    /// The common prefix may not run past <paramref name="caret"/> and the common suffix may not run
    /// back before it, which is what makes the reconstruction unambiguous: typing a character equal to
    /// the ones around it (a <c>5</c> into <c>555</c>) is a tie the caret — and only the caret —
    /// breaks. Everything outside the two common runs is the edit.
    /// </remarks>
    private static void DeriveEdit(
        string previous,
        string candidate,
        int caret,
        out int start,
        out int removedLength,
        out int insertedLength)
    {
        caret = Math.Clamp(caret, 0, candidate.Length);

        start = 0;
        while (start < caret && start < previous.Length && previous[start] == candidate[start])
            ++start;

        var suffix = 0;
        var maximum = Math.Min(previous.Length - start, candidate.Length - caret);
        while (suffix < maximum && previous[previous.Length - 1 - suffix] == candidate[candidate.Length - 1 - suffix])
            ++suffix;

        removedLength = previous.Length - suffix - start;
        insertedLength = candidate.Length - suffix - start;
    }

    /// <summary>
    /// Replays one derived edit onto a rendering: the removed run empties the slots it covers
    /// (literals survive, they are not the user's to delete), then each inserted character is written
    /// into the first editable slot at or after the running position. A caret that sat past the last
    /// slot has no slot to write into, so it falls back to the first still-empty one — that is what
    /// makes typing after End fill the mask from the front instead of being refused.
    /// </summary>
    private bool TryEdit(
        string previous,
        string candidate,
        int start,
        int removedLength,
        int insertedLength,
        out string display,
        out int position,
        out MaskedTextResultHint rejectHint)
    {
        var slots = _slots;
        var prompt = this.PromptChar;
        var result = previous.ToCharArray();
        rejectHint = MaskedTextResultHint.InvalidInput;
        display = string.Empty;

        for (var i = Math.Max(0, start); i < start + removedLength && i < result.Length; ++i)
            if (!slots[i].IsLiteral)
                result[i] = prompt;

        position = Math.Max(0, start);
        for (var i = 0; i < insertedLength; ++i)
        {
            var c = candidate[start + i];

            // A literal the user typed themselves is absorbed by the matching mask position rather
            // than stored in a slot — the same consumption a whole-value mapping does, which is what
            // lets "87-65" be pasted or retyped over "12-34".
            var scan = position;
            while (scan < slots.Length && slots[scan].IsLiteral && slots[scan].Literal != c)
                ++scan;

            if (scan < slots.Length && slots[scan].IsLiteral)
            {
                position = scan + 1;
                continue;
            }

            var slot = NextEditable(slots, position);
            if (slot < 0)
                slot = FirstEmptyEditable(slots, result, prompt);

            if (slot < 0)
            {
                position = slots.Length;
                rejectHint = MaskedTextResultHint.UnavailableEditPosition;
                return false;
            }

            if (c != prompt && !slots[slot].Accepts(c))
            {
                position = slot;
                return false;
            }

            result[slot] = c;
            position = slot + 1;
        }

        var next = NextEditable(slots, position);
        position = next < 0 ? result.Length : next;
        display = new string(result);
        return true;
    }

    /// <summary>The first non-literal slot at or after <paramref name="from"/>, or -1 when none is left.</summary>
    private static int NextEditable(MaskSlot[] slots, int from)
    {
        for (var i = Math.Max(0, from); i < slots.Length; ++i)
            if (!slots[i].IsLiteral)
                return i;

        return -1;
    }

    /// <summary>The first slot that is editable and still empty, or -1 when the mask is full.</summary>
    private static int FirstEmptyEditable(MaskSlot[] slots, char[] rendering, char prompt)
    {
        for (var i = 0; i < slots.Length; ++i)
            if (!slots[i].IsLiteral && rendering[i] == prompt)
                return i;

        return -1;
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
