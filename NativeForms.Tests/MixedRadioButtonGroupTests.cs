using System.Collections.Generic;
using System.Linq;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a radio group whose members do not all take the same rendering path.
///
/// The gate is per control, so one button carrying an <see cref="RadioButton.Image"/> stays owner-drawn
/// while its siblings realize onto real widgets — and that mixture is the case most likely to break,
/// because each half reports a click through a different pipeline. Grouping is deliberately kept in the
/// core for exactly this reason (the peers are non-automatic), and these tests are what pin it: whichever
/// member takes the selection, every other member must lose it, and a promoted loser's <em>widget</em>
/// must be cleared too, not just its managed flag.
/// </summary>
[TestFixture]
internal sealed class MixedRadioButtonGroupTests {
  private static HeadlessBackend Promoting() => new() { OfferNativeRadioButton = true };

  /// <summary>A button that clears its gate, so a willing backend promotes it.</summary>
  private static RadioButton Native(string text) => new() { Bounds = new(0, 0, 120, 20), Text = text };

  /// <summary>A button carrying an image, which no platform radio can draw — so it stays painted.</summary>
  private static RadioButton Drawn(string text)
      => new() { Bounds = new(0, 0, 120, 20), Text = text, Image = new HeadlessImage(8, 8) };

  private static Form Realize(IPlatformBackend backend, params Control[] controls) {
    var form = new Form();
    form.Controls.AddRange(controls);
    Application.Run(form, backend);
    return form;
  }

  /// <summary>The widget behind a promoted button, found by its caption so a mixed group's indices do not matter.</summary>
  private static HeadlessRadioButtonPeer Peer(HeadlessBackend backend, string text)
      => backend.RadioButtons.Single(peer => peer.Text == text);

  [TearDown]
  public void Restore() => Application.PreferNativeWidgets = true;

  [Test]
  public void The_gate_really_does_split_the_group() {
    var native = Native("native");
    var drawn = Drawn("drawn");

    Realize(Promoting(), native, drawn);

    Assert.Multiple(() => {
      Assert.That(native.IsNativeWidget, Is.True);
      Assert.That(drawn.IsNativeWidget, Is.False, "the premise of every test below");
    });
  }

  // --- A programmatic selection crosses the split, both ways -------------------------------------

  [Test]
  public void Checking_the_promoted_button_clears_the_painted_one() {
    var native = Native("native");
    var drawn = Drawn("drawn");
    Realize(Promoting(), native, drawn);
    drawn.Checked = true;

    native.Checked = true;

    Assert.Multiple(() => {
      Assert.That(native.Checked, Is.True);
      Assert.That(drawn.Checked, Is.False);
    });
  }

  [Test]
  public void Checking_the_painted_button_clears_the_promoted_one_and_its_widget() {
    var backend = Promoting();
    var native = Native("native");
    var drawn = Drawn("drawn");
    Realize(backend, native, drawn);
    native.Checked = true;
    Assume.That(Peer(backend, "native").GetChecked(), Is.True);

    drawn.Checked = true;

    Assert.Multiple(() => {
      Assert.That(drawn.Checked, Is.True);
      Assert.That(native.Checked, Is.False);
      Assert.That(
          Peer(backend, "native").GetChecked(),
          Is.False,
          "clearing only the managed flag would leave the widget drawn as the selection");
    });
  }

  // --- A user gesture crosses the split, both ways -----------------------------------------------

  [Test]
  public void A_click_on_the_widget_clears_the_painted_sibling() {
    var backend = Promoting();
    var native = Native("native");
    var drawn = Drawn("drawn");
    Realize(backend, native, drawn);
    drawn.Checked = true;

    Peer(backend, "native").RaiseUserSelect();

    Assert.Multiple(() => {
      Assert.That(native.Checked, Is.True);
      Assert.That(drawn.Checked, Is.False);
    });
  }

  [Test]
  public void A_click_on_the_painted_button_clears_the_promoted_siblings_widget() {
    var backend = Promoting();
    var native = Native("native");
    var drawn = Drawn("drawn");
    Realize(backend, native, drawn);
    native.Checked = true;

    drawn.PerformClick();

    Assert.Multiple(() => {
      Assert.That(drawn.Checked, Is.True);
      Assert.That(native.Checked, Is.False);
      Assert.That(Peer(backend, "native").GetChecked(), Is.False);
    });
  }

  [Test]
  public void Each_affected_button_raises_CheckedChanged_exactly_once_per_gesture() {
    var backend = Promoting();
    var native = Native("native");
    var drawn = Drawn("drawn");
    Realize(backend, native, drawn);
    drawn.Checked = true;
    var nativeChanges = 0;
    var drawnChanges = 0;
    native.CheckedChanged += (_, _) => ++nativeChanges;
    drawn.CheckedChanged += (_, _) => ++drawnChanges;

    Peer(backend, "native").RaiseUserSelect();

    Assert.Multiple(() => {
      Assert.That(nativeChanges, Is.EqualTo(1), "the winner reports once");
      Assert.That(drawnChanges, Is.EqualTo(1), "and so does the loser");
    });
  }

