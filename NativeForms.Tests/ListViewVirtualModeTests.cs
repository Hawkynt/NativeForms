using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="ListView"/> in <see cref="ListView.VirtualMode"/> serves its rows from
/// <see cref="ListView.RetrieveVirtualItem"/> over a <see cref="ListView.VirtualListSize"/>: only the
/// visible rows are fetched, selection and keyboard navigation stay index-based, and the model
/// <see cref="ListView.Items"/> collection is never populated.
/// </summary>
[TestFixture]
internal sealed class ListViewVirtualModeTests
{
    [Test]
    public void Only_the_visible_rows_are_fetched_never_the_whole_list()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 100_000;
        var fetched = new List<int>();
        list.RetrieveVirtualItem += (_, e) => { fetched.Add(e.ItemIndex); e.Item = new ListViewItem("Row" + e.ItemIndex); };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(list);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        fetched.Clear();
        canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.Not.Empty, "the visible rows are fetched");
            Assert.That(fetched.Count, Is.LessThan(50), "only a screenful is fetched, not 100k");
            Assert.That(list.Items, Is.Empty, "no model item is materialised");
        });
    }

    [Test]
    public void A_virtual_row_is_painted_from_the_retrieved_item()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 100;
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem("Row" + e.ItemIndex);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(list);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.Contains("\"Row0\"")), Is.True, "the first virtual row is drawn");
    }

    [Test]
    public void SelectedIndex_is_clamped_to_the_virtual_size_and_paints_a_selection()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 10;
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem("Row" + e.ItemIndex);

        list.SelectedIndex = 3;
        Assert.That(list.SelectedIndex, Is.EqualTo(3));

        list.SelectedIndex = 999;
        Assert.That(list.SelectedIndex, Is.EqualTo(-1), "an out-of-range index clears the selection");
    }

    [Test]
    public void The_down_arrow_moves_the_selection_over_virtual_rows()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 10;
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem("Row" + e.ItemIndex);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(list);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        list.SelectedIndex = 0;
        canvas.RaiseKeyDown(Keys.Down);

        Assert.That(list.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void Clicking_a_virtual_row_selects_its_index()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 100;
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem("Row" + e.ItemIndex);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(list);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        // List view: no header, 22-px rows. The third row spans y 44..66.
        canvas.RaiseMouseDown(10, 50);

        Assert.That(list.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void Leaving_virtual_mode_clears_the_selection()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.VirtualMode = true;
        list.VirtualListSize = 10;
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem("Row" + e.ItemIndex);
        list.SelectedIndex = 4;

        list.VirtualMode = false;

        Assert.That(list.SelectedIndex, Is.EqualTo(-1));
    }
}
