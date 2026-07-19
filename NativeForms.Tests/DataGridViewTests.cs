using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class DataGridViewTests
{
    private sealed record Person(string Name, int Age);

    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static DataGridView MakeGrid()
    {
        // 22px header + 4 rows at 22px.
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });
        return grid;
    }

    [Test]
    public void Paints_header_text_and_cell_values()
    {
        var grid = MakeGrid();
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25)]);
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Name"), Is.True, "header text");
            Assert.That(g.DrewText("Age"), Is.True, "header text");
            Assert.That(g.DrewText("Alice"), Is.True, "cell value");
            Assert.That(g.DrewText("Bob"), Is.True, "cell value");
            Assert.That(g.DrewText("30"), Is.True, "cell value");
        });
    }

    [Test]
    public void DataSource_replaces_rows_and_paints_them()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        grid.DataSource = new[] { new Person("Carol", 40) };

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(grid.Items, Has.Count.EqualTo(1));
            Assert.That(g.DrewText("Carol"), Is.True);
        });
    }

    [Test]
    public void Clicking_data_row_selects_it_and_raises_event()
    {
        var grid = MakeGrid();
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        var selections = 0;
        grid.SelectionChanged += (_, _) => ++selections;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 50); // header 22 + row 1 (44..66)

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
            Assert.That(grid.SelectedItem, Is.EqualTo(new Person("Bob", 25)));
            Assert.That(selections, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clicking_the_header_does_not_select_a_row()
    {
        var grid = MakeGrid();
        grid.Items.AddRange([new Person("Alice", 30)]);
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 5); // inside the 22px header

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Arrow_keys_move_selection()
    {
        var grid = MakeGrid();
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        var canvas = Realize(grid);

        canvas.RaiseKeyDown(Keys.Down); // -1 -> 0
        canvas.RaiseKeyDown(Keys.Down); // 0 -> 1
        canvas.RaiseKeyDown(Keys.Down); // 1 -> 2
        canvas.RaiseKeyDown(Keys.Up);   // 2 -> 1

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
    }

    [Test]
    public void Wheel_scrolls_the_top_row_and_clamps()
    {
        var grid = MakeGrid(); // 4 visible data rows
        for (var i = 0; i < 10; ++i)
            grid.Items.Add(new Person($"P{i}", i));
        var canvas = Realize(grid);

        canvas.RaiseMouseWheel(-120); // down 3 rows
        Assert.That(grid.TopRow, Is.EqualTo(3));

        canvas.RaiseMouseWheel(-120); // clamp at max (10 - 4 = 6)
        Assert.That(grid.TopRow, Is.EqualTo(6));

        canvas.RaiseMouseWheel(120); // back up
        Assert.That(grid.TopRow, Is.EqualTo(3));
    }

    [Test]
    public void Removing_selected_row_clamps_selection()
    {
        var grid = MakeGrid();
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        grid.SelectedRowIndex = 2;
        var canvas = Realize(grid);

        grid.Items.RemoveAt(2);

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
    }

    [Test]
    public void Virtualization_paints_only_visible_rows()
    {
        var grid = MakeGrid(); // 2 columns, ~4-5 visible rows
        for (var i = 0; i < 100_000; ++i)
            grid.Items.Add(new Person($"P{i}", i));
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.FindAll(o => o.StartsWith("text ")).Count;
        // 2 header cells + at most (VisibleRowCount + 1) rows * 2 columns — a small bounded number,
        // nowhere near 100000. Proves rows are virtualized rather than all rendered.
        Assert.That(textOps, Is.LessThan(32), $"rendered {textOps} text ops for 100000 rows");
    }
}