  // --- More than two, and in any order ------------------------------------------------------------

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void Whichever_member_of_a_four_way_mixed_group_wins_every_other_loses(int winner) {
    var backend = Promoting();
    var buttons = new[] { Native("a"), Drawn("b"), Native("c"), Drawn("d") };
    Realize(backend, buttons);
    buttons[0].Checked = true;

    buttons[winner].Checked = true;

    Assert.Multiple(() => {
      for (var i = 0; i < buttons.Length; ++i)
        Assert.That(buttons[i].Checked, Is.EqualTo(i == winner), $"button {i}");

      foreach (var peer in backend.RadioButtons)
        Assert.That(
            peer.GetChecked(),
            Is.EqualTo(peer.Text == buttons[winner].Text),
            $"the widget behind \"{peer.Text}\"");
    });
  }

  [Test]
  public void A_mixed_group_still_only_reaches_its_own_parent() {
    var backend = Promoting();
    var left = new GroupBox { Bounds = new(0, 0, 200, 100) };
    var leftNative = Native("left-native");
    var leftDrawn = Drawn("left-drawn");
    left.Controls.AddRange(leftNative, leftDrawn);

    var right = new GroupBox { Bounds = new(0, 100, 200, 100) };
    var rightNative = Native("right-native");
    var rightDrawn = Drawn("right-drawn");
    right.Controls.AddRange(rightNative, rightDrawn);

    Realize(backend, left, right);
    leftNative.Checked = true;
    rightDrawn.Checked = true;

    rightNative.Checked = true;

    Assert.Multiple(() => {
      Assert.That(rightNative.Checked, Is.True);
      Assert.That(rightDrawn.Checked, Is.False, "its own group loses the selection");
      Assert.That(leftNative.Checked, Is.True, "the other group is untouched");
      Assert.That(Peer(backend, "left-native").GetChecked(), Is.True);
    });
  }

  // --- The split can move while the group is live -------------------------------------------------

  [Test]
  public void A_button_that_falls_back_to_the_painter_keeps_the_selection() {
    var backend = Promoting();
    var native = Native("native");
    var other = Native("other");
    Realize(backend, native, other);
    native.Checked = true;

    native.Image = new HeadlessImage(8, 8);

    Assert.Multiple(() => {
      Assert.That(native.IsNativeWidget, Is.False, "it left the gate");
      Assert.That(native.Checked, Is.True, "re-realizing is state transparent");
      Assert.That(other.Checked, Is.False, "and it did not hand the selection over");
    });
  }

  [Test]
  public void A_button_that_falls_back_to_the_painter_is_still_cleared_by_a_promoted_sibling() {
    var backend = Promoting();
    var native = Native("native");
    var other = Native("other");
    Realize(backend, native, other);
    native.Checked = true;
    native.Image = new HeadlessImage(8, 8);
    Assume.That(native.IsNativeWidget, Is.False);

    Peer(backend, "other").RaiseUserSelect();

    Assert.Multiple(() => {
      Assert.That(other.Checked, Is.True);
      Assert.That(native.Checked, Is.False, "grouping must not care which path a member is on");
    });
  }

  [Test]
  public void A_button_promoted_mid_use_takes_its_checked_state_into_the_new_widget() {
    var backend = Promoting();
    var drawn = Drawn("drawn");
    var other = Native("other");
    Realize(backend, drawn, other);
    drawn.Checked = true;

    drawn.Image = null;

    Assert.Multiple(() => {
      Assert.That(drawn.IsNativeWidget, Is.True, "it entered the gate");
      Assert.That(drawn.Checked, Is.True);
      Assert.That(
          Peer(backend, "drawn").GetChecked(),
          Is.True,
          "the fresh widget has to be told it is the selection");
      Assert.That(other.Checked, Is.False);
    });
  }

  [Test]
  public void Re_realizing_an_unchecked_focused_button_does_not_steal_the_selection() {
    var backend = Promoting();
    var winner = Native("winner");
    var moving = Native("moving");
    Realize(backend, winner, moving);
    winner.Checked = true;

    // Focus arriving normally checks a radio, so this is the one way to hold focus without the
    // selection: take it while the selection is elsewhere, then cross the gate.
    moving.Focus();
    moving.Checked = false;
    winner.Checked = true;
    Assume.That(moving.Checked, Is.False);

    moving.Image = new HeadlessImage(8, 8);

    Assert.Multiple(() => {
      Assert.That(moving.IsNativeWidget, Is.False);
      Assert.That(
          winner.Checked,
          Is.True,
          "the focus the swap re-establishes must not be read as the user selecting the button");
      Assert.That(moving.Checked, Is.False);
    });
  }

  // --- The mixture must not depend on the group's realization order --------------------------------

  [Test]
  public void The_painted_button_realizing_first_changes_nothing() {
    var backend = Promoting();
    var drawn = Drawn("drawn");
    var native = Native("native");
    Realize(backend, drawn, native);

    drawn.Checked = true;
    native.Checked = true;

    Assert.Multiple(() => {
      Assert.That(native.Checked, Is.True);
      Assert.That(drawn.Checked, Is.False);
      Assert.That(Peer(backend, "native").GetChecked(), Is.True);
    });
  }

  [Test]
  public void A_group_the_app_pinned_to_the_painter_still_groups_with_a_promoted_sibling() {
    var backend = Promoting();
    var pinned = Native("pinned");
    pinned.UseNativeWidget = false;
    var promoted = Native("promoted");
    Realize(backend, pinned, promoted);
    promoted.Checked = true;

    pinned.Checked = true;

    Assert.Multiple(() => {
      Assert.That(pinned.IsNativeWidget, Is.False, "the per-control override held");
      Assert.That(pinned.Checked, Is.True);
      Assert.That(promoted.Checked, Is.False);
      Assert.That(Peer(backend, "promoted").GetChecked(), Is.False);
    });
  }
}
