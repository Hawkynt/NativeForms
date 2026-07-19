using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The icon layouts of <see cref="ListView"/>: LargeIcon/SmallIcon/Tile cell geometry, 2D keyboard
/// navigation across the cell grid, and per-view paint virtualization. Default metrics: theme row
/// height 22, large icon 32×32 (cell 64×58), small icon 16×16 (cell 124×22), tile cell 160×44.
/// </summary>
[TestFixture]
internal sealed class ListViewIconViewTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ListView MakeIconList(ListViewView view, int itemCount = 10)
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = view };
        for (var i = 0; i < itemCount; ++i)
            list.Items.Add(new ListViewItem("Item" + i, "Sub" + i));

        return list;
    }

    [Test]
    public void LargeIcon_cells_flow_left_to_right_then_wrap()
    {
        var list = MakeIconList(ListViewView.LargeIcon); // 300 / 64 = 4 cells per row

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemBounds(0), Is.EqualTo(new Rectangle(0, 0, 64, 58)));
            Assert.That(list.GetItemBounds(1), Is.EqualTo(new Rectangle(64, 0, 64, 58)));
            Assert.That(list.GetItemBounds(4), Is.EqualTo(new Rectangle(0, 58, 64, 58)));
        });
    }

    [Test]
    public void LargeIcon_cell_size_derives_from_image_list_image_size()
    {
        var list = MakeIconList(ListViewView.LargeIcon);
        using var images = new ImageList(48);
        list.LargeImageList = images;

        Assert.That(list.GetItemBounds(1), Is.EqualTo(new Rectangle(80, 0, 80, 74)), "cell = icon + padding, icon + label height");
    }

    [Test]
    public void LargeIcon_paints_icon_centered_above_label()
    {
        var list = MakeIconList(ListViewView.LargeIcon, 2);
        var backend = new HeadlessBackend();
        list.Items[0].Image = backend.CreateImage(32, 32, new int[32 * 32]);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("image ") && o.EndsWith("@16,2,32,32")), Is.True, "icon centered in the 64px cell");
            Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Item0\"") && o.Contains("@2,36")), Is.True, "label under the icon");
        });
    }

    [Test]
    public void SmallIcon_cells_flow_in_rows_of_fixed_width()
    {
        var list = MakeIconList(ListViewView.SmallIcon); // 300 / 124 = 2 cells per row

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemBounds(0), Is.EqualTo(new Rectangle(0, 0, 124, 22)));
            Assert.That(list.GetItemBounds(1), Is.EqualTo(new Rectangle(124, 0, 124, 22)));
            Assert.That(list.GetItemBounds(2), Is.EqualTo(new Rectangle(0, 22, 124, 22)));
        });
    }

    [Test]
    public void Tile_paints_label_and_first_subitem_stacked_beside_the_icon()
    {
        var list = MakeIconList(ListViewView.Tile, 2); // one 160×44 cell per row
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Item0\"") && o.Contains("@38,0")), Is.True, "label right of the 32px icon slot");
            Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Sub0\"") && o.Contains("@38,22")), Is.True, "sub-item on the second line");
            Assert.That(list.GetItemBounds(1), Is.EqualTo(new Rectangle(0, 44, 160, 44)));
        });
    }

    [Test]
    public void Grid_views_navigate_in_two_dimensions()
    {
        var list = MakeIconList(ListViewView.LargeIcon); // 4 columns
        var canvas = Realize(list);

        canvas.RaiseKeyDown(Keys.Down);  // -1 -> 0
        canvas.RaiseKeyDown(Keys.Right); // 0 -> 1
        canvas.RaiseKeyDown(Keys.Down);  // 1 -> 5 (next row)
        Assert.That(list.SelectedIndex, Is.EqualTo(5));

        canvas.RaiseKeyDown(Keys.Up);    // 5 -> 1
        canvas.RaiseKeyDown(Keys.Left);  // 1 -> 0
        Assert.That(list.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Left_and_right_edges_clamp_grid_navigation()
    {
        var list = MakeIconList(ListViewView.LargeIcon, 6);
        var canvas = Realize(list);

        canvas.RaiseKeyDown(Keys.Down); // 0
        canvas.RaiseKeyDown(Keys.Left); // clamps at 0
        Assert.That(list.SelectedIndex, Is.Zero);

        canvas.RaiseKeyDown(Keys.End);
        canvas.RaiseKeyDown(Keys.Down); // clamps at the last item
        Assert.That(list.SelectedIndex, Is.EqualTo(5));
    }

    [Test]
    public void Clicking_a_cell_selects_its_item()
    {
        var list = MakeIconList(ListViewView.LargeIcon);
        var canvas = Realize(list);

        canvas.RaiseMouseDown(70, 60); // column 1, row 1 -> item 5

        Assert.That(list.SelectedIndex, Is.EqualTo(5));
    }

    [Test]
    public void Wheel_scrolls_grid_rows_and_keyboard_stays_reachable()
    {
        var list = MakeIconList(ListViewView.LargeIcon, 100); // 25 grid rows, 3 visible
        var canvas = Realize(list);

        canvas.RaiseMouseWheel(-120);
        Assert.That(list.TopIndex, Is.EqualTo(3), "wheel scrolls three rows of cells");

        list.SelectedIndex = 99;
        Assert.That(list.TopIndex, Is.EqualTo(22), "selection scrolled the last row into view");
    }

    [TestCase(ListViewView.LargeIcon)]
    [TestCase(ListViewView.SmallIcon)]
    [TestCase(ListViewView.Tile)]
    public void Icon_views_virtualize_painting_for_100k_items(ListViewView view)
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = view };
        var items = new ListViewItem[100_000];
        for (var i = 0; i < items.Length; ++i)
            items[i] = new("Row" + i);

        list.Items.AddRange(items);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.Count(static o => o.StartsWith("text "));
        Assert.That(textOps, Is.LessThan(60), "only the visible cells are painted");
    }
}
