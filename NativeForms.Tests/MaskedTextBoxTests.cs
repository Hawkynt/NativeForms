using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class MaskedTextBoxTests
{
    private static HeadlessBackend Realize(MaskedTextBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessTextBoxPeer PeerOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessTextBoxPeer>().Single();

    [Test]
    public void Setting_the_mask_renders_literals_and_prompts()
    {
        var box = new MaskedTextBox { Mask = "(000) 000-0000" };

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("(___) ___-____"));
            Assert.That(box.MaskCompleted, Is.False);
        });
    }

    [Test]
    public void Programmatic_text_maps_into_the_mask_slots()
    {
        var box = new MaskedTextBox { Mask = "(000) 000-0000" };

        box.Text = "1234567890";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("(123) 456-7890"));
            Assert.That(box.MaskCompleted, Is.True);
        });
    }

    [Test]
    public void Input_literals_matching_the_mask_are_consumed()
    {
        var box = new MaskedTextBox { Mask = "(000) 000-0000" };

        box.Text = "(123) 456-7890";

        Assert.That(box.Text, Is.EqualTo("(123) 456-7890"));
    }

    [Test]
    public void Partial_input_leaves_prompts_in_the_remaining_slots()
    {
        var box = new MaskedTextBox { Mask = "00-00" };

        box.Text = "12";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12-__"));
            Assert.That(box.MaskCompleted, Is.False);
        });
    }

    [Test]
    public void Optional_slots_do_not_block_MaskCompleted()
    {
        var box = new MaskedTextBox { Mask = "09" };

        box.Text = "1";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("1_"));
            Assert.That(box.MaskCompleted, Is.True);
        });
    }

    [Test]
    public void Escaped_mask_characters_are_literals()
    {
        var box = new MaskedTextBox { Mask = @"\09" };

        box.Text = "7";

        Assert.That(box.Text, Is.EqualTo("07"));
    }

    [Test]
    public void Invalid_programmatic_text_is_rejected_and_keeps_the_last_valid_value()
    {
        var box = new MaskedTextBox { Mask = "LL" };
        box.Text = "ab";

        box.Text = "12";

        Assert.That(box.Text, Is.EqualTo("ab"));
    }

    [Test]
    public void Invalid_user_edit_reverts_the_widget_to_the_last_valid_value()
    {
        var box = new MaskedTextBox { Mask = "00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateUserInput("12");

        var changes = 0;
        box.TextChanged += (_, _) => ++changes;
        peer.SimulateUserInput("1x");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12"));
            Assert.That(peer.Text, Is.EqualTo("12"));
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void Valid_user_edit_applies_and_raises_MaskedTextChanged()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var raised = 0;
        box.MaskedTextChanged += (_, _) => ++raised;

        PeerOf(backend).SimulateUserInput("1234");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12-34"));
            Assert.That(PeerOf(backend).Text, Is.EqualTo("12-34"));
            Assert.That(raised, Is.EqualTo(1));
        });
    }

    // --- Typing: every keystroke arrives as a whole value one character too long, so the edit has
    // to be reconstructed from the previous rendering, the candidate and the caret before the mask
    // can be applied to it. Before that reconstruction existed the control could not be typed into
    // at all: the candidate never fit the mask and every character was rejected.

    [Test]
    public void Typing_digits_fills_the_slots_and_skips_the_literals()
    {
        var box = new MaskedTextBox { Mask = "(000) 000-0000" };
        var backend = Realize(box);

        PeerOf(backend).SimulateTyping("5551234567");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("(555) 123-4567"));
            Assert.That(box.MaskCompleted, Is.True);
            Assert.That(PeerOf(backend).Text, Is.EqualTo("(555) 123-4567"));
        });
    }

    [Test]
    public void Typing_with_the_caret_parked_past_the_last_slot_fills_from_the_first_empty_one()
    {
        // What End leaves behind: the caret sits after the last mask position, where no slot can
        // take the character. Falling back to the first still-empty slot is what makes the box
        // typable at all from there, instead of refusing every key.
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SetSelection(5, 0);

        peer.SimulateUserInput("__-__7", 5);

        Assert.That(box.Text, Is.EqualTo("7_-__"));
    }

    [Test]
    public void Typing_over_a_filled_slot_replaces_it_and_moves_on()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateTyping("1234");

        peer.SetSelection(0, 0);
        peer.SimulateTyping("9");

        Assert.That(box.Text, Is.EqualTo("92-34"));
    }

    [Test]
    public void A_typed_literal_is_absorbed_by_the_matching_mask_position()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);

        PeerOf(backend).SimulateTyping("12-34");

        Assert.That(box.Text, Is.EqualTo("12-34"));
    }

    [Test]
    public void Deleting_a_character_empties_its_slot_and_leaves_the_literals()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateTyping("1234");

        // Backspace at the end: the widget reports the shorter value with the caret where the
        // character used to be.
        peer.SimulateUserInput("12-3", 4);

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12-3_"));
            Assert.That(box.MaskCompleted, Is.False);
        });
    }

    [Test]
    public void Replacing_a_selection_clears_the_slots_it_covered_and_applies_the_replacement()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateTyping("1234");

        // The whole content selected and one digit typed over it.
        peer.SimulateUserInput("9", 0);

        Assert.That(box.Text, Is.EqualTo("9_-__"));
    }

    [Test]
    public void A_typed_character_the_slot_refuses_is_rejected_without_a_TextChanged()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateTyping("12");

        var changes = 0;
        MaskInputRejectedEventArgs? rejected = null;
        box.TextChanged += (_, _) => ++changes;
        box.MaskInputRejected += (_, e) => rejected = e;
        peer.SimulateTyping("x");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12-__"));
            Assert.That(peer.Text, Is.EqualTo("12-__"), "the widget was reverted too");
            Assert.That(changes, Is.Zero);
            Assert.That(rejected, Is.Not.Null);
            Assert.That(rejected!.Position, Is.EqualTo(3), "the slot the character did not fit");
        });
    }

    [Test]
    public void Typing_into_a_full_mask_from_past_its_end_reports_no_room_left()
    {
        var box = new MaskedTextBox { Mask = "00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);
        peer.SimulateTyping("12");

        MaskInputRejectedEventArgs? rejected = null;
        box.MaskInputRejected += (_, e) => rejected = e;
        peer.SetSelection(2, 0);
        peer.SimulateUserInput("123", 2);

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12"));
            Assert.That(rejected, Is.Not.Null);
            Assert.That(rejected!.RejectionHint, Is.EqualTo(MaskedTextResultHint.UnavailableEditPosition));
        });
    }

    [Test]
    public void The_caret_pushed_back_after_a_keystroke_lands_on_the_next_editable_slot()
    {
        // The literals are not the user's to stand on: after the last digit of a group the caret
        // has to jump the separator, or the next keystroke would resolve to the same slot again.
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        var peer = PeerOf(backend);

        peer.SimulateTyping("12");

        Assert.That(peer.SelectionStart, Is.EqualTo(3));
    }

    [Test]
    public void CharacterCasing_is_applied_before_the_mask()
    {
        var box = new MaskedTextBox { Mask = "LL", CharacterCasing = CharacterCasing.Upper };

        box.Text = "ab";

        Assert.That(box.Text, Is.EqualTo("AB"));
    }

    [Test]
    public void Prompt_characters_in_the_input_leave_slots_empty()
    {
        var box = new MaskedTextBox { Mask = "00-00" };

        box.Text = "12-__";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("12-__"));
            Assert.That(box.MaskCompleted, Is.False);
        });
    }

    [Test]
    public void PromptChar_change_rerenders_the_empty_slots()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        box.Text = "12";

        box.PromptChar = '#';

        Assert.That(box.Text, Is.EqualTo("12-##"));
    }

    [Test]
    public void Mask_change_remaps_the_existing_content()
    {
        var box = new MaskedTextBox { Mask = "0000" };
        box.Text = "1234";

        box.Mask = "00-00";

        Assert.That(box.Text, Is.EqualTo("12-34"));
    }

    [Test]
    public void Mask_change_discards_content_that_no_longer_fits()
    {
        var box = new MaskedTextBox { Mask = "LL" };
        box.Text = "ab";

        box.Mask = "00";

        Assert.That(box.Text, Is.EqualTo("__"));
    }

    [Test]
    public void GetTextWithoutPromptOrLiterals_strips_literals_and_prompts()
    {
        var box = new MaskedTextBox { Mask = "(000) 000-0000" };
        box.Text = "12345";

        Assert.That(box.GetTextWithoutPromptOrLiterals(), Is.EqualTo("12345"));
    }

    [Test]
    public void Empty_mask_behaves_like_a_plain_TextBox()
    {
        var box = new MaskedTextBox();

        box.Text = "anything at all";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("anything at all"));
            Assert.That(box.MaskCompleted, Is.True);
            Assert.That(box.GetTextWithoutPromptOrLiterals(), Is.EqualTo("anything at all"));
        });
    }

    [Test]
    public void Excess_input_beyond_the_mask_is_rejected()
    {
        var box = new MaskedTextBox { Mask = "000" };
        box.Text = "123";

        box.Text = "1234";

        Assert.That(box.Text, Is.EqualTo("123"));
    }

    [Test]
    public void MaskInputRejected_reports_position_and_hint_for_a_misfit_character()
    {
        var box = new MaskedTextBox { Mask = "00-00" };
        var backend = Realize(box);
        MaskInputRejectedEventArgs? rejected = null;
        box.MaskInputRejected += (_, e) => rejected = e;

        PeerOf(backend).SimulateUserInput("1a");

        Assert.Multiple(() =>
        {
            Assert.That(rejected, Is.Not.Null);
            Assert.That(rejected!.Position, Is.EqualTo(1), "the slot the character did not fit");
            Assert.That(rejected.RejectionHint, Is.EqualTo(MaskedTextResultHint.InvalidInput));
            Assert.That(box.Text, Is.EqualTo("__-__"), "the transactional revert restored the last valid rendering");
            Assert.That(PeerOf(backend).Text, Is.EqualTo("__-__"), "and pushed it back to the widget");
        });
    }

    [Test]
    public void MaskInputRejected_reports_the_end_position_for_overflowing_input()
    {
        var box = new MaskedTextBox { Mask = "00" };
        MaskInputRejectedEventArgs? rejected = null;
        box.MaskInputRejected += (_, e) => rejected = e;

        box.Text = "123";

        Assert.Multiple(() =>
        {
            Assert.That(rejected!.Position, Is.EqualTo(2));
            Assert.That(rejected.RejectionHint, Is.EqualTo(MaskedTextResultHint.UnavailableEditPosition));
            Assert.That(box.Text, Is.EqualTo("__"));
        });

        rejected = null;
        box.Text = "42"; // a fitting value raises nothing
        Assert.Multiple(() =>
        {
            Assert.That(rejected, Is.Null);
            Assert.That(box.Text, Is.EqualTo("42"));
        });
    }
}
