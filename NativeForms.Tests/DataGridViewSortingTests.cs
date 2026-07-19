using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Header-click sorting: the ascending/descending toggle, the themed arrow glyph, the index
/// indirection that leaves <see cref="DataGridView.Items"/> untouched, custom comparisons, lazy
/// re-sorting after item changes, and model-stable selection and event indices under a sort.
/// </summary>
[TestFixture]
internal sealed class DataGridViewSortingTests
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
        // 22px header + 4 rows at 22px; the Age column (x 100..160) sorts automatically.
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age)
        {
            Width = 60,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
        grid.Items.AddRange([new Person("Bob", 25), new Person("Alice", 30), new Person("Carol", 20)]);
        return grid;
    }

    private static int TextRowY(RecordingGraphics g, string name)
    {
        var op = g.Operations.Find(o => o.StartsWith($"text \"{name}\""));
        Assert.That(op, Is.Not.Null, $"'{name}' painted");
        return int.Parse(op!.Split(',')[^1]);
    }

    [Test]
    public void Header_click_sorts_ascending_then_descending()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(130, 5); // Age header

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(grid.SortedColumn, Is.SameAs(grid.Columns[1]));
            Assert.That(grid.SortOrder, Is.EqualTo(SortOrder.Ascending));
            Assert.That(TextRowY(g, "Carol"), Is.EqualTo(22), "age 20 first");
            Assert.That(TextRowY(g, "Bob"), Is.EqualTo(44));
            Assert.That(TextRowY(g, "Alice"), Is.EqualTo(66));
        });

        canvas.RaiseMouseDown(130, 5);

        g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(grid.SortOrder, Is.EqualTo(SortOrder.Descending));
            Assert.That(TextRowY(g, "Alice"), Is.EqualTo(22), "age 30 first");
            Assert.That(TextRowY(g, "Carol"), Is.EqualTo(66));
        });
    }

    [Test]
    public void Sorting_never_mutates_the_items_collection()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(130, 5);
        canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(((Person)grid.Items[0]!).Name, Is.EqualTo("Bob"));
            Assert.That(((Person)grid.Items[1]!).Name, Is.EqualTo("Alice"));
            Assert.That(((Person)grid.Items[2]!).Name, Is.EqualTo("Carol"));
        });
    }

    [Test]
    public void Sorted_header_paints_an_arrow_glyph()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        var before = canvas.RaisePaint();
        canvas.RaiseMouseDown(130, 5);
        var after = canvas.RaisePaint();

        // The arrow strokes with the header text color; no other header-colored lines exist.
        Assert.Multiple(() =>
        {
            Assert.That(before.Operations.Exists(static o => o.StartsWith("line #FF303030")), Is.False, "no arrow while unsorted");
            Assert.That(after.Operations.FindAll(static o => o.StartsWith("line #FF303030")), Has.Count.EqualTo(4), "arrow triangle");
        });
    }

    [Test]
    public void Sort_uses_the_custom_comparison_when_provided()
    {
        var grid = MakeGrid();
        grid.Columns[1].SortComparison = static (x, y) => ((Person)y!).Age - ((Person)x!).Age; // inverted
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(130, 5); // "ascending" under the inverted comparison

        var g = canvas.RaisePaint();
        Assert.That(TextRowY(g, "Alice"), Is.EqualTo(22), "custom comparison drives the order");
    }

    [Test]
    public void NotSortable_header_click_does_nothing()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 5); // Name header, default NotSortable

        Assert.Multiple(() =>
        {
            Assert.That(grid.SortedColumn, Is.Null);
            Assert.That(grid.SortOrder, Is.EqualTo(SortOrder.None));
        });
    }

    [Test]
    public void Selection_follows_the_item_across_a_sort()
    {
        var grid = MakeGrid();
        grid.SelectedRowIndex = 1; // Alice
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(130, 5);

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "model index is stable");
            Assert.That(((Person)grid.SelectedItem!).Name, Is.EqualTo("Alice"));
        });
    }

    [Test]
    public void Keyboard_navigation_follows_the_display_order()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(130, 5); // ascending by age: Carol, Bob, Alice
        grid.SelectedRowIndex = 2;     // Carol (display row 0)

        canvas.RaiseKeyDown(Keys.Down);

        Assert.That(((Person)grid.SelectedItem!).Name, Is.EqualTo("Bob"), "next row in display order");
    }

    [Test]
    public void Cell_events_report_model_indices_while_sorted()
    {
        var grid = MakeGrid();
        DataGridViewCellEventArgs? click = null;
        grid.CellClick += (_, e) => click = e;
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(130, 5); // ascending: display row 0 is Carol = Items[2]

        canvas.RaiseMouseDown(10, 30); // first data row

        Assert.Multiple(() =>
        {
            Assert.That(click, Is.Not.Null);
            Assert.That(click!.RowIndex, Is.EqualTo(2), "Items index, not display index");
            Assert.That(((Person)grid.SelectedItem!).Name, Is.EqualTo("Carol"));
        });
    }

    [Test]
    public void Item_changes_resort_lazily_before_the_next_paint()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(130, 5); // ascending by age

        grid.Items.Add(new Person("Dave", 1));

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(TextRowY(g, "Dave"), Is.EqualTo(22), "new youngest row sorts to the top");
            Assert.That(((Person)grid.Items[3]!).Name, Is.EqualTo("Dave"), "items keep insertion order");
        });
    }

    [Test]
    public void Clearing_the_sort_restores_items_order()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(130, 5);

        grid.Sort(null, SortOrder.None);

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(grid.SortedColumn, Is.Null);
            Assert.That(grid.SortOrder, Is.EqualTo(SortOrder.None));
            Assert.That(TextRowY(g, "Bob"), Is.EqualTo(22), "back to insertion order");
        });
    }
}
