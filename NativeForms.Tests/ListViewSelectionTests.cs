using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Multi-selection on <see cref="ListView"/>: the extended Ctrl/Shift mouse and keyboard gestures
/// (mirroring the ListBox <see cref="SelectionMode.MultiExtended"/> engine), sorted
/// <see cref="ListView.SelectedIndices"/>, the live <see cref="ListView.SelectedItems"/> view, one
/// <see cref="ListView.SelectedIndexChanged"/> per gesture, and the per-item
/// <see cref="ListViewItem.Selected"/> flag routing.
/// </summary>
[TestFixture]
internal sealed class ListViewSelectionTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeList(out HeadlessCanvasPeer canvas)
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.Items.AddRange(
        [
            new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c"),
            new ListViewItem("d"), new ListViewItem("e"),
        ]);
        canvas = Realize(list);
        return list; // rows at y 0,22,44,66,88
    }

    [Test]
    public void Multi_select_defaults_to_true()
        => Assert.That(new ListView().MultiSelect, Is.True);

    [Test]
    public void Ctrl_click_toggles_membership()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 5); // row 0
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control); // + row 2
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));

        canvas.RaiseMouseDown(10, 5, MouseButtons.Left, KeyModifiers.Control); // - row 0
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Shift_click_ranges_from_the_anchor()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 27); // anchor row 1
        canvas.RaiseMouseDown(10, 71, MouseButtons.Left, KeyModifiers.Shift); // range to row 3
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2, 3 }));

        canvas.RaiseMouseDown(10, 5, MouseButtons.Left, KeyModifiers.Shift); // range back up to row 0
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Plain_click_replaces_the_selection()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control);
        canvas.RaiseMouseDown(10, 71);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Range_gesture_raises_selected_index_changed_once()
    {
        var list = MakeList(out var canvas);
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;

        canvas.RaiseMouseDown(10, 5); // anchor row 0
        canvas.RaiseMouseDown(10, 93, MouseButtons.Left, KeyModifiers.Shift); // rows 0..4 in one gesture

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Has.Count.EqualTo(5));
            Assert.That(selections, Is.EqualTo(2));
        });
    }

    [Test]
    public void Shift_arrow_extends_the_range()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 27); // anchor row 1
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift);
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Selected_items_view_maps_the_sorted_indices()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 71); // row 3
        canvas.RaiseMouseDown(10, 27, MouseButtons.Left, KeyModifiers.Control); // + row 1

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(list.SelectedItems.Select(static i => i.Text), Is.EqualTo(new[] { "b", "d" }));
            Assert.That(list.SelectedItem?.Text, Is.EqualTo("b"), "first selected item");
        });
    }

    [Test]
    public void Item_selected_flags_track_the_selection()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Selected, Is.True);
            Assert.That(list.Items[2].Selected, Is.True);
        });

        canvas.RaiseMouseDown(10, 71); // plain click replaces
        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Selected, Is.False);
            Assert.That(list.Items[2].Selected, Is.False);
            Assert.That(list.Items[3].Selected, Is.True);
        });
    }

    [Test]
    public void Setting_item_selected_joins_the_selection()
    {
        var list = MakeList(out _);

        list.Items[1].Selected = true;
        list.Items[3].Selected = true;
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 3 }));

        list.Items[1].Selected = false;
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Multi_select_false_gives_single_selection_gestures()
    {
        var list = MakeList(out var canvas);
        list.MultiSelect = false;

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }), "Ctrl-click replaces without MultiSelect");
    }

    [Test]
    public void Turning_multi_select_off_collapses_to_the_first_selected()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 27);
        canvas.RaiseMouseDown(10, 71, MouseButtons.Left, KeyModifiers.Control);
        list.MultiSelect = false;

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Space_toggles_the_caret_item_when_checks_are_off()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 27); // row 1 selected, caret 1
        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.SelectedIndices, Is.Empty);

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Removing_items_prunes_and_shifts_the_selection()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control); // + row 2
        list.Items.RemoveAt(2);
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));

        list.Items.RemoveAt(0);
        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.Empty);
            Assert.That(list.Items[0].Selected, Is.False);
        });
    }

    [Test]
    public void Paint_highlights_every_selected_row()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(10, 49, MouseButtons.Left, KeyModifiers.Control);

        var g = canvas.RaisePaint();

        var fills = g.Operations.FindAll(static o => o.StartsWith("fill "));
        Assert.Multiple(() =>
        {
            Assert.That(fills, Has.Count.EqualTo(3), "background + two selected rows");
            Assert.That(fills.Exists(static o => o.EndsWith(" 0,0,300,22")), Is.True, "row 0 highlighted");
            Assert.That(fills.Exists(static o => o.EndsWith(" 0,44,300,22")), Is.True, "row 2 highlighted");
        });
    }
}
