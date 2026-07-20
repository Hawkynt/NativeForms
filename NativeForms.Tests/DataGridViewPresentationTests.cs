using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The reflection-free presentation selectors: per-row colors, heights, hidden and selectable
/// predicates on the grid; per-cell style, display-text and tooltip selectors on the column; and the
/// three-level read-only matrix. Also proves virtualization stays bounded when the selectors are on.
/// </summary>
[TestFixture]
internal sealed class DataGridViewPresentationTests
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
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        return grid;
    }

    [Test]
    public void RowBackColorSelector_tints_the_matching_row()
    {
        var grid = MakeGrid();
        grid.RowBackColorSelector = static o => ((Person)o!).Name == "Bob" ? Color.FromArgb(0xFF, 0xFF, 0x00, 0x00) : null;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("fill #FFFF0000 0,44,200,22"), "row 1 tinted");
    }

    [Test]
    public void RowHeightSelector_lays_out_following_rows_lower()
    {
        var grid = MakeGrid();
        grid.RowHeightSelector = static o => ((Person)o!).Name == "Alice" ? 40 : null;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();
        canvas.RaiseMouseDown(10, 70); // 22 + 40 = 62 .. 84 is row 1

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Bob\"") && o.Contains(",62")), Is.True, "row 1 starts below the tall row 0");
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "hit-testing honors the per-row height");
        });
    }

    [Test]
    public void RowHiddenSelector_skips_rows_in_paint_and_hit_testing()
    {
        var grid = MakeGrid();
        grid.RowHiddenSelector = static o => ((Person)o!).Name == "Bob";
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();
        canvas.RaiseMouseDown(10, 50); // second visible row (44..66) is Carol now

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Bob"), Is.False, "hidden row is not painted");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Carol\"") && o.Contains(",44")), Is.True, "Carol moves up");
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(2), "hit-testing skips the hidden row");
        });
    }

    [Test]
    public void RowSelectableSelector_blocks_mouse_selection()
    {
        var grid = MakeGrid();
        grid.RowSelectableSelector = static o => ((Person)o!).Name != "Bob";
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 50); // row 1 = Bob

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Keyboard_navigation_skips_unselectable_rows()
    {
        var grid = MakeGrid();
        grid.RowSelectableSelector = static o => ((Person)o!).Name != "Bob";
        grid.SelectedRowIndex = 0;
        var canvas = Realize(grid);

        canvas.RaiseKeyDown(Keys.Down);

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(2), "Bob is skipped");
    }

    [Test]
    public void CellStyleSelector_overrides_colors_and_alignment()
    {
        var grid = MakeGrid();
        grid.Columns[0].CellStyleSelector = static o => ((Person)o!).Name == "Bob"
            ? new(foreColor: Color.FromArgb(0xFF, 0xFF, 0x00, 0x00), backColor: Color.FromArgb(0xFF, 0x00, 0xFF, 0x00), alignment: ContentAlignment.MiddleRight)
            : default;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FF00FF00 0,44,100,22"), "cell background");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Bob\"") && o.Contains("#FFFF0000") && o.Contains("MiddleRight")), Is.True, "fore color + alignment");
        });
    }

    [Test]
    public void DisplayTextSelector_overrides_the_cell_text()
    {
        var grid = MakeGrid();
        grid.Columns[1].DisplayTextSelector = static o => $"{((Person)o!).Age} yrs";
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("30 yrs"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"30\"")), Is.False, "raw value is replaced");
        });
    }

    [Test]
    public void GetCellTooltip_returns_the_selector_text_and_null_out_of_range()
    {
        var grid = MakeGrid();
        grid.Columns[0].TooltipSelector = static o => $"tip-{((Person)o!).Name}";

        Assert.Multiple(() =>
        {
            Assert.That(grid.GetCellTooltip(0, 0), Is.EqualTo("tip-Alice"));
            Assert.That(grid.GetCellTooltip(1, 1), Is.Null, "column without a selector");
            Assert.That(grid.GetCellTooltip(9, 0), Is.Null);
            Assert.That(grid.GetCellTooltip(0, 9), Is.Null);
        });
    }

    [Test]
    public void IsCellReadOnly_honors_grid_column_and_cell_levels()
    {
        var grid = MakeGrid();
        var column = grid.Columns[0];
        var alice = grid.Items[0];
        var bob = grid.Items[1];

        Assert.That(grid.IsCellReadOnly(alice, column), Is.False, "all levels writable");

        grid.ReadOnly = true;
        Assert.That(grid.IsCellReadOnly(alice, column), Is.True, "grid level wins");
        grid.ReadOnly = false;

        column.ReadOnly = true;
        Assert.That(grid.IsCellReadOnly(alice, column), Is.True, "column level wins");
        column.ReadOnly = false;

        column.ReadOnlyCellSelector = static o => ((Person)o!).Name == "Alice";
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsCellReadOnly(alice, column), Is.True, "cell level wins");
            Assert.That(grid.IsCellReadOnly(bob, column), Is.False, "other cells stay writable");
        });
    }

    [Test]
    public void FullRowTextSelector_merges_the_row_into_one_full_width_cell()
    {
        var grid = MakeGrid();
        grid.FullRowTextSelector = static o => ((Person)o!).Name == "Bob" ? "— Section —" : null;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"— Section —\"") && o.Contains("@4,44")), Is.True, "one full-width cell");
            Assert.That(g.DrewText("Bob"), Is.False, "no per-column cells on the merged row");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"25\"")), Is.False);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Alice\"") && o.Contains(",22")), Is.True, "normal rows keep their cells");
        });
    }

    [Test]
    public void Merged_rows_are_skipped_by_selection_and_navigation()
    {
        var grid = MakeGrid();
        grid.FullRowTextSelector = static o => ((Person)o!).Name == "Bob" ? "— Section —" : null;
        var cellClicks = 0;
        grid.CellClick += (_, _) => ++cellClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(10, 50); // the merged row
        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1), "merged rows take no selection");
            Assert.That(cellClicks, Is.Zero, "and have no cells to click");
        });

        grid.SelectedRowIndex = 0;
        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(grid.SelectedRowIndex, Is.EqualTo(2), "keyboard navigation skips the merged row");
    }

    [Test]
    public void Merged_rows_refuse_editing()
    {
        var grid = MakeGrid();
        grid.Columns[0].TextSetter = static (_, _) => { };
        grid.FullRowTextSelector = static o => ((Person)o!).Name == "Bob" ? "— Section —" : null;
        Realize(grid);

        Assert.Multiple(() =>
        {
            Assert.That(grid.BeginEdit(1, 0), Is.False, "the merged row has no editable cells");
            Assert.That(grid.BeginEdit(0, 0), Is.True, "normal rows edit");
        });
    }

    [Test]
    public void Virtualization_stays_bounded_with_presentation_selectors()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });
        grid.RowHiddenSelector = static o => (((Person)o!).Age & 1) == 1; // every other row hidden
        grid.RowHeightSelector = static o => 20 + (((Person)o!).Age & 3);
        grid.RowBackColorSelector = static _ => null;
        for (var i = 0; i < 100_000; ++i)
            grid.Items.Add(new Person($"P{i}", i));
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.FindAll(o => o.StartsWith("text ")).Count;
        // 2 header cells + a handful of visible rows * 2 columns — the hidden/height selectors walk
        // linearly over the visible window only, so the op count stays tiny for 100000 rows.
        Assert.That(textOps, Is.LessThan(32), $"rendered {textOps} text ops for 100000 rows");
    }

    private static HeadlessBackend RealizeWithBackend(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend;
    }

    [Test]
    public void FormatSelector_shapes_the_display_after_the_value_selector()
    {
        var grid = MakeGrid();
        grid.Columns[1].FormatSelector = static value => $"{value} y";
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("30 y"), Is.True, "the value runs through the formatter");
            Assert.That(g.DrewText("Age"), Is.True, "headers stay unformatted");
        });
    }

    [Test]
    public void Editors_seed_from_the_raw_value_not_the_format()
    {
        var grid = MakeGrid();
        grid.Columns[1].FormatSelector = static value => $"{value} y";
        grid.Columns[1].TextSetter = static (_, _) => { };
        Realize(grid);

        grid.BeginEdit(0, 1);

        Assert.That(grid.EditingControl!.Text, Is.EqualTo("30"), "formatting is display-only");
    }

    [Test]
    public void Cell_tooltip_pops_up_after_the_hover_delay_and_auto_hides()
    {
        var grid = MakeGrid();
        grid.Columns[0].TooltipSelector = static o => ((Person)o!).Name == "Alice" ? "tip-A" : null;
        var backend = RealizeWithBackend(grid);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseMove(10, 30); // row 0, Name
        var timer = backend.Timers.Single();
        Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500 }), "armed with the tooltip initial delay");

        timer.FireTick();
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            // Cursor (10, 30) plus the shared 18px offset; "tip-A" (35x16) in 4px padding.
            Assert.That(popup.ShowCalls.Single(), Is.EqualTo((new Point(10, 48), new Size(43, 24))));
            Assert.That(popup.RaisePaint().DrewText("tip-A"), Is.True);
        });

        timer.FireTick(); // the auto-pop phase elapses
        Assert.That(popup.IsShown, Is.False);
    }

    [Test]
    public void Cell_tooltip_hides_when_the_pointer_leaves_or_presses()
    {
        var grid = MakeGrid();
        grid.Columns[0].TooltipSelector = static _ => "tip";
        var backend = RealizeWithBackend(grid);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseMove(10, 30);
        backend.Timers.Single().FireTick();
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.That(popup.IsShown, Is.True);

        canvas.RaiseMouseLeave();
        Assert.That(popup.IsShown, Is.False, "leaving the grid hides the tip");

        canvas.RaiseMouseMove(10, 30);
        backend.Timers.Single().FireTick();
        Assert.That(popup.IsShown, Is.True);

        canvas.RaiseMouseDown(10, 30);
        Assert.That(popup.IsShown, Is.False, "a press hides the tip");
    }

    [Test]
    public void Cells_without_tooltip_text_never_arm_the_delay()
    {
        var grid = MakeGrid();
        grid.Columns[0].TooltipSelector = static o => ((Person)o!).Name == "Alice" ? "tip-A" : null;
        var backend = RealizeWithBackend(grid);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseMove(10, 50); // row 1 — Bob has no tip

        Assert.That(backend.Timers, Is.Empty);
    }
}
