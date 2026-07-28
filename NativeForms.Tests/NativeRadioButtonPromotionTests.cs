using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a <see cref="RadioButton"/> realizes onto a real platform radio when the backend offers one,
/// and behaves identically either way. Grouping stays in the core on both paths — the platform peers are
/// non-automatic precisely so the two never disagree about who owns the selection.
/// </summary>
[TestFixture]
internal sealed class NativeRadioButtonPromotionTests
{
    private static HeadlessBackend Promoting() => new() { OfferNativeRadioButton = true };

    private static Form Realize(IPlatformBackend backend, params RadioButton[] buttons)
    {
        var form = new Form();
        foreach (var button in buttons)
            form.Controls.Add(button);

        Application.Run(form, backend);
        return form;
    }

    private static RadioButton Button() => new() { Bounds = new(0, 0, 120, 20) };

    [TearDown]
    public void Restore() => Application.PreferNativeWidgets = true;

    [Test]
    public void A_backend_that_declines_leaves_the_button_owner_drawn()
    {
        var button = Button();

        Realize(new HeadlessBackend(), button);

        Assert.That(button.IsNativeWidget, Is.False);
    }

    [Test]
    public void A_backend_that_offers_a_widget_promotes_the_button()
    {
        var button = Button();

        Realize(Promoting(), button);

        Assert.That(button.IsNativeWidget, Is.True);
    }

    [Test]
    public void An_image_keeps_the_button_owner_drawn_even_on_a_willing_backend()
    {
        var button = Button();
        button.Image = new HeadlessImage(8, 8);

        Realize(Promoting(), button);

        Assert.That(button.IsNativeWidget, Is.False, "no platform radio draws our image beside the caption");
    }

    [Test]
    public void The_global_switch_turns_promotion_off()
    {
        Application.PreferNativeWidgets = false;
        var button = Button();

        Realize(Promoting(), button);

        Assert.That(button.IsNativeWidget, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Checking_one_button_clears_its_siblings_on_both_paths(bool native)
    {
        var first = Button();
        var second = Button();
        Realize(native ? Promoting() : new HeadlessBackend(), first, second);
        first.Checked = true;

        second.Checked = true;

        Assert.Multiple(() =>
        {
            Assert.That(second.Checked, Is.True);
            Assert.That(first.Checked, Is.False, "a radio group has exactly one selection");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Clearing_the_only_checked_button_is_permitted_on_both_paths(bool native)
    {
        var button = Button();
        Realize(native ? Promoting() : new HeadlessBackend(), button);
        button.Checked = true;

        button.Checked = false;

        Assert.That(button.Checked, Is.False, "the core allows an empty group, so the widget must too");
    }

    [Test]
    public void The_widget_is_told_about_a_programmatic_check()
    {
        var backend = Promoting();
        var button = Button();
        Realize(backend, button);

        button.Checked = true;

        Assert.That(backend.RadioButtons[0].GetChecked(), Is.True);
    }

    [Test]
    public void The_widget_is_told_when_a_sibling_takes_the_selection()
    {
        var backend = Promoting();
        var first = Button();
        var second = Button();
        Realize(backend, first, second);
        first.Checked = true;

        second.Checked = true;

        Assert.That(backend.RadioButtons[0].GetChecked(), Is.False, "the cleared sibling's widget must be cleared too");
    }

    [Test]
    public void A_click_on_the_widget_checks_the_button_and_clears_its_sibling()
    {
        var backend = Promoting();
        var first = Button();
        var second = Button();
        Realize(backend, first, second);
        first.Checked = true;

        backend.RadioButtons[1].RaiseUserSelect();

        Assert.Multiple(() =>
        {
            Assert.That(second.Checked, Is.True);
            Assert.That(first.Checked, Is.False);
        });
    }

    [Test]
    public void A_click_on_the_widget_raises_CheckedChanged_and_Click_exactly_once()
    {
        var backend = Promoting();
        var first = Button();
        var second = Button();
        Realize(backend, first, second);
        first.Checked = true;
        var checkedChanged = 0;
        var clicks = 0;
        second.CheckedChanged += (_, _) => ++checkedChanged;
        second.Click += (_, _) => ++clicks;

        backend.RadioButtons[1].RaiseUserSelect();

        Assert.Multiple(() =>
        {
            Assert.That(checkedChanged, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void Giving_a_promoted_button_an_image_drops_it_back_to_owner_drawn()
    {
        var button = Button();
        Realize(Promoting(), button);
        Assume.That(button.IsNativeWidget, Is.True);

        button.Image = new HeadlessImage(8, 8);

        Assert.That(button.IsNativeWidget, Is.False);
    }

    [Test]
    public void Taking_the_image_away_again_promotes_the_button_back()
    {
        var button = Button();
        button.Image = new HeadlessImage(8, 8);
        Realize(Promoting(), button);
        Assume.That(button.IsNativeWidget, Is.False);

        button.Image = null;

        Assert.That(button.IsNativeWidget, Is.True);
    }

    [Test]
    public void The_checked_state_survives_the_swap_to_owner_drawn()
    {
        var button = Button();
        Realize(Promoting(), button);
        button.Checked = true;

        button.Image = new HeadlessImage(8, 8);

        Assert.That(button.Checked, Is.True, "re-realizing is state transparent");
    }
}
