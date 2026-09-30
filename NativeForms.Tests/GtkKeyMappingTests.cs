using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The GDK keyval → <see cref="Keys"/> translation is a pure lookup, so it is asserted directly
/// instead of through a display. It earns its own fixture because a missing arm here is silent: the
/// key simply arrives as <see cref="Keys.None"/> and every control ignores it, which is how the
/// function keys went unmapped while <c>docs/controls/datagridview.md</c> documented F2 as the
/// edit gesture.
/// </summary>
[TestFixture]
internal sealed class GtkKeyMappingTests {
  // GDK names the function keys contiguously from 0xffbe, the same way Keys runs from F1.
  private const uint _GdkKeyF1 = 0xffbe;

  [Test]
  public void Every_function_key_maps_to_its_counterpart() {
    for (var offset = 0; offset < 12; ++offset)
      Assert.That(
          GtkCanvasPeer.ToKey(_GdkKeyF1 + (uint)offset),
          Is.EqualTo(Keys.F1 + offset),
          $"F{offset + 1}");
  }

  [Test]
  public void F2_maps_because_the_grid_documents_it_as_the_edit_gesture()
      => Assert.That(GtkCanvasPeer.ToKey(0xffbf), Is.EqualTo(Keys.F2));

  [Test]
  public void The_keyval_below_the_function_block_is_not_mistaken_for_one()
      => Assert.That(GtkCanvasPeer.ToKey(_GdkKeyF1 - 1), Is.Not.EqualTo(Keys.F1));

  [Test]
  public void The_keyval_above_the_function_block_is_not_mistaken_for_one()
      => Assert.That(GtkCanvasPeer.ToKey(0xffca), Is.EqualTo(Keys.None));

  // --- the physical key behind a shifted symbol ------------------------------------------------

  [TestCase('@', '2', Keys.D2, TestName = "Shift+2 on a US layout is the 2 key")]
  [TestCase('#', '3', Keys.D3, TestName = "Shift+3 on a US layout is the 3 key")]
  [TestCase(')', '0', Keys.D0, TestName = "Shift+0 on a US layout is the 0 key")]
  [TestCase('&', '1', Keys.D1, TestName = "the unshifted 1 key on AZERTY is the 1 key")]
  [TestCase('"', '2', Keys.D2, TestName = "Shift+2 on a German layout is the 2 key")]
  public void A_shifted_digit_maps_to_its_physical_key(char symbol, char otherLevel, Keys expected)
      => Assert.That(GtkCanvasPeer.ToKey(symbol, otherLevel), Is.EqualTo(expected));

  [Test]
  public void A_symbol_that_already_maps_is_not_second_guessed()
      => Assert.That(GtkCanvasPeer.ToKey('a', '1'), Is.EqualTo(Keys.A));

  [TestCase('!', '?', TestName = "a symbol whose other level is a symbol")]
  [TestCase('@', 'q', TestName = "a symbol whose other level is a letter (AltGr layouts)")]
  [TestCase('@', '\0', TestName = "a symbol with nothing at the other level")]
  public void A_symbol_with_no_digit_behind_it_stays_unmapped(char symbol, char otherLevel)
      => Assert.That(GtkCanvasPeer.ToKey(symbol, otherLevel), Is.EqualTo(Keys.None));

  [Test]
  public void Letters_digits_and_navigation_still_map_after_the_function_block_was_added() {
    Assert.Multiple(() => {
      Assert.That(GtkCanvasPeer.ToKey('a'), Is.EqualTo(Keys.A), "lowercase folds to the virtual key");
      Assert.That(GtkCanvasPeer.ToKey('Z'), Is.EqualTo(Keys.Z));
      Assert.That(GtkCanvasPeer.ToKey('7'), Is.EqualTo(Keys.D7));
      Assert.That(GtkCanvasPeer.ToKey(0xff0d), Is.EqualTo(Keys.Enter), "Return");
      Assert.That(GtkCanvasPeer.ToKey(0xff1b), Is.EqualTo(Keys.Escape));
    });
  }
}
