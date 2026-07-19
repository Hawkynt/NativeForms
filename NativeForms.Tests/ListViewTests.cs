using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ListViewTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeDetails()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220) };
        list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
        list.Items.AddRange(
        [
            new ListViewItem("File1", "1 KB"),
            new ListViewItem("File2", "2 KB"),
            new ListViewItem("File3", "3 KB"),
        ]);
        return list;
    }

    [Test]
    public void Details_paints_headers_and_item_text_and_subitems()
    {
        var list = MakeDetails();
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Name"), Is.True, "header caption");
            Assert.That(g.DrewText("Size"), Is.True, "header caption");
            Assert.That(g.DrewText("File1"), Is.True, "primary cell text");
            Assert.That(g.DrewText("1 KB"), Is.True, "sub-item text");
        });
    }

    [Test]
    public void Click_below_header_selects_row_and_raises_event()
    {
        var list = MakeDetails();
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;
        var canvas = Realize(list);

        // Header occupies y 0..22; second row is 44..66.
        canvas.RaiseMouseDown(10, 50);

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.SelectedItem?.Text, Is.EqualTo("File2"));
            Assert.That(list.SelectedItem?.Selected, Is.True);
            Assert.That(selections, Is.EqualTo(1));
        });
    }

    [Test]
    public void Click_on_header_does_not_select()
    {
        var list = MakeDetails();
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 5); // within the header band

        Assert.That(list.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Arrow_keys_move_selection()
    {
        var list = MakeDetails();
        var canvas = Realize(list);

        canvas.RaiseKeyDown(Keys.Down); // -1 -> 0
        canvas.RaiseKeyDown(Keys.Down); // 0 -> 1
        canvas.RaiseKeyDown(Keys.Up);   // 1 -> 0

        Assert.That(list.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void Switching_view_to_list_repaints_and_still_draws_text()
    {
        var list = MakeDetails();
        var canvas = Realize(list);
        var before = canvas.InvalidateCount;

        list.View = ListViewView.List;

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(canvas.InvalidateCount, Is.GreaterThan(before), "view change invalidates");
            Assert.That(g.DrewText("File1"), Is.True);
            Assert.That(g.DrewText("Name"), Is.False, "no header in List view");
        });
    }

    [Test]
    public void Painting_is_virtualized_for_large_item_counts()
    {
        var list = new ListView { Bounds = new(0, 0, 200, 220) };
        list.Columns.Add(new ColumnHeader("Name", 180));
        var items = new ListViewItem[50000];
        for (var i = 0; i < items.Length; ++i)
            items[i] = new ListViewItem("Row" + i);

        list.Items.AddRange(items);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.Count(o => o.StartsWith("text "));
        Assert.That(textOps, Is.LessThan(50), "only the visible rows (plus header) are painted");
    }
}
