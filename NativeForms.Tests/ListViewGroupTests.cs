using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Grouped presentation of <see cref="ListView"/>: header rows (accent caption + separator rule),
/// items flattened under their group with ungrouped items in a trailing default section, and
/// virtualization over the flattened rows.
/// </summary>
[TestFixture]
internal sealed class ListViewGroupTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeGrouped()
    {
        // Details without the header band, so item rows start at y = 0 (22px each).
        var list = new ListView { Bounds = new(0, 0, 300, 220), ShowColumnHeaders = false };
        var docs = new ListViewGroup("Documents");
        var media = new ListViewGroup("Media");
        list.Groups.AddRange([docs, media]);
        list.Items.AddRange(
        [
            new ListViewItem("Song.mp3") { Group = media },
            new ListViewItem("Letter.txt") { Group = docs },
            new ListViewItem("Loose.bin"),
            new ListViewItem("Report.txt") { Group = docs },
        ]);
        return list;
    }

    [Test]
    public void Group_headers_paint_accent_caption_and_separator_rule()
    {
        var list = MakeGrouped();
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        var accent = "#FF0078D4"; // theme accent
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Documents\" " + accent)), Is.True, "accent header caption");
            Assert.That(g.Operations.Exists(o => o.StartsWith("line " + accent) && o.Contains(",20-")), Is.True, "separator rule under the first header");
        });
    }

    [Test]
    public void Items_render_under_their_group_with_ungrouped_last()
    {
        var list = MakeGrouped();

        // Flattened rows: Documents, Letter, Report, Media, Song, Default, Loose — 22px each.
        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemBounds(1).Y, Is.EqualTo(22), "Letter.txt directly under Documents");
            Assert.That(list.GetItemBounds(3).Y, Is.EqualTo(44), "Report.txt keeps collection order within the group");
            Assert.That(list.GetItemBounds(0).Y, Is.EqualTo(88), "Song.mp3 under Media");
            Assert.That(list.GetItemBounds(2).Y, Is.EqualTo(132), "Loose.bin under the default section");
        });
    }

    [Test]
    public void Ungrouped_items_get_a_default_section_header()
    {
        var list = MakeGrouped();
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.That(g.DrewText("Default"), Is.True);
    }

    [Test]
    public void Show_groups_false_flattens_back_to_plain_rows()
    {
        var list = MakeGrouped();
        list.ShowGroups = false;
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Documents"), Is.False, "no header rows");
            Assert.That(list.GetItemBounds(0).Y, Is.Zero, "model order again");
        });
    }

    [Test]
    public void List_view_never_groups()
    {
        var list = MakeGrouped();
        list.View = ListViewView.List;
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Documents"), Is.False, "the classic control shows no groups in List view");
            Assert.That(list.GetItemBounds(0).Y, Is.Zero, "model order");
        });
    }

    [Test]
    public void Icon_view_groups_wrap_cells_under_their_header()
    {
        var list = MakeGrouped();
        list.View = ListViewView.LargeIcon; // 4 cells of 64×58 per row
        var docs = list.Groups[0];
        list.Items.AddRange(
        [
            new ListViewItem("Extra1.txt") { Group = docs },
            new ListViewItem("Extra2.txt") { Group = docs },
            new ListViewItem("Extra3.txt") { Group = docs },
        ]);

        // Documents header row (22px), then Letter/Report/Extra1/Extra2 and a wrapped Extra3.
        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemBounds(1), Is.EqualTo(new Rectangle(0, 22, 64, 58)), "first cell under the header");
            Assert.That(list.GetItemBounds(5), Is.EqualTo(new Rectangle(192, 22, 64, 58)), "fourth cell of the group row");
            Assert.That(list.GetItemBounds(6), Is.EqualTo(new Rectangle(0, 80, 64, 58)), "wrapped to the next cell row");
            Assert.That(list.GetItemBounds(0).Y, Is.EqualTo(160), "Song.mp3 under the Media header (138 + 22)");
        });
    }

    [Test]
    public void Empty_groups_are_skipped()
    {
        var list = MakeGrouped();
        list.Groups.Insert(0, new ListViewGroup("Empty"));
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Empty"), Is.False);
            Assert.That(list.GetItemBounds(1).Y, Is.EqualTo(22), "layout unchanged by the empty group");
        });
    }

    [Test]
    public void Changing_an_items_group_reflattens()
    {
        var list = MakeGrouped();
        list.Items[2].Group = list.Groups[0]; // Loose.bin joins Documents

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemBounds(2).Y, Is.EqualTo(44), "into Documents, in model order before Report");
            Assert.That(list.GetItemBounds(3).Y, Is.EqualTo(66));
            Assert.That(list.GetItemBounds(0).Y, Is.EqualTo(110), "Media shifted down; no default section left");
        });
    }

    [Test]
    public void Keyboard_navigation_follows_display_order_across_groups()
    {
        var list = MakeGrouped();
        var canvas = Realize(list);

        canvas.RaiseKeyDown(Keys.Down); // first display item: Letter.txt (model 1)
        Assert.That(list.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Down); // Report.txt (model 3)
        canvas.RaiseKeyDown(Keys.Down); // Song.mp3 (model 0)
        Assert.That(list.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Clicking_a_group_header_row_selects_nothing()
    {
        var list = MakeGrouped();
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 10); // the Documents header row

        Assert.That(list.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Grouped_painting_stays_virtualized_for_100k_items()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), ShowColumnHeaders = false };
        var groups = new ListViewGroup[10];
        for (var i = 0; i < groups.Length; ++i)
            groups[i] = new("Group" + i);

        list.Groups.AddRange(groups);
        var items = new ListViewItem[100_000];
        for (var i = 0; i < items.Length; ++i)
            items[i] = new("Row" + i) { Group = groups[i % groups.Length] };

        list.Items.AddRange(items);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.Count(static o => o.StartsWith("text "));
        Assert.That(textOps, Is.LessThan(50), "only the visible rows (plus headers) are painted");
    }
}
