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
