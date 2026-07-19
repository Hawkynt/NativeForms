using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Clickable cells (<see cref="DataGridView.CellClick"/>, <see cref="DataGridView.CellDoubleClick"/>),
/// keyboard activation, row headers, column resize dragging, per-column auto-size and the horizontal
/// scroll behavior (clamping + Shift+wheel), all driven through the headless canvas.
/// </summary>
[TestFixture]
internal sealed class DataGridViewInteractionTests
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
        // 22px header + 4 rows at 22px; columns at x 0..100 and 100..160.
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        return grid;
    }

    [Test]
    public void Clicking_a_cell_raises_CellClick_with_row_and_column()
    {
        var grid = MakeGrid();
        DataGridViewCellEventArgs? click = null;
        grid.CellClick += (_, e) => click = e;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(110, 50); // column 1, row 1

        Assert.Multiple(() =>
        {
            Assert.That(click, Is.Not.Null);
            Assert.That(click!.RowIndex, Is.EqualTo(1));
            Assert.That(click.ColumnIndex, Is.EqualTo(1));
            Assert.That(click.ContentIndex, Is.EqualTo(-1));
            Assert.That(grid.CurrentColumnIndex, Is.EqualTo(1), "clicked column becomes current");
        });
    }

    [Test]
    public void Two_quick_clicks_on_the_same_cell_raise_CellDoubleClick()
    {
        var grid = MakeGrid();
        var cellClicks = 0;
        DataGridViewCellEventArgs? doubleClick = null;
        grid.CellClick += (_, _) => ++cellClicks;
        grid.CellDoubleClick += (_, e) => doubleClick = e;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(110, 50);
        canvas.RaiseMouseDown(110, 50);

        Assert.Multiple(() =>
        {
            Assert.That(cellClicks, Is.EqualTo(2), "every press clicks");
            Assert.That(doubleClick, Is.Not.Null);
            Assert.That(doubleClick!.RowIndex, Is.EqualTo(1));
            Assert.That(doubleClick.ColumnIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clicks_on_different_cells_do_not_double_click()
    {
        var grid = MakeGrid();
        var doubleClicks = 0;
        grid.CellDoubleClick += (_, _) => ++doubleClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(110, 30); // row 0
        canvas.RaiseMouseDown(110, 50); // row 1

        Assert.That(doubleClicks, Is.Zero);
    }

    [Test]
    public void Space_raises_CellClick_on_the_current_cell()
    {
        var grid = MakeGrid();
        grid.SelectedRowIndex = 1;
        grid.CurrentColumnIndex = 1;
        DataGridViewCellEventArgs? click = null;
        grid.CellClick += (_, e) => click = e;
        var canvas = Realize(grid);

        canvas.RaiseKeyDown(Keys.Space);

        Assert.Multiple(() =>
        {
            Assert.That(click, Is.Not.Null);
            Assert.That(click!.RowIndex, Is.EqualTo(1));
            Assert.That(click.ColumnIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Enter_raises_CellClick_on_the_current_cell()
    {
        var grid = MakeGrid();
        grid.SelectedRowIndex = 2;
        DataGridViewCellEventArgs? click = null;
        grid.CellClick += (_, e) => click = e;
        var canvas = Realize(grid);

        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(click, Is.Not.Null);
            Assert.That(click!.RowIndex, Is.EqualTo(2));
            Assert.That(click.ColumnIndex, Is.Zero, "current column defaults to the first");
        });
    }

    [Test]
    public void Row_headers_paint_the_strip_and_shift_the_columns()
    {
        var grid = MakeGrid();
        grid.ShowRowHeaders = true;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFECECEC 0,0,24,110"), "row-header strip");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Name\"") && o.Contains("@28,0")), Is.True, "columns start right of the strip");
        });
    }

    [Test]
    public void Row_header_of_the_selected_row_shows_the_marker()
    {
        var grid = MakeGrid();
        grid.ShowRowHeaders = true;
        grid.SelectedRowIndex = 0;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // The marker triangle strokes with the header text color inside the 24px strip, row 0 (y 22..44).
        var markerLines = g.Operations.FindAll(o =>
        {
            if (!o.StartsWith("line #FF303030 "))
                return false;

            var coordinates = o["line #FF303030 ".Length..].Split('-')[0].Split(',');
            return int.Parse(coordinates[0]) < 24 && int.Parse(coordinates[1]) is >= 22 and < 44;
        });
        Assert.That(markerLines, Has.Count.EqualTo(4), "marker triangle");
    }

    [Test]
    public void Clicking_a_row_header_selects_the_row_without_a_cell_click()
    {
        var grid = MakeGrid();
        grid.ShowRowHeaders = true;
        var cellClicks = 0;
        grid.CellClick += (_, _) => ++cellClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 30); // inside the 24px row-header strip, row 0

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.Zero);
            Assert.That(cellClicks, Is.Zero);
        });
    }

    [Test]
    public void Dragging_a_column_divider_resizes_the_column()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5); // divider of column 0 (edge at x=100)
        canvas.RaiseMouseMove(130, 5);
        canvas.RaiseMouseUp(130, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(130));

        canvas.RaiseMouseMove(180, 5); // after mouse-up the drag is over
        Assert.That(grid.Columns[0].Width, Is.EqualTo(130));
    }

    [Test]
    public void Column_resize_clamps_at_a_minimum_width()
    {
        var grid = MakeGrid();
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5);
        canvas.RaiseMouseMove(1, 5);
        canvas.RaiseMouseUp(1, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(8));
    }

    [Test]
    public void Disabling_AllowUserToResizeColumns_ignores_divider_drags()
    {
        var grid = MakeGrid();
        grid.AllowUserToResizeColumns = false;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5);
        canvas.RaiseMouseMove(130, 5);
        canvas.RaiseMouseUp(130, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(100));
    }

    [Test]
    public void AutoSize_AllCells_fits_the_widest_visible_cell()
    {
        var grid = MakeGrid();
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        var canvas = Realize(grid);

        canvas.RaisePaint();

        // Widest visible value "Alice"/"Carol" measures 5*7=35px, plus 2*4 padding.
        Assert.That(grid.Columns[0].Width, Is.EqualTo(43));
    }

    [Test]
    public void HorizontalOffset_clamps_to_the_total_column_width()
    {
        var grid = MakeGrid(); // columns 100+60 in a 200px control: nothing to scroll
        Realize(grid);

        grid.HorizontalOffset = 50;
        Assert.That(grid.HorizontalOffset, Is.Zero, "no overflow, no scrolling");

        grid.Columns[0].Width = 300; // total 360, viewport 200 => max 160
        grid.HorizontalOffset = 10_000;
        Assert.That(grid.HorizontalOffset, Is.EqualTo(160));
    }

    [Test]
    public void Shift_wheel_scrolls_horizontally_and_clamps_at_zero()
    {
        var grid = MakeGrid();
        grid.Columns[0].Width = 300; // total 360 => horizontally scrollable
        var canvas = Realize(grid);

        canvas.RaiseMouseWheel(-120, modifiers: KeyModifiers.Shift);

        Assert.Multiple(() =>
        {
            Assert.That(grid.HorizontalOffset, Is.EqualTo(30), "wheel down scrolls right");
            Assert.That(grid.TopRow, Is.Zero, "vertical position is untouched");
        });

        canvas.RaiseMouseWheel(120, modifiers: KeyModifiers.Shift);
        canvas.RaiseMouseWheel(120, modifiers: KeyModifiers.Shift);
        Assert.That(grid.HorizontalOffset, Is.Zero, "clamped at the left edge");
    }
}
