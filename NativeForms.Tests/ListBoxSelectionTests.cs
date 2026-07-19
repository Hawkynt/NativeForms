using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ListBoxSelectionTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListBox CreateList(SelectionMode mode, out HeadlessCanvasPeer canvas)
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 110), SelectionMode = mode }; // 5 rows at 22px
        list.Items.AddRange(["a", "b", "c", "d", "e"]);
        canvas = Realize(list);
        return list;
    }

    [Test]
    public void Default_selection_mode_is_one()
        => Assert.That(new ListBox().SelectionMode, Is.EqualTo(SelectionMode.One));

    [Test]
    public void One_mode_click_replaces_selection()
    {
        var list = CreateList(SelectionMode.One, out var canvas);

        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 25); // row 1

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(list.SelectedItems, Is.EqualTo(new[] { "b" }));
        });
    }

    [Test]
    public void None_mode_click_moves_caret_but_selects_nothing()
    {
        var list = CreateList(SelectionMode.None, out var canvas);
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;

        canvas.RaiseMouseDown(10, 47); // row 2

        Assert.Multiple(() =>
        {
            Assert.That(list.FocusedIndex, Is.EqualTo(2));
            Assert.That(list.SelectedIndex, Is.EqualTo(-1));
            Assert.That(list.SelectedIndices, Is.Empty);
            Assert.That(selections, Is.Zero);
        });
    }

    [Test]
    public void MultiSimple_click_toggles_membership()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;

        canvas.RaiseMouseDown(10, 25); // row 1 on
        canvas.RaiseMouseDown(10, 47); // row 2 on
        canvas.RaiseMouseDown(10, 25); // row 1 off

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }));
            Assert.That(selections, Is.EqualTo(3));
        });
    }

    [Test]
    public void MultiExtended_plain_click_replaces_selection()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 47, MouseButtons.Left, KeyModifiers.Control); // + row 2
        canvas.RaiseMouseDown(10, 69); // plain click row 3 replaces

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void MultiExtended_ctrl_click_toggles()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 5); // row 0
        canvas.RaiseMouseDown(10, 47, MouseButtons.Left, KeyModifiers.Control); // + row 2
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));

        canvas.RaiseMouseDown(10, 5, MouseButtons.Left, KeyModifiers.Control); // - row 0
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void MultiExtended_shift_click_ranges_from_the_anchor()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 25); // anchor row 1
        canvas.RaiseMouseDown(10, 69, MouseButtons.Left, KeyModifiers.Shift); // range to row 3
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2, 3 }));

        canvas.RaiseMouseDown(10, 5, MouseButtons.Left, KeyModifiers.Shift); // range back up to row 0
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Shift_click_range_raises_selected_index_changed_once()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;

        canvas.RaiseMouseDown(10, 5); // anchor row 0
        canvas.RaiseMouseDown(10, 91, MouseButtons.Left, KeyModifiers.Shift); // rows 0..4 in one gesture

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Has.Count.EqualTo(5));
            Assert.That(selections, Is.EqualTo(2));
        });
    }

    [Test]
    public void Selected_indices_stay_sorted_regardless_of_click_order()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);

        canvas.RaiseMouseDown(10, 91); // row 4
        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 47); // row 2

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2, 4 }));
    }

    [Test]
    public void Setting_selected_index_in_multi_mode_replaces_selection()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(10, 47);
        list.SelectedIndex = 3;

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
            Assert.That(list.SelectedIndex, Is.EqualTo(3));
        });
    }

    [Test]
    public void MultiSimple_arrows_move_caret_without_selecting_and_space_toggles()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);

        canvas.RaiseMouseDown(10, 5); // row 0 selected, caret 0
        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.Multiple(() =>
        {
            Assert.That(list.FocusedIndex, Is.EqualTo(2));
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
        });

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));
    }

    [Test]
    public void MultiExtended_plain_arrow_replaces_selection()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 25); // row 1
        canvas.RaiseMouseDown(10, 69, MouseButtons.Left, KeyModifiers.Control); // + row 3
        canvas.RaiseKeyDown(Keys.Down); // caret 3 -> 4, plain arrow replaces

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void MultiExtended_shift_arrow_extends_the_range()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 25); // anchor row 1
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift);
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void MultiExtended_space_toggles_the_caret_item()
    {
        var list = CreateList(SelectionMode.MultiExtended, out var canvas);

        canvas.RaiseMouseDown(10, 25); // row 1 selected, caret 1
        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.SelectedIndices, Is.Empty);

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void None_mode_arrows_move_only_the_caret()
    {
        var list = CreateList(SelectionMode.None, out var canvas);

        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Down);

        Assert.Multiple(() =>
        {
            Assert.That(list.FocusedIndex, Is.EqualTo(1));
            Assert.That(list.SelectedIndices, Is.Empty);
        });
    }

    [Test]
    public void Removing_items_prunes_and_shifts_the_selection()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);
        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 47); // row 2

        list.Items.RemoveAt(2); // selected row vanishes
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));

        canvas.RaiseMouseDown(10, 47); // reselect row 2 ("d")
        list.Items.RemoveAt(0); // unselected row before it vanishes
        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(list.SelectedItems, Is.EqualTo(new[] { "d" }));
        });
    }

    [Test]
    public void Paint_highlights_every_selected_row()
    {
        var list = CreateList(SelectionMode.MultiSimple, out var canvas);
        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 47); // row 2

        var g = canvas.RaisePaint();

        var fills = g.Operations.FindAll(o => o.StartsWith("fill "));
        Assert.Multiple(() =>
        {
            Assert.That(fills, Has.Count.EqualTo(3), "background + two selected rows");
            Assert.That(fills.Exists(o => o.EndsWith(" 0,0,120,22")), Is.True, "row 0 highlighted");
            Assert.That(fills.Exists(o => o.EndsWith(" 0,44,120,22")), Is.True, "row 2 highlighted");
        });
    }
}
