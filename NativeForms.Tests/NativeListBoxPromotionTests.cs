using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: a <see cref="ListBox"/> realizes onto a real platform list when nothing in its state needs
/// the owner-drawn painter. Beyond the gate and the item mirror, the assertions worth pinning are the
/// geometry readers — a promoted list is scrolled by the widget, so <see cref="ListBox.TopIndex"/> and
/// <see cref="ListBox.IndexFromPoint"/> have to come from it rather than from arithmetic over a painter
/// that is not running.
/// </summary>
[TestFixture]
internal sealed class NativeListBoxPromotionTests
{
    private static HeadlessBackend Promoting() => new() { OfferNativeListBox = true };

    private static ListBox Realize(ListBox list, IPlatformBackend backend)
    {
        var form = new Form();
        form.Controls.Add(list);
        Application.Run(form, backend);
        return list;
    }

    private static ListBox List()
    {
        var list = new ListBox { Bounds = new(0, 0, 200, 120) };
        list.Items.AddRange(["alpha", "beta", "gamma", "delta"]);
        return list;
    }

    [TearDown]
    public void Restore() => Application.PreferNativeWidgets = true;

    [Test]
    public void A_backend_that_declines_leaves_the_list_owner_drawn()
        => Assert.That(Realize(List(), new HeadlessBackend()).IsNativeWidget, Is.False);

    [Test]
    public void A_backend_that_offers_a_widget_promotes_the_list()
        => Assert.That(Realize(List(), Promoting()).IsNativeWidget, Is.True);

    [TestCase(SelectionMode.None)]
    [TestCase(SelectionMode.MultiSimple)]
    [TestCase(SelectionMode.MultiExtended)]
    public void Anything_but_single_selection_keeps_the_list_owner_drawn(SelectionMode mode)
    {
        var list = List();
        list.SelectionMode = mode;

        Realize(list, Promoting());

        Assert.That(list.IsNativeWidget, Is.False);
    }

    [Test]
    public void An_image_selector_keeps_the_list_owner_drawn()
    {
        var list = List();
        list.ImageSelector = static _ => null;

        Realize(list, Promoting());

        Assert.That(list.IsNativeWidget, Is.False, "a stock list shows no per-item icons");
    }

    [Test]
    public void A_custom_item_height_keeps_the_list_owner_drawn()
    {
        var list = List();
        list.ItemHeight = 40;

        Realize(list, Promoting());

        Assert.That(list.IsNativeWidget, Is.False, "a stock list lays rows out at its own height");
    }

    [Test]
    public void A_CheckedListBox_is_never_promoted()
    {
        var list = new CheckedListBox { Bounds = new(0, 0, 200, 120) };
        list.Items.AddRange(["alpha", "beta"]);

        Realize(list, Promoting());

        Assert.That(
            list.IsNativeWidget,
            Is.False,
            "the per-row check box is this control's own painting, and a platform list would drop it");
    }

    [Test]
    public void The_global_switch_turns_promotion_off()
    {
        Application.PreferNativeWidgets = false;

        Assert.That(Realize(List(), Promoting()).IsNativeWidget, Is.False);
    }

    // --- The item list is mirrored ----------------------------------------------------------------

    [Test]
    public void The_items_present_at_realization_reach_the_widget()
    {
        var backend = Promoting();

        Realize(List(), backend);

        Assert.That(backend.LastListBox!.Items, Is.EqualTo(new[] { "alpha", "beta", "gamma", "delta" }));
    }

    [Test]
    public void Adding_an_item_after_realization_reaches_the_widget()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);

        list.Items.Add("epsilon");

        Assert.That(backend.LastListBox!.Items, Has.Length.EqualTo(5));
    }

    // --- Selection --------------------------------------------------------------------------------

    [Test]
    public void A_programmatic_selection_reaches_the_widget()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);

        list.SelectedIndex = 2;

        Assert.That(backend.LastListBox!.GetSelectedIndex(), Is.EqualTo(2));
    }

    [Test]
    public void A_selection_made_in_the_widget_reaches_the_control_once()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);
        var changes = 0;
        list.SelectedIndexChanged += (_, _) => ++changes;

        backend.LastListBox!.RaiseUserSelect(1);

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.SelectedItem, Is.EqualTo("beta"));
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clearing_the_selection_in_the_widget_reaches_the_control()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);
        list.SelectedIndex = 1;

        backend.LastListBox!.RaiseUserSelect(-1);

        Assert.That(list.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Activating_a_row_in_the_widget_raises_DoubleClick()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);
        var activations = 0;
        list.DoubleClick += (_, _) => ++activations;

        backend.LastListBox!.RaiseUserActivate();

        Assert.That(activations, Is.EqualTo(1));
    }

    // --- The widget owns the geometry -------------------------------------------------------------

    [Test]
    public void EnsureVisible_scrolls_the_widget_rather_than_a_painter()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);

        list.EnsureVisible(3);

        Assert.That(backend.LastListBox!.ScrolledTo, Is.EqualTo(3));
    }

    [Test]
    public void TopIndex_reports_the_widgets_scroll_position()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);
        backend.LastListBox!.TopIndex = 2;

        Assert.That(list.TopIndex, Is.EqualTo(2));
    }

    [Test]
    public void IndexFromPoint_asks_the_widget_which_row_it_put_there()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);
        backend.LastListBox!.TopIndex = 1;

        Assert.That(list.IndexFromPoint(10, 25), Is.EqualTo(2), "row 1 is at the top, so 25px down is row 2");
    }

    [Test]
    public void FocusedIndex_follows_the_selection_on_a_promoted_list()
    {
        var backend = Promoting();
        var list = Realize(List(), backend);

        backend.LastListBox!.RaiseUserSelect(2);

        Assert.That(list.FocusedIndex, Is.EqualTo(2), "one selection and one caret, kept together by the platform");
    }

    // --- Crossing the gate mid-use ----------------------------------------------------------------

    [Test]
    public void Switching_to_a_multi_selection_mode_drops_the_list_back_to_owner_drawn()
    {
        var list = Realize(List(), Promoting());
        Assume.That(list.IsNativeWidget, Is.True);

        list.SelectionMode = SelectionMode.MultiExtended;

        Assert.That(list.IsNativeWidget, Is.False);
    }

    [Test]
    public void The_items_survive_the_swap_to_owner_drawn()
    {
        var list = Realize(List(), Promoting());

        list.ItemHeight = 40;

        Assert.Multiple(() =>
        {
            Assert.That(list.IsNativeWidget, Is.False);
            Assert.That(list.Items, Has.Count.EqualTo(4), "re-realizing is state transparent");
        });
    }
}
