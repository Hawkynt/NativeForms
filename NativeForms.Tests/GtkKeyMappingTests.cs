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
internal sealed class GtkKeyMappingTests
{
    // GDK names the function keys contiguously from 0xffbe, the same way Keys runs from F1.
    private const uint _GdkKeyF1 = 0xffbe;

    [Test]
    public void Every_function_key_maps_to_its_counterpart()
    {
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

    [Test]
    public void Letters_digits_and_navigation_still_map_after_the_function_block_was_added()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GtkCanvasPeer.ToKey('a'), Is.EqualTo(Keys.A), "lowercase folds to the virtual key");
            Assert.That(GtkCanvasPeer.ToKey('Z'), Is.EqualTo(Keys.Z));
            Assert.That(GtkCanvasPeer.ToKey('7'), Is.EqualTo(Keys.D7));
            Assert.That(GtkCanvasPeer.ToKey(0xff0d), Is.EqualTo(Keys.Enter), "Return");
            Assert.That(GtkCanvasPeer.ToKey(0xff1b), Is.EqualTo(Keys.Escape));
        });
    }
}
