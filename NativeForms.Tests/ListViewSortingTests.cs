using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Sorting on <see cref="ListView"/>: <see cref="ListView.Sorting"/>/<see cref="ListView.ItemSorter"/>
/// mutate the item order in place (unlike DataGridView's index indirection), Details header clicks
/// raise <see cref="ListView.ColumnClick"/> and drive the automatic sort (repeat clicks flip the
/// direction, sub-item columns sort by their text), the active column carries the themed arrow, and
/// selection sticks to its items across a sort.
/// </summary>
[TestFixture]
internal sealed class ListViewSortingTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeUnsorted()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220) };
        list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
        list.Items.AddRange(
        [
            new ListViewItem("cherry", "1"),
            new ListViewItem("apple", "3"),
            new ListViewItem("banana", "2"),
        ]);
        return list;
    }

    private static string[] Texts(ListView list)
    {
        var texts = new string[list.Items.Count];
        for (var i = 0; i < texts.Length; ++i)
            texts[i] = list.Items[i].Text;

        return texts;
    }

    [Test]
    public void Assigning_sorting_ascending_sorts_items_in_place_by_text()
    {
        var list = MakeUnsorted();

        list.Sorting = SortOrder.Ascending;

        Assert.That(Texts(list), Is.EqualTo(new[] { "apple", "banana", "cherry" }));
    }

    [Test]
    public void Descending_reverses_the_text_order()
    {
        var list = MakeUnsorted();

        list.Sorting = SortOrder.Descending;

        Assert.That(Texts(list), Is.EqualTo(new[] { "cherry", "banana", "apple" }));
    }

    [Test]
    public void Item_sorter_wins_over_sorting()
    {
        var list = MakeUnsorted();
        list.Sorting = SortOrder.Ascending;

        list.ItemSorter = static (a, b) => b.Text.Length.CompareTo(a.Text.Length); // longest first

        // banana/cherry tie on length; the stable sort keeps their previous (alphabetical) order.
        Assert.That(Texts(list), Is.EqualTo(new[] { "banana", "cherry", "apple" }));
    }

    [Test]
    public void Header_click_raises_column_click_with_the_column_index()
    {
        var list = MakeUnsorted();
        var canvas = Realize(list);
        var clicked = new List<int>();
        list.ColumnClick += (_, e) => clicked.Add(e.Column);

        canvas.RaiseMouseDown(10, 5);  // Name
        canvas.RaiseMouseDown(150, 5); // Size

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(Texts(list), Is.EqualTo(new[] { "cherry", "apple", "banana" }), "no active sort, order untouched");
        });
    }

    [Test]
    public void Header_click_sorts_when_sorting_is_active_and_repeat_clicks_flip()
    {
        var list = MakeUnsorted();
        list.Sorting = SortOrder.Ascending;
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 5);
        Assert.That(Texts(list), Is.EqualTo(new[] { "apple", "banana", "cherry" }));

        canvas.RaiseMouseDown(10, 5);
        Assert.Multiple(() =>
        {
            Assert.That(list.Sorting, Is.EqualTo(SortOrder.Descending));
            Assert.That(Texts(list), Is.EqualTo(new[] { "cherry", "banana", "apple" }));
        });
    }

    [Test]
    public void Clicking_a_sub_item_column_sorts_by_its_text()
    {
        var list = MakeUnsorted();
        list.Sorting = SortOrder.Ascending;
        var canvas = Realize(list);

        canvas.RaiseMouseDown(150, 5); // the Size column

        Assert.That(Texts(list), Is.EqualTo(new[] { "cherry", "banana", "apple" }), "sizes 1/2/3 ascending");
    }

    [Test]
    public void Active_column_paints_the_sort_arrow()
    {
        var list = MakeUnsorted();
        list.Sorting = SortOrder.Ascending;
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 5); // sort by Name, arrow at the column's right edge
        var g = canvas.RaisePaint();
        Assert.That(g.Operations.Exists(static o => o.StartsWith("line ") && o.EndsWith(" 131,9-132,9")), Is.True, "ascending arrow tip");

        canvas.RaiseMouseDown(10, 5); // flip to descending
        g = canvas.RaisePaint();
        Assert.That(g.Operations.Exists(static o => o.StartsWith("line ") && o.EndsWith(" 131,12-132,12")), Is.True, "descending arrow tip");
    }

    [Test]
    public void Sort_keeps_the_selection_on_its_items()
    {
        var list = MakeUnsorted();
        var canvas = Realize(list);
        canvas.RaiseMouseDown(10, 30); // select "cherry" (row 0)

        list.Sorting = SortOrder.Ascending;

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }), "cherry moved to the end");
            Assert.That(list.SelectedItem?.Text, Is.EqualTo("cherry"));
        });
    }

    [Test]
    public void Sort_is_stable_for_equal_keys()
    {
        var list = new ListView();
        list.Items.AddRange(
        [
            new ListViewItem("b") { Tag = 1 },
            new ListViewItem("a") { Tag = 2 },
            new ListViewItem("b") { Tag = 3 },
        ]);

        list.Sorting = SortOrder.Ascending;

        Assert.Multiple(() =>
        {
            Assert.That(Texts(list), Is.EqualTo(new[] { "a", "b", "b" }));
            Assert.That(list.Items[1].Tag, Is.EqualTo(1), "equal keys keep their relative order");
            Assert.That(list.Items[2].Tag, Is.EqualTo(3));
        });
    }
}
