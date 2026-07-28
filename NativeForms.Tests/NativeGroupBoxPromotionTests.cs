using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a <see cref="GroupBox"/> realizes onto a real platform frame when its caption needs no icon.
/// It is the one container in the set, so the assertion that matters most is that the children still
/// arrive — at the peer, with the bounds the application gave them, which is what makes the two rendering
/// paths lay out identically.
/// </summary>
[TestFixture]
internal sealed class NativeGroupBoxPromotionTests
{
    private static HeadlessBackend Promoting() => new() { OfferNativeGroupBox = true };

    private static GroupBox Realize(GroupBox group, IPlatformBackend backend)
    {
        var form = new Form();
        form.Controls.Add(group);
        Application.Run(form, backend);
        return group;
    }

    private static GroupBox Group() => new() { Bounds = new(0, 0, 200, 120), Text = "Size" };

    [TearDown]
    public void Restore() => Application.PreferNativeWidgets = true;

    [Test]
    public void A_backend_that_declines_leaves_the_frame_owner_drawn()
        => Assert.That(Realize(Group(), new HeadlessBackend()).IsNativeWidget, Is.False);

    [Test]
    public void A_backend_that_offers_a_widget_promotes_the_frame()
        => Assert.That(Realize(Group(), Promoting()).IsNativeWidget, Is.True);

    [Test]
    public void A_caption_icon_keeps_the_frame_owner_drawn()
    {
        var group = Group();
        group.Image = new HeadlessImage(8, 8);

        Realize(group, Promoting());

        Assert.That(group.IsNativeWidget, Is.False, "a stock frame's caption is text");
    }

    [Test]
    public void The_global_switch_turns_promotion_off()
    {
        Application.PreferNativeWidgets = false;

        Assert.That(Realize(Group(), Promoting()).IsNativeWidget, Is.False);
    }

    [Test]
    public void The_caption_reaches_the_widget()
    {
        var backend = Promoting();

        Realize(Group(), backend);

        Assert.That(backend.LastGroupBox!.Text, Is.EqualTo("Size"));
    }

    // --- It is a container ------------------------------------------------------------------------

    [Test]
    public void The_children_are_parented_into_the_promoted_frame()
    {
        var backend = Promoting();
        var group = Group();
        group.Controls.Add(new Button { Bounds = new(12, 34, 100, 24), Text = "Inside" });

        Realize(group, backend);

        Assert.That(backend.LastGroupBox!.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_child_added_after_realization_still_reaches_the_frame()
    {
        var backend = Promoting();
        var group = Realize(Group(), backend);

        group.Controls.Add(new Button { Bounds = new(12, 34, 100, 24), Text = "Later" });

        Assert.That(backend.LastGroupBox!.Children, Has.Count.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void A_child_keeps_the_bounds_it_was_given_on_both_paths(bool native)
    {
        var group = Group();
        var button = new Button { Bounds = new(12, 34, 100, 24), Text = "Inside" };
        group.Controls.Add(button);

        Realize(group, native ? Promoting() : new HeadlessBackend());

        Assert.That(
            button.Bounds,
            Is.EqualTo(new System.Drawing.Rectangle(12, 34, 100, 24)),
            "the frame is drawn behind the children rather than around them, so nothing is offset");
    }

    // --- Crossing the gate mid-use ----------------------------------------------------------------

    [Test]
    public void Giving_a_promoted_frame_a_caption_icon_drops_it_back_to_owner_drawn()
    {
        var group = Realize(Group(), Promoting());
        Assume.That(group.IsNativeWidget, Is.True);

        group.Image = new HeadlessImage(8, 8);

        Assert.That(group.IsNativeWidget, Is.False);
    }

    [Test]
    public void The_children_survive_the_swap_to_owner_drawn()
    {
        var group = Group();
        group.Controls.Add(new Button { Bounds = new(12, 34, 100, 24), Text = "Inside" });
        Realize(group, Promoting());

        group.Image = new HeadlessImage(8, 8);

        Assert.Multiple(() =>
        {
            Assert.That(group.IsNativeWidget, Is.False);
            Assert.That(group.Controls, Has.Count.EqualTo(1), "re-realizing is state transparent");
        });
    }
}
