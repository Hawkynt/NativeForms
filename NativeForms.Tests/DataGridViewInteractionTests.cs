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

    /// <summary>A grid whose first column is frozen and whose columns overflow the 200px viewport:
    /// Name (100, frozen), Age (60) and City (100) — 160px of scrolling columns behind a 100px pin.</summary>
    private static DataGridView MakeFrozenGrid()
    {
        var grid = MakeGrid();
        grid.Columns[0].Frozen = true;
        grid.Columns.Add(new DataGridViewColumn("City", static _ => "Rome"));
        return grid;
    }

    [Test]
    public void Frozen_column_stays_pinned_while_the_rest_scroll()
    {
        var grid = MakeFrozenGrid();
        var canvas = Realize(grid);
        grid.HorizontalOffset = 50;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Name\"") && o.Contains("@4,0")), Is.True, "frozen header pinned at x=0");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Alice\"") && o.Contains("@4,22")), Is.True, "frozen cell pinned");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Age\"") && o.Contains("@54,0")), Is.True, "scrolling header shifted by the offset (100 - 50 + 4)");
            Assert.That(g.Operations, Does.Contain("clip 100,0,100,22"), "scrolling headers clip to the right of the frozen run");
            Assert.That(g.Operations, Does.Contain("line #FFC8C8C8 99,0-99,110"), "frozen seam");
        });
    }

    [Test]
    public void Frozen_columns_hit_test_at_their_pinned_position()
    {
        var grid = MakeFrozenGrid();
        DataGridViewCellEventArgs? click = null;
        grid.CellClick += (_, e) => click = e;
        var canvas = Realize(grid);
        grid.HorizontalOffset = 50;

        canvas.RaiseMouseDown(50, 30); // inside the pinned Name column
        Assert.That(click!.ColumnIndex, Is.Zero, "the frozen column wins over the columns scrolled beneath it");

        canvas.RaiseMouseDown(105, 30); // Age spans 50..110 but only shows right of the 100px pin
        Assert.That(click.ColumnIndex, Is.EqualTo(1));
    }

    [Test]
    public void HorizontalOffset_clamps_against_the_scrolling_columns_only()
    {
        var grid = MakeFrozenGrid(); // 100 frozen + 160 scrolling in a 200px control => max 160 - 100 = 60
        Realize(grid);

        grid.HorizontalOffset = 10_000;

        Assert.That(grid.HorizontalOffset, Is.EqualTo(60));
    }

    [Test]
    public void Dragging_a_header_reorders_the_display_and_updates_DisplayIndex()
    {
        var grid = MakeGrid();
        grid.AllowUserToOrderColumns = true;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 5);  // grab the Name header
        canvas.RaiseMouseMove(130, 5); // drag past the Age column
        canvas.RaiseMouseUp(130, 5);

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(grid.Columns[0].DisplayIndex, Is.EqualTo(1), "Name now displays second");
            Assert.That(grid.Columns[1].DisplayIndex, Is.Zero, "Age now displays first");
            Assert.That(grid.Columns[0].HeaderText, Is.EqualTo("Name"), "the model Columns list is untouched");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Age\"") && o.Contains("@4,0")), Is.True, "Age painted first");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Name\"") && o.Contains("@64,0")), Is.True, "Name painted after Age's 60px");
        });
    }

    [Test]
    public void Header_drags_do_not_reorder_while_disallowed()
    {
        var grid = MakeGrid(); // AllowUserToOrderColumns defaults to false
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 5);
        canvas.RaiseMouseMove(130, 5);
        canvas.RaiseMouseUp(130, 5);

        Assert.Multiple(() =>
        {
            Assert.That(grid.Columns[0].DisplayIndex, Is.EqualTo(-1));
            Assert.That(grid.Columns[1].DisplayIndex, Is.EqualTo(-1));
        });
    }

    [Test]
    public void GetClipboardContent_returns_tab_separated_display_text()
    {
        var grid = MakeGrid();
        Realize(grid);

        Assert.That(grid.GetClipboardContent(), Is.Empty, "no selection, no content");

        grid.SelectedRowIndex = 1;
        Assert.That(grid.GetClipboardContent(), Is.EqualTo("Bob\t25"));
    }

    [Test]
    public void Clipboard_rows_follow_the_display_order_while_sorted()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        grid.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(130, 5); // sort ascending by age: Bob(25), Alice(30), Carol(40)

        canvas.RaiseMouseDown(10, 30);                                  // Bob (display row 0)
        canvas.RaiseMouseDown(10, 50, modifiers: KeyModifiers.Control); // + Alice (display row 1)

        Assert.That(grid.GetClipboardContent(), Is.EqualTo("Bob\t25\r\nAlice\t30"));
    }

    [Test]
    public void Ctrl_C_stores_the_selection_through_the_backend_seam()
    {
        var backend = new HeadlessBackend();
        var grid = MakeGrid();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        grid.SelectedRowIndex = 0;

        canvas.RaiseKeyDown(Keys.C, KeyModifiers.Control);

        Assert.That(backend.ClipboardTexts, Is.EqualTo(new[] { "Alice\t30" }));

        grid.SelectedRowIndex = -1;
        canvas.RaiseKeyDown(Keys.C, KeyModifiers.Control);
        Assert.That(backend.ClipboardTexts, Has.Count.EqualTo(1), "an empty selection stores nothing");
    }

    [Test]
    public void Ctrl_click_toggles_rows_into_the_multi_selection()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        var selections = 0;
        grid.SelectionChanged += (_, _) => ++selections;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 30);                                  // Alice
        canvas.RaiseMouseDown(10, 74, modifiers: KeyModifiers.Control); // + Carol

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Alice", "Carol" }));
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(2), "the toggled row becomes current");
            Assert.That(selections, Is.EqualTo(2));
        });

        canvas.RaiseMouseDown(10, 30, modifiers: KeyModifiers.Control); // - Alice
        Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Carol" }));
    }

    [Test]
    public void Shift_click_selects_a_display_range()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 30);                                // anchor on Alice
        canvas.RaiseMouseDown(10, 74, modifiers: KeyModifiers.Shift); // range to Carol

        Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Alice", "Bob", "Carol" }));
    }

    [Test]
    public void Plain_click_collapses_the_multi_selection()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(10, 30);
        canvas.RaiseMouseDown(10, 74, modifiers: KeyModifiers.Control);

        canvas.RaiseMouseDown(10, 50); // plain click on Bob

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Bob" }));
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Shift_arrow_extends_the_selection_from_the_anchor()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(10, 30); // anchor on Alice

        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift);

        Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Alice", "Bob" }));

        canvas.RaiseKeyDown(Keys.Down); // a plain arrow collapses to the new row
        Assert.That(grid.SelectedItems.Cast<Person>().Select(static p => p.Name), Is.EqualTo(new[] { "Carol" }));
    }

    [Test]
    public void Fill_columns_share_the_leftover_width_by_weight()
    {
        var grid = MakeGrid(); // 3 rows fit — the whole 200px viewport is available
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[0].FillWeight = 100f;
        grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[1].FillWeight = 300f;
        var canvas = Realize(grid);

        canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(grid.Columns[0].Width, Is.EqualTo(50), "100 of 400 weight over 200px");
            Assert.That(grid.Columns[1].Width, Is.EqualTo(150), "300 of 400 weight over 200px");
        });
    }

    [Test]
    public void Fill_columns_respect_their_minimum_width()
    {
        var grid = MakeGrid();
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[0].FillWeight = 100f;
        grid.Columns[0].MinimumWidth = 80;
        grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[1].FillWeight = 300f;
        var canvas = Realize(grid);

        canvas.RaisePaint();

        Assert.That(grid.Columns[0].Width, Is.EqualTo(80), "the 50px share is floored at MinimumWidth");
    }

    [Test]
    public void Fill_recomputes_when_the_grid_resizes()
    {
        var grid = MakeGrid(); // fixed 60px Age column, Name fills the rest
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var canvas = Realize(grid);
        canvas.RaisePaint();
        Assert.That(grid.Columns[0].Width, Is.EqualTo(140));

        grid.Bounds = new(0, 0, 300, 110);
        canvas.RaisePaint();

        Assert.That(grid.Columns[0].Width, Is.EqualTo(240), "the fill follows the wider viewport");
    }

    [Test]
    public void Resizable_False_blocks_the_divider_drag_even_when_the_grid_allows()
    {
        var grid = MakeGrid(); // AllowUserToResizeColumns defaults to true
        grid.Columns[0].Resizable = DataGridViewTriState.False;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5); // the Name/Age divider
        canvas.RaiseMouseMove(130, 5);
        canvas.RaiseMouseUp(130, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(100));
    }

    [Test]
    public void Resizable_True_overrides_a_grid_that_disallows()
    {
        var grid = MakeGrid();
        grid.AllowUserToResizeColumns = false;
        grid.Columns[0].Resizable = DataGridViewTriState.True;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5);
        canvas.RaiseMouseMove(130, 5);
        canvas.RaiseMouseUp(130, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(130));
    }

    [Test]
    public void MinimumWidth_clamps_the_user_resize()
    {
        var grid = MakeGrid();
        grid.Columns[0].MinimumWidth = 50;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(100, 5);
        canvas.RaiseMouseMove(10, 5); // would shrink to 10px
        canvas.RaiseMouseUp(10, 5);

        Assert.That(grid.Columns[0].Width, Is.EqualTo(50));
    }

    [Test]
    public void MultiSelect_gestures_run_the_row_validation_pipeline()
    {
        var grid = MakeGrid();
        grid.MultiSelect = true;
        var canvas = Realize(grid);
        canvas.RaiseMouseDown(10, 30); // row 0 becomes the current row

        var veto = true;
        var validated = 0;
        grid.RowValidating += (_, e) => e.Cancel = veto;
        grid.RowValidated += (_, _) => ++validated;

        canvas.RaiseMouseDown(10, 52, MouseButtons.Left, KeyModifiers.Control); // Ctrl-click row 1
        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.Zero, "the veto kept the current row");
            Assert.That(grid.SelectedItems.Count(), Is.EqualTo(1), "and the selection");
            Assert.That(validated, Is.Zero);
        });

        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Shift); // the extending move validates too
        Assert.That(grid.SelectedRowIndex, Is.Zero);

        veto = false;
        canvas.RaiseMouseDown(10, 52, MouseButtons.Left, KeyModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
            Assert.That(grid.SelectedItems.Count(), Is.EqualTo(2));
            Assert.That(validated, Is.EqualTo(1));
        });
    }
}
