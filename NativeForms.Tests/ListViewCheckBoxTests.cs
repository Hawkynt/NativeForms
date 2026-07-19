using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The check-box story of <see cref="ListView"/>: the vetoable <see cref="ListView.ItemCheck"/> /
/// after-the-fact <see cref="ListView.ItemChecked"/> pipeline behind <see cref="ListViewItem.Checked"/>,
/// glyph placement per view (inline leading glyph vs. the corner overlay), glyph clicks and the
/// Space gesture.
/// </summary>
[TestFixture]
internal sealed class ListViewCheckBoxTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeChecked(ListViewView view = ListViewView.Details)
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = view, CheckBoxes = true };
        if (view == ListViewView.Details)
            list.Columns.Add(new ColumnHeader("Name", 140));

        list.Items.AddRange([new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c")]);
        return list;
    }

    [Test]
    public void Checked_setter_raises_item_check_before_and_item_checked_after()
    {
        var list = MakeChecked();
        var log = new List<string>();
        list.ItemCheck += (_, e) => log.Add($"check {e.Index} {e.CurrentValue}->{e.NewValue}");
        list.ItemChecked += (_, e) => log.Add($"checked {e.Item.Text} {e.Item.Checked}");

        list.Items[1].Checked = true;

        Assert.Multiple(() =>
        {
            Assert.That(log, Is.EqualTo(new[] { "check 1 False->True", "checked b True" }));
            Assert.That(list.Items[1].Checked, Is.True);
        });
    }

    [Test]
    public void Item_check_handler_can_veto_the_flip()
    {
        var list = MakeChecked();
        var checkedEvents = 0;
        list.ItemCheck += (_, e) => e.NewValue = e.CurrentValue;
        list.ItemChecked += (_, _) => ++checkedEvents;

        list.Items[0].Checked = true;

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Checked, Is.False);
            Assert.That(checkedEvents, Is.Zero, "vetoed flips raise no ItemChecked");
        });
    }

    [Test]
    public void Details_paints_the_glyph_before_the_label()
    {
        var list = MakeChecked();
        list.Items[0].Checked = true;
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        // Row 0 sits under the 22px header; the 14px glyph is centered: y = 22 + 4 = 26.
        Assert.That(g.Operations.Exists(static o => o.StartsWith("rect ") && o.EndsWith(" 2,26,14,14")), Is.True, "glyph box at the row start");
    }

    [Test]
    public void LargeIcon_paints_the_glyph_as_top_left_overlay()
    {
        var list = MakeChecked(ListViewView.LargeIcon);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("rect ") && o.EndsWith(" 2,2,14,14")), Is.True, "overlay in the first cell's corner");
            Assert.That(g.Operations.Exists(static o => o.StartsWith("rect ") && o.EndsWith(" 66,2,14,14")), Is.True, "overlay in the second cell's corner");
        });
    }

    [Test]
    public void SmallIcon_paints_the_glyph_inline_before_each_cell()
    {
        var list = MakeChecked(ListViewView.SmallIcon); // 124px cells, 22px rows
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("rect ") && o.EndsWith(" 2,4,14,14")), Is.True, "glyph centered in the first cell");
            Assert.That(g.Operations.Exists(static o => o.StartsWith("rect ") && o.EndsWith(" 126,4,14,14")), Is.True, "glyph in the second cell");
        });
    }

    [Test]
    public void Clicking_the_glyph_toggles_without_selecting()
    {
        var list = MakeChecked();
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 30); // inside the glyph zone of row 0

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Checked, Is.True);
            Assert.That(list.SelectedIndex, Is.EqualTo(-1), "glyph clicks do not select");
        });

        canvas.RaiseMouseDown(10, 30);
        Assert.That(list.Items[0].Checked, Is.False);
    }

    [Test]
    public void Clicking_past_the_glyph_selects_without_toggling()
    {
        var list = MakeChecked();
        var canvas = Realize(list);

        canvas.RaiseMouseDown(60, 30);

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.Zero);
            Assert.That(list.Items[0].Checked, Is.False);
        });
    }

    [Test]
    public void Space_toggles_the_checks_of_the_whole_selection()
    {
        var list = MakeChecked();
        var canvas = Realize(list);

        canvas.RaiseMouseDown(60, 30);  // select row 0
        canvas.RaiseMouseDown(60, 74, MouseButtons.Left, KeyModifiers.Control); // + row 2
        canvas.RaiseKeyDown(Keys.Space);

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Checked, Is.True);
            Assert.That(list.Items[1].Checked, Is.False);
            Assert.That(list.Items[2].Checked, Is.True);
        });

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.Items[0].Checked, Is.False);
    }

    [Test]
    public void Detached_items_flip_silently()
    {
        var item = new ListViewItem("free");
        item.Checked = true;

        Assert.That(item.Checked, Is.True);
    }
}
