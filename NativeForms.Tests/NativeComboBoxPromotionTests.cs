using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a <see cref="ComboBox"/> realizes onto a real platform drop-down list when nothing in its
/// state needs the owner-drawn painter. The gate is the interesting part here — a stock combo shows a
/// flat list of strings and nothing else — along with the drop-down being state transparent: the widget
/// owns the list, so <see cref="ComboBox.OpenDropDown"/> must drive it and the flag must follow.
/// </summary>
[TestFixture]
internal sealed class NativeComboBoxPromotionTests {
  private static HeadlessBackend Promoting() => new() { OfferNativeComboBox = true };

  private static ComboBox Realize(ComboBox combo, IPlatformBackend backend) {
    var form = new Form();
    form.Controls.Add(combo);
    Application.Run(form, backend);
    return combo;
  }

  private static ComboBox Combo() {
    var combo = new ComboBox { Bounds = new(0, 0, 200, 26) };
    combo.Items.AddRange(["alpha", "beta", "gamma"]);
    return combo;
  }

  [TearDown]
  public void Restore() => Application.PreferNativeWidgets = true;

  [Test]
  public void A_backend_that_declines_leaves_the_combo_owner_drawn()
      => Assert.That(Realize(Combo(), new HeadlessBackend()).IsNativeWidget, Is.False);

  [Test]
  public void A_backend_that_offers_a_widget_promotes_the_combo()
      => Assert.That(Realize(Combo(), Promoting()).IsNativeWidget, Is.True);

  [Test]
  public void The_editable_style_keeps_the_combo_owner_drawn() {
    var combo = Combo();
    combo.DropDownStyle = ComboBoxStyle.DropDown;

    Realize(combo, Promoting());

    Assert.That(combo.IsNativeWidget, Is.False, "the editable style hosts a real TextBox child");
  }

  [Test]
  public void A_placeholder_keeps_the_combo_owner_drawn() {
    var combo = Combo();
    combo.PlaceholderText = "Pick one…";

    Realize(combo, Promoting());

    Assert.That(combo.IsNativeWidget, Is.False);
  }

  [Test]
  public void An_image_selector_keeps_the_combo_owner_drawn() {
    var combo = Combo();
    combo.ImageSelector = static _ => null;

    Realize(combo, Promoting());

    Assert.That(combo.IsNativeWidget, Is.False, "a stock combo shows no per-item icons");
  }

  [Test]
  public void The_global_switch_turns_promotion_off() {
    Application.PreferNativeWidgets = false;

    Assert.That(Realize(Combo(), Promoting()).IsNativeWidget, Is.False);
  }

  // --- The item list is mirrored ----------------------------------------------------------------

  [Test]
  public void The_items_present_at_realization_reach_the_widget() {
    var backend = Promoting();

    Realize(Combo(), backend);

    Assert.That(backend.LastComboBox!.Items, Is.EqualTo(new[] { "alpha", "beta", "gamma" }));
  }

  [Test]
  public void The_widget_sees_the_display_text_rather_than_the_item() {
    var backend = Promoting();
    var combo = new ComboBox { Bounds = new(0, 0, 200, 26), DisplaySelector = static o => $"#{o}" };
    combo.Items.AddRange([1, 2]);

    Realize(combo, backend);

    Assert.That(backend.LastComboBox!.Items, Is.EqualTo(new[] { "#1", "#2" }));
  }

  [Test]
  public void Adding_an_item_after_realization_reaches_the_widget() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);

    combo.Items.Add("delta");

    Assert.That(backend.LastComboBox!.Items, Is.EqualTo(new[] { "alpha", "beta", "gamma", "delta" }));
  }

  [Test]
  public void Clearing_the_items_reaches_the_widget() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);

    combo.Items.Clear();

    Assert.That(backend.LastComboBox!.Items, Is.Empty);
  }

  // --- Selection --------------------------------------------------------------------------------

  [Test]
  public void A_programmatic_selection_reaches_the_widget() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);

    combo.SelectedIndex = 2;

    Assert.That(backend.LastComboBox!.GetSelectedIndex(), Is.EqualTo(2));
  }

  [Test]
  public void A_selection_made_in_the_widget_reaches_the_control_once() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);
    var changes = 0;
    combo.SelectedIndexChanged += (_, _) => ++changes;

    backend.LastComboBox!.RaiseUserSelect(1);

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(1));
      Assert.That(combo.SelectedItem, Is.EqualTo("beta"));
      Assert.That(changes, Is.EqualTo(1));
    });
  }

  // --- The drop-down ----------------------------------------------------------------------------

  [Test]
  public void OpenDropDown_opens_the_widgets_own_list() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);

    combo.OpenDropDown();

    Assert.Multiple(() => {
      Assert.That(backend.LastComboBox!.DroppedDown, Is.True);
      Assert.That(combo.DroppedDown, Is.True, "the flag follows what the widget reported");
    });
  }

  [Test]
  public void CloseDropDown_closes_the_widgets_own_list() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);
    combo.OpenDropDown();

    combo.CloseDropDown();

    Assert.Multiple(() => {
      Assert.That(backend.LastComboBox!.DroppedDown, Is.False);
      Assert.That(combo.DroppedDown, Is.False);
    });
  }

  [Test]
  public void The_widget_opening_its_list_raises_DropDown_once() {
    var backend = Promoting();
    var combo = Realize(Combo(), backend);
    var opens = 0;
    var closes = 0;
    combo.DropDown += (_, _) => ++opens;
    combo.DropDownClosed += (_, _) => ++closes;

    backend.LastComboBox!.SetDroppedDown(true);
    backend.LastComboBox!.SetDroppedDown(false);

    Assert.Multiple(() => {
      Assert.That(opens, Is.EqualTo(1));
      Assert.That(closes, Is.EqualTo(1));
    });
  }

  // --- Crossing the gate mid-use ----------------------------------------------------------------

  [Test]
  public void Switching_to_the_editable_style_drops_the_combo_back_to_owner_drawn() {
    var combo = Realize(Combo(), Promoting());
    Assume.That(combo.IsNativeWidget, Is.True);

    combo.DropDownStyle = ComboBoxStyle.DropDown;

    Assert.That(combo.IsNativeWidget, Is.False);
  }

  [Test]
  public void Giving_a_promoted_combo_an_image_selector_drops_it_back_to_owner_drawn() {
    var combo = Realize(Combo(), Promoting());
    Assume.That(combo.IsNativeWidget, Is.True);

    combo.ImageSelector = static _ => null;

    Assert.That(combo.IsNativeWidget, Is.False);
  }

  [Test]
  public void The_selection_survives_the_swap_to_owner_drawn() {
    var combo = Realize(Combo(), Promoting());
    combo.SelectedIndex = 2;

    combo.ImageSelector = static _ => null;

    Assert.Multiple(() => {
      Assert.That(combo.IsNativeWidget, Is.False);
      Assert.That(combo.SelectedIndex, Is.EqualTo(2), "re-realizing is state transparent");
    });
  }

  [Test]
  public void Taking_the_placeholder_away_again_promotes_the_combo_back() {
    var combo = Combo();
    combo.PlaceholderText = "Pick one…";
    Realize(combo, Promoting());
    Assume.That(combo.IsNativeWidget, Is.False);

    combo.PlaceholderText = string.Empty;

    Assert.That(combo.IsNativeWidget, Is.True);
  }
}
