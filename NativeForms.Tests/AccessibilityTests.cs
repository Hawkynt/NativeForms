using System.Collections.Generic;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §8: what a control tells assistive technology it is called and what it is.
///
/// A real platform widget answers for itself — a native check box tells a screen reader it is a check box
/// with its caption, unprompted. An owner-drawn control does not: however carefully it is painted, it is a
/// blank drawing surface to an accessibility client, so the toolkit has to say what the pixels mean. These
/// tests pin what gets said, and that saying nothing is never the answer for a control that has a caption.
/// </summary>
[TestFixture]
internal sealed class AccessibilityTests {
  private static HeadlessPeer Realize(Control control) {
    var form = new Form();
    form.Controls.Add(control);
    Application.Run(form, new HeadlessBackend());
    return (HeadlessPeer)control.Peer!;
  }

  // --- Defaults ---------------------------------------------------------------------------------

  [Test]
  public void A_controls_caption_is_its_accessible_name_without_being_asked()
      => Assert.That(Realize(new CheckBox { Text = "Enable logging" }).AccessibleName, Is.EqualTo("Enable logging"));

  [Test]
  public void A_control_with_no_caption_publishes_no_name_rather_than_an_empty_one()
      => Assert.That(Realize(new CheckBox()).AccessibleName, Is.Null);

  /// <summary>
  /// Every control whose role a screen reader cares about, constructed rather than reflected over —
  /// <c>Activator.CreateInstance(Type)</c> is banned in this repository, tests included.
  /// </summary>
  private static IEnumerable<TestCaseData> Roles() {
    yield return new TestCaseData(new CheckBox(), AccessibleRole.CheckButton).SetName("CheckBox");
    yield return new TestCaseData(new RadioButton(), AccessibleRole.RadioButton).SetName("RadioButton");
    yield return new TestCaseData(new ComboBox(), AccessibleRole.ComboBox).SetName("ComboBox");
    yield return new TestCaseData(new ListBox(), AccessibleRole.List).SetName("ListBox");
    yield return new TestCaseData(new CheckedListBox(), AccessibleRole.List).SetName("CheckedListBox");
    yield return new TestCaseData(new TreeView(), AccessibleRole.Tree).SetName("TreeView");
    yield return new TestCaseData(new DataGridView(), AccessibleRole.Table).SetName("DataGridView");
    yield return new TestCaseData(new TrackBar(), AccessibleRole.Slider).SetName("TrackBar");
    yield return new TestCaseData(new ProgressBar(), AccessibleRole.ProgressBar).SetName("ProgressBar");
    yield return new TestCaseData(new HScrollBar(), AccessibleRole.ScrollBar).SetName("HScrollBar");
    yield return new TestCaseData(new LinkLabel(), AccessibleRole.Link).SetName("LinkLabel");
    yield return new TestCaseData(new GroupBox(), AccessibleRole.Grouping).SetName("GroupBox");
    yield return new TestCaseData(new TabControl(), AccessibleRole.PageTabList).SetName("TabControl");
    yield return new TestCaseData(new PictureBox(), AccessibleRole.Graphic).SetName("PictureBox");
    yield return new TestCaseData(new Panel(), AccessibleRole.Pane).SetName("Panel");
    yield return new TestCaseData(new ListView(), AccessibleRole.List).SetName("ListView");
    yield return new TestCaseData(new ToolStrip(), AccessibleRole.ToolBar).SetName("ToolStrip");
    yield return new TestCaseData(new MenuStrip(), AccessibleRole.MenuBar).SetName("MenuStrip");
  }

  [TestCaseSource(nameof(Roles))]
  public void A_control_announces_what_it_is(Control control, AccessibleRole expected)
      => Assert.That(
          Realize(control).AccessibleRole,
          Is.EqualTo(expected),
          $"{control.GetType().Name} would be announced as an unnamed something");

  // --- Overrides --------------------------------------------------------------------------------

  [Test]
  public void An_explicit_name_wins_over_the_caption() {
    var button = new CheckBox { Text = "×" };
    button.AccessibleName = "Close";

    Assert.That(Realize(button).AccessibleName, Is.EqualTo("Close"), "a glyph is not a name");
  }

  [Test]
  public void An_explicit_role_wins_over_the_controls_own() {
    var panel = new Panel { AccessibleRole = AccessibleRole.Grouping };

    Assert.That(Realize(panel).AccessibleRole, Is.EqualTo(AccessibleRole.Grouping));
  }

  [Test]
  public void A_description_reaches_the_peer() {
    var box = new CheckBox { Text = "Verbose", AccessibleDescription = "Logs every request and response." };

    Assert.That(Realize(box).AccessibleDescription, Is.EqualTo("Logs every request and response."));
  }

  // --- Staying in step --------------------------------------------------------------------------

  [Test]
  public void Renaming_a_control_renames_it_for_a_screen_reader() {
    var box = new CheckBox { Text = "before" };
    var peer = Realize(box);

    box.Text = "after";

    Assert.That(peer.AccessibleName, Is.EqualTo("after"));
  }

  [Test]
  public void A_caption_change_does_not_disturb_an_explicit_name() {
    var box = new CheckBox { Text = "before", AccessibleName = "Pinned" };
    var peer = Realize(box);

    box.Text = "after";

    Assert.That(peer.AccessibleName, Is.EqualTo("Pinned"));
  }

  [Test]
  public void Setting_a_name_after_realization_reaches_the_peer() {
    var box = new CheckBox { Text = "caption" };
    var peer = Realize(box);

    box.AccessibleName = "Renamed";

    Assert.That(peer.AccessibleName, Is.EqualTo("Renamed"));
  }

  [Test]
  public void The_information_set_before_realization_survives_to_the_peer() {
    var box = new CheckBox {
      Text = "caption",
      AccessibleName = "Named early",
      AccessibleDescription = "Described early",
      AccessibleRole = AccessibleRole.PushButton,
    };

    var peer = Realize(box);

    Assert.Multiple(() => {
      Assert.That(peer.AccessibleName, Is.EqualTo("Named early"));
      Assert.That(peer.AccessibleDescription, Is.EqualTo("Described early"));
      Assert.That(peer.AccessibleRole, Is.EqualTo(AccessibleRole.PushButton));
    });
  }

  [Test]
  public void A_promoted_control_is_described_too() {
    var backend = new HeadlessBackend { OfferNativeCheckBox = true };
    var box = new CheckBox { Text = "Native" };
    var form = new Form();
    form.Controls.Add(box);
    Application.Run(form, backend);

    Assert.Multiple(() => {
      Assert.That(box.IsNativeWidget, Is.True);
      Assert.That(
          backend.LastCheckBox!.AccessibleName,
          Is.EqualTo("Native"),
          "the platform would name it anyway, but the toolkit must not contradict it by staying silent");
    });
  }
}
