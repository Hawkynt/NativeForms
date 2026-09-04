using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a <see cref="LinkLabel"/> realizes onto a real platform hyperlink when the backend offers
/// one. Nothing gates it — everything the control models is expressible — so the interesting assertions
/// are that the visited flag reaches the widget and that an activation raises the same pair of events
/// the owner-drawn mouse path does.
/// </summary>
[TestFixture]
internal sealed class NativeLinkLabelPromotionTests {
  private static HeadlessBackend Promoting() => new() { OfferNativeLinkLabel = true };

  private static LinkLabel Realize(LinkLabel link, IPlatformBackend backend) {
    var form = new Form();
    form.Controls.Add(link);
    Application.Run(form, backend);
    return link;
  }

  private static LinkLabel Link() => new() { Bounds = new(0, 0, 120, 20), Text = "Open the docs" };

  [TearDown]
  public void Restore() => Application.PreferNativeWidgets = true;

  [Test]
  public void A_backend_that_declines_leaves_the_link_owner_drawn()
      => Assert.That(Realize(Link(), new HeadlessBackend()).IsNativeWidget, Is.False);

  [Test]
  public void A_backend_that_offers_a_widget_promotes_the_link()
      => Assert.That(Realize(Link(), Promoting()).IsNativeWidget, Is.True);

  [Test]
  public void The_global_switch_turns_promotion_off() {
    Application.PreferNativeWidgets = false;

    Assert.That(Realize(Link(), Promoting()).IsNativeWidget, Is.False);
  }

  [Test]
  public void A_per_control_override_beats_the_global_switch() {
    Application.PreferNativeWidgets = false;
    var link = Link();
    link.UseNativeWidget = true;

    Realize(link, Promoting());

    Assert.That(link.IsNativeWidget, Is.True);
  }

  [Test]
  public void The_visited_flag_set_before_realization_reaches_the_widget() {
    var backend = Promoting();
    var link = Link();
    link.LinkVisited = true;

    Realize(link, backend);

    Assert.That(backend.LastLinkLabel!.Visited, Is.True);
  }

  [Test]
  public void The_visited_flag_set_after_realization_reaches_the_widget() {
    var backend = Promoting();
    var link = Realize(Link(), backend);

    link.LinkVisited = true;

    Assert.That(backend.LastLinkLabel!.Visited, Is.True);
  }

  [Test]
  public void The_legacy_Visited_spelling_still_drives_the_widget() {
    var backend = Promoting();
    var link = Realize(Link(), backend);

    link.Visited = true;

    Assert.That(backend.LastLinkLabel!.Visited, Is.True);
  }

  [Test]
  public void An_activation_raises_Click_and_LinkClicked_once_each() {
    var backend = Promoting();
    var link = Realize(Link(), backend);
    var clicks = 0;
    var linkClicks = 0;
    link.Click += (_, _) => ++clicks;
    link.LinkClicked += (_, _) => ++linkClicks;

    backend.LastLinkLabel!.RaiseUserActivate();

    Assert.Multiple(() => {
      Assert.That(clicks, Is.EqualTo(1), "the owner-drawn mouse path raises both, so the widget must too");
      Assert.That(linkClicks, Is.EqualTo(1));
    });
  }

  [Test]
  public void Text_assigned_after_realization_reaches_the_widget() {
    var backend = Promoting();
    var link = Realize(Link(), backend);

    link.Text = "Somewhere else";

    Assert.That(backend.LastLinkLabel!.Text, Is.EqualTo("Somewhere else"));
  }
}
